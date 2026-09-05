<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, ref, watch } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import {
  CircleClose, Connection, CopyDocument, Delete, Download, FullScreen, FolderOpened, Aim, Plus, Promotion, Refresh, Search, Setting, Star, Switch, Upload, Warning,
} from '@element-plus/icons-vue'
import {
  addGitRepo, discoverGitRepos, getGitBranches, getGitChanges, getGitCommitDetail, getGitConfigList, getGitEnv, getGitFileDiff, getGitLog,
  getGitMergeState, getGitRepos, getGitReposHealth, getGitStatus, gitCancel, gitCheckout, gitClone, gitCommitChanges, gitConfigSet, gitDiscard,
  gitExec, gitListRemoteBranches, gitMerge, gitPull, gitPush, gitStage, gitStash, getGitStashList, gitStashPop, gitStashDrop, removeGitRepo,
  updateRepoGroup,
  type GitBranch, type GitChangeFile, type GitCommandResult, type GitCommitDetail, type GitConfigItem, type GitDiffFile,
  type GitDiscoveredRepo, type GitEnv, type GitFileDiff, type GitLogEntry, type GitMergeState, type GitRepoStatus, type GitStashEntry,
} from '@/common/api/git'
import { openOutputFolder, selectFolder } from '@/common/api/universalBuild'
import { GIT_CATEGORY_ALL, gitCategories, gitCommands, type GitCommandEntry } from '@/common/data/gitCommands'

// ============================ 仓库列表 ============================

const repos = ref<GitRepoStatus[]>([])
const reposLoading = ref(false)

// ============================ 搜索 & 置顶 ============================

const repoSearch = ref('')

/** 置顶路径集合（存 localStorage，刷新保留） */
const PINS_KEY = 'git-workbench-pins'
function loadPins(): string[] {
  try { return JSON.parse(localStorage.getItem(PINS_KEY) ?? '[]') } catch { return [] }
}
const pinnedPaths = ref<string[]>(loadPins())

function togglePin(path: string) {
  const i = pinnedPaths.value.indexOf(path)
  if (i >= 0) pinnedPaths.value.splice(i, 1)
  else pinnedPaths.value.push(path)
  localStorage.setItem(PINS_KEY, JSON.stringify(pinnedPaths.value))
}

// ============================ 分组 ============================

const GROUP_ORDER_KEY = 'git-workbench-group-order'
const GROUP_EXPAND_KEY = 'git-workbench-group-expand'
const REPO_ORDER_KEY = 'git-workbench-repo-order'

function loadGroupOrder(): string[] {
  try { return JSON.parse(localStorage.getItem(GROUP_ORDER_KEY) ?? '[]') } catch { return [] }
}
function saveGroupOrder(order: string[]) {
  localStorage.setItem(GROUP_ORDER_KEY, JSON.stringify(order))
}
function loadGroupExpand(): Record<string, boolean> {
  try { return JSON.parse(localStorage.getItem(GROUP_EXPAND_KEY) ?? '{}') } catch { return {} }
}
function loadRepoOrder(): string[] {
  try { return JSON.parse(localStorage.getItem(REPO_ORDER_KEY) ?? '[]') } catch { return [] }
}
function saveRepoOrder(order: string[]) {
  localStorage.setItem(REPO_ORDER_KEY, JSON.stringify(order))
}
const groupOrder = ref<string[]>(loadGroupOrder())
const groupExpand = ref<Record<string, boolean>>(loadGroupExpand())

function toggleGroupExpand(group: string) {
  groupExpand.value[group] = !groupExpand.value[group]
  localStorage.setItem(GROUP_EXPAND_KEY, JSON.stringify(groupExpand.value))
}

/** 过滤后的仓库按分组归类（无分组仓库不在此列，一级平铺） */
const groupedRepos = computed(() => {
  const q = repoSearch.value.trim().toLowerCase()
  const list = q
    ? repos.value.filter(r => r.name.toLowerCase().includes(q) || r.path.toLowerCase().includes(q))
    : repos.value

  // 按自定义顺序排序（置顶前置，其余按用户拖拽顺序）
  const pinned = pinnedPaths.value
  const repoOrd = loadRepoOrder()
  const sorted = [...list].sort((a, b) => {
    const ap = pinned.includes(a.path) ? 0 : 1
    const bp = pinned.includes(b.path) ? 0 : 1
    if (ap !== bp) return ap - bp
    const ai = repoOrd.indexOf(a.path)
    const bi = repoOrd.indexOf(b.path)
    return (ai >= 0 ? ai : 9999) - (bi >= 0 ? bi : 9999)
  })

  // 按 group 分组（无分组仓库跳过，与分组同级一级显示）
  const map = new Map<string, typeof sorted>()
  for (const r of sorted) {
    const g = r.group || ''
    if (!g) continue
    if (!map.has(g)) map.set(g, [])
    map.get(g)!.push(r)
  }

  // 非搜索状态：groupOrder 中已创建但暂无仓库的空分组也显示（新建分组后可见，拖仓库进入）
  if (!q) {
    for (const g of groupOrder.value) {
      if (g && !map.has(g)) map.set(g, [])
    }
  }

  // 按 groupOrder 排序，未排序的分组追加到末尾
  const order = groupOrder.value
  const groups = [...map.keys()].sort((a, b) => {
    const ai = order.indexOf(a)
    const bi = order.indexOf(b)
    return (ai >= 0 ? ai : 9999) - (bi >= 0 ? bi : 9999)
  })

  return groups.map(g => ({
    name: g,
    label: g,
    repos: map.get(g)!,
    expanded: groupExpand.value[g] !== false, // 默认展开
  }))
})

/** 无分组仓库：与分组同级一级平铺（置顶前置 + 自定义排序） */
const ungroupedRepos = computed(() => {
  const q = repoSearch.value.trim().toLowerCase()
  const list = q
    ? repos.value.filter(r => r.name.toLowerCase().includes(q) || r.path.toLowerCase().includes(q))
    : repos.value
  const pinned = pinnedPaths.value
  const repoOrd = loadRepoOrder()
  return list.filter(r => !r.group).sort((a, b) => {
    const ap = pinned.includes(a.path) ? 0 : 1
    const bp = pinned.includes(b.path) ? 0 : 1
    if (ap !== bp) return ap - bp
    const ai = repoOrd.indexOf(a.path)
    const bi = repoOrd.indexOf(b.path)
    return (ai >= 0 ? ai : 9999) - (bi >= 0 ? bi : 9999)
  })
})

const filteredRepos = computed(() => repos.value)

/** 搜索结果中是否同时存在置顶和普通仓库（用于显示分隔线） */
const hasPinDivider = computed(() => false)

// ============================ 全屏 ============================

const changesFullscreen = ref(false)
const historyFullscreen = ref(false)

function toggleChangesFullscreen() { changesFullscreen.value = !changesFullscreen.value }
function toggleHistoryFullscreen() { historyFullscreen.value = !historyFullscreen.value }

function onGlobalKeydown(e: KeyboardEvent) {
  if (e.key === 'Escape') {
    changesFullscreen.value = false
    historyFullscreen.value = false
    contextMenu.value.visible = false
    return
  }
  // Ctrl+Enter：提交已暂存改动
  if (e.ctrlKey && !e.shiftKey && e.key === 'Enter') {
    if (activeTab.value === 'changes' && commitMessage.value.trim() && changesStaged.value.length > 0 && !committing.value && !runningOp.value) {
      e.preventDefault()
      void commitStaged()
    }
    return
  }
  // Ctrl+Shift+A：暂存所有未暂存文件
  if (e.ctrlKey && e.shiftKey && e.key === 'A') {
    if (activeTab.value === 'changes' && changesUnstaged.value.length > 0 && !runningOp.value) {
      e.preventDefault()
      void stageFile(null, true)
    }
    return
  }
}

// ============================ 右键菜单 ============================

interface ContextMenuItem {
  label: string
  icon?: string          // emoji/文字图标
  danger?: boolean       // 红色危险项
  divider?: boolean      // 在本项上方显示分隔线
  disabled?: boolean
  children?: ContextMenuItem[] // 子菜单（悬停展开，如「移动到分组」）
  action?: () => void
}

const contextMenu = ref<{
  visible: boolean
  x: number
  y: number
  items: ContextMenuItem[]
}>({ visible: false, x: 0, y: 0, items: [] })

function showContextMenu(e: MouseEvent, items: ContextMenuItem[]) {
  e.preventDefault()
  e.stopPropagation()
  contextMenu.value = { visible: true, x: e.clientX, y: e.clientY, items }
}

function hideContextMenu() {
  contextMenu.value.visible = false
}

function runMenuItem(item: ContextMenuItem) {
  if (item.disabled || !item.action) return
  hideContextMenu()
  item.action()
}

/** 仓库项右键菜单（分组内 / 一级仓库共用） */
function repoMenuItems(repo: GitRepoStatus): ContextMenuItem[] {
  const pinned = pinnedPaths.value.includes(repo.path)
  return [
    { icon: pinned ? '★' : '☆', label: pinned ? '取消置顶' : '置顶', action: () => togglePin(repo.path) },
    { icon: '📁', label: '移动到分组', children: groupMenuItems(repo) },
    { icon: '↓', label: '拉取', divider: true, disabled: !repo.isRepo || !!runningOp, action: () => { selectRepo(repo.path); pullCurrent() } },
    { icon: '↑', label: '推送', disabled: !repo.isRepo || !!runningOp, action: () => { selectRepo(repo.path); pushCurrent() } },
    { icon: '🔀', label: '合并', disabled: !repo.isRepo || !!runningOp, action: () => { selectRepo(repo.path); nextTick(openMergeDialog) } },
    { icon: '⎇', label: '分支管理', disabled: !repo.isRepo, action: () => { selectRepo(repo.path); nextTick(openBranchDrawer) } },
    { icon: '📂', label: '在资源管理器打开', divider: true, disabled: !repo.isRepo, action: () => { selectRepo(repo.path); nextTick(openRepoFolder) } },
    { icon: '📋', label: '复制路径', action: () => copyText(repo.path, '路径已复制') },
    { icon: '🗑️', label: '移除仓库', divider: true, danger: true, action: () => { selectRepo(repo.path); nextTick(removeRepoConfirm) } },
  ]
}

function onDocumentClick() {
  hideContextMenu()
}
/** 当前选中仓库路径（唯一标识） */
const currentPath = ref('')
const current = computed(() => repos.value.find(r => r.path === currentPath.value))

async function loadRepos(silent = false) {
  if (!silent) reposLoading.value = true
  try {
    repos.value = await getGitRepos({ silent })
    // 当前选中失效（被移除/目录消失）时自动切到第一个
    if (currentPath.value && !repos.value.some(r => r.path === currentPath.value)) {
      currentPath.value = repos.value[0]?.path ?? ''
    }
  } catch {
    // 拦截器已弹错误提示
  } finally {
    reposLoading.value = false
  }
}

function selectRepo(path: string) {
  currentPath.value = path
}

/** 刷新当前仓库状态（概览条 + 侧栏项同步更新，含合并中间状态） */
async function refreshCurrent() {
  if (!currentPath.value) return
  try {
    const status = await getGitStatus({ path: currentPath.value }, { silent: true })
    const idx = repos.value.findIndex(r => r.path === currentPath.value)
    if (idx >= 0) {
      // 保留本地维护的字段（group 由后端 Repos 接口填充，GetStatus 不返回）
      const preservedGroup = repos.value[idx].group
      repos.value[idx] = { ...status, group: preservedGroup }
    }
  } catch {
    // 静默：状态刷新失败不打扰
  }
  void loadMergeState()
}

// ============================ 实时监听（轮询 + 焦点回归） ============================

/** 工作区轮询定时器 */
let changesPollingTimer: ReturnType<typeof setInterval> | null = null
const CHANGES_POLL_INTERVAL = 8000

/** 仓库健康检查轮询（检测目录被外部删除/移动，失效打标记） */
let healthPollingTimer: ReturnType<typeof setInterval> | null = null
const HEALTH_POLL_INTERVAL = 15000

async function checkReposHealth() {
  if (reposLoading.value || repos.value.length === 0) return
  try {
    const health = await getGitReposHealth()
    let hasRecovered = false
    for (const repo of repos.value) {
      const ok = health[repo.path]
      if (ok === undefined) continue
      if (ok && !repo.isRepo) {
        // 失效→恢复（目录移回来了）：需要拿完整状态（分支/领先落后等）
        hasRecovered = true
      } else if (!ok && repo.isRepo) {
        repo.isRepo = false
        repo.message = '目录不存在或已被移动'
      }
    }
    if (hasRecovered) {
      await loadRepos(true)  // silent
      if (currentPath.value) void refreshCurrent()
    }
  } catch {
    // 静默：健康检查失败不打扰
  }
}

function startHealthPolling() {
  if (healthPollingTimer) return
  healthPollingTimer = setInterval(() => { void checkReposHealth() }, HEALTH_POLL_INTERVAL)
}

function stopHealthPolling() {
  if (healthPollingTimer) {
    clearInterval(healthPollingTimer)
    healthPollingTimer = null
  }
}

function startChangesPolling() {
  if (changesPollingTimer) return
  changesPollingTimer = setInterval(() => {
    if (activeTab.value === 'changes' && currentPath.value && !changesLoading.value) {
      void reloadChanges(true)  // silent
      void refreshCurrent()
    }
  }, CHANGES_POLL_INTERVAL)
}

function stopChangesPolling() {
  if (changesPollingTimer) {
    clearInterval(changesPollingTimer)
    changesPollingTimer = null
  }
}

/** 窗口获得焦点时刷新（节流：3s 内不重复触发，debounce 合并连续事件） */
const FOCUS_THROTTLE_MS = 3000
let lastFocusRefresh = 0
let focusDebounceTimer: ReturnType<typeof setTimeout> | null = null

function onWindowFocus() {
  if (focusDebounceTimer) clearTimeout(focusDebounceTimer)
  focusDebounceTimer = setTimeout(() => {
    focusDebounceTimer = null
    const now = Date.now()
    if (now - lastFocusRefresh < FOCUS_THROTTLE_MS) return
    lastFocusRefresh = now
    void checkReposHealth()
    if (!currentPath.value) return
    void refreshCurrent()
    if (activeTab.value === 'changes') void reloadChanges(true)  // silent
  }, 300)
}

onMounted(async () => {
  await loadRepos()
  if (repos.value.length > 0 && !currentPath.value) {
    currentPath.value = repos.value[0].path
  }
  void checkEnv()
  document.addEventListener('keydown', onGlobalKeydown)
  document.addEventListener('click', onDocumentClick)
  window.addEventListener('focus', onWindowFocus)
  startHealthPolling()
})

onUnmounted(() => {
  document.removeEventListener('keydown', onGlobalKeydown)
  document.removeEventListener('click', onDocumentClick)
  window.removeEventListener('focus', onWindowFocus)
  stopChangesPolling()
  stopHealthPolling()
  if (focusDebounceTimer) clearTimeout(focusDebounceTimer)
})

// ============================ 添加 / 移除仓库 ============================

/** 添加流程：选文件夹 → 本身是仓库直接加；不是则扫描一级子目录弹窗勾选批量加 */
async function addRepoFlow() {
  const path = await selectFolder()
  if (!path) return

  try {
    const res = await addGitRepo({ path })
    if (res.ok && res.status) {
      await loadRepos()
      currentPath.value = res.status.path
      appendLog('ok', `[${res.status.name}] 已添加仓库（${res.status.branch || '未知分支'}）`)
      ElMessage.success('仓库已添加')
      return
    }
  } catch {
    // 本身不是仓库：继续尝试扫描子目录（AddRepo 的错误提示已由拦截器弹出）
    try {
      const found = await discoverGitRepos({ path })
      if (found.length > 0) {
        discoverPath.value = path
        discovered.value = found
        discoverChecked.value = new Set(found.map(f => f.path))
        discoverVisible.value = true
        return
      }
      ElMessage.warning('该目录及其一级子目录下都没有 Git 仓库')
    } catch {
      // 扫描失败，拦截器已提示
    }
  }
}

// ============================ 分组管理 ============================

/** 获取所有已有的分组名（含空分组，去重） */
const existingGroups = computed(() => {
  const set = new Set<string>()
  for (const r of repos.value) if (r.group) set.add(r.group)
  for (const g of groupOrder.value) if (g) set.add(g)
  return [...set].sort()
})

/** 实际执行仓库移动到分组（API + 本地状态 + groupOrder 维护） */
async function applyRepoGroup(path: string, group: string) {
  await updateRepoGroup({ path, group })
  const repo = repos.value.find(r => r.path === path)
  if (repo) repo.group = group
  if (group && !groupOrder.value.includes(group)) {
    groupOrder.value.push(group)
    saveGroupOrder(groupOrder.value)
  }
}

/** 移动仓库到指定分组（带成功提示，子菜单/拖拽共用） */
async function moveRepoTo(path: string, group: string) {
  try {
    await applyRepoGroup(path, group)
    ElMessage.success(group ? `已移动到「${group}」` : '已移出分组（一级显示）')
  } catch { /* 拦截器已提示 */ }
}

/** 新建分组并把仓库移入（子菜单「新建分组…」入口） */
async function moveRepoToNewGroup(path: string) {
  try {
    const { value } = await ElMessageBox.prompt('输入新分组名称，创建后仓库将移入该分组', '新建分组并移入', {
      confirmButtonText: '创建并移入',
      cancelButtonText: '取消',
      inputPlaceholder: '新分组名',
      inputValidator: (v: string) => {
        const name = (v ?? '').trim()
        if (!name) return '分组名不能为空'
        if (existingGroups.value.includes(name)) return '分组已存在（请从子菜单直接选择）'
        return true
      },
    })
    const name = (value ?? '').trim()
    if (name) await moveRepoTo(path, name)
  } catch { /* 用户取消 */ }
}

/** 「移动到分组」子菜单：直接列出所有分组，免手输 */
function groupMenuItems(repo: GitRepoStatus): ContextMenuItem[] {
  const currentGroup = repo.group || ''
  const items: ContextMenuItem[] = existingGroups.value
    .filter(g => g !== currentGroup)
    .map(g => ({ label: g, icon: '▸', action: () => { void moveRepoTo(repo.path, g) } }))
  if (currentGroup) {
    items.push({ label: '移出分组（一级显示）', icon: '←', divider: true, action: () => { void moveRepoTo(repo.path, '') } })
  }
  items.push({ label: '新建分组…', icon: '＋', divider: true, action: () => { void moveRepoToNewGroup(repo.path) } })
  return items
}

/** 新建空分组（仅记录到 groupOrder，无仓库时也显示） */
async function createGroup() {
  try {
    const { value } = await ElMessageBox.prompt('输入新分组名称，创建后可拖拽仓库进入', '新建分组', {
      confirmButtonText: '创建',
      cancelButtonText: '取消',
      inputPlaceholder: '新分组名',
      inputValidator: (v: string) => {
        const name = (v ?? '').trim()
        if (!name) return '分组名不能为空'
        if (name === '未分组') return '不能使用保留名「未分组」'
        if (existingGroups.value.includes(name)) return '分组已存在'
        return true
      },
    })
    const name = (value ?? '').trim()
    groupOrder.value.push(name)
    saveGroupOrder(groupOrder.value)
    groupExpand.value[name] = true
    localStorage.setItem(GROUP_EXPAND_KEY, JSON.stringify(groupExpand.value))
    ElMessage.success(`分组「${name}」已创建，可拖拽仓库进入`)
  } catch { /* 用户取消 */ }
}

/** 重命名分组：批量更新组内仓库 + 同步排序/展开状态 */
async function renameGroup(oldName: string) {
  try {
    const { value } = await ElMessageBox.prompt(`将分组「${oldName}」重命名为：`, '重命名分组', {
      confirmButtonText: '重命名',
      cancelButtonText: '取消',
      inputValue: oldName,
      inputValidator: (v: string) => {
        const name = (v ?? '').trim()
        if (!name) return '分组名不能为空'
        if (name === '未分组') return '不能使用保留名「未分组」'
        if (name === oldName) return '名称未变化'
        if (existingGroups.value.includes(name)) return '目标分组已存在'
        return true
      },
    })
    const newName = (value ?? '').trim()
    const members = repos.value.filter(r => (r.group || '') === oldName)
    for (const r of members) {
      await updateRepoGroup({ path: r.path, group: newName })
      r.group = newName
    }
    // 同步 groupOrder（保持原位置）
    const idx = groupOrder.value.indexOf(oldName)
    if (idx >= 0) groupOrder.value[idx] = newName
    else groupOrder.value.push(newName)
    saveGroupOrder(groupOrder.value)
    // 同步展开状态
    if (oldName in groupExpand.value) {
      groupExpand.value[newName] = groupExpand.value[oldName]
      delete groupExpand.value[oldName]
      localStorage.setItem(GROUP_EXPAND_KEY, JSON.stringify(groupExpand.value))
    }
    ElMessage.success(`已重命名为「${newName}」（${members.length} 个仓库）`)
  } catch { /* 用户取消 */ }
}

/** 解散分组：组内仓库全部移到未分组 */
async function dissolveGroup(name: string) {
  const members = repos.value.filter(r => (r.group || '') === name)
  const tip = members.length > 0
    ? `分组「${name}」下有 ${members.length} 个仓库，解散后将全部移到未分组。`
    : `确认解散空分组「${name}」？`
  try {
    await ElMessageBox.confirm(tip, '解散分组', { type: 'warning', confirmButtonText: '解散', cancelButtonText: '取消' })
  } catch { return }
  for (const r of members) {
    await updateRepoGroup({ path: r.path, group: '' })
    r.group = ''
  }
  groupOrder.value = groupOrder.value.filter(g => g !== name)
  saveGroupOrder(groupOrder.value)
  delete groupExpand.value[name]
  localStorage.setItem(GROUP_EXPAND_KEY, JSON.stringify(groupExpand.value))
}

// ---------- 拖拽仓库到分组 ----------

/** 拖拽中：当前悬停的分组名（高亮放置目标） */
const dragOverGroup = ref('')
/** 拖拽中的仓库路径 */
let draggingRepoPath = ''

/** 拖拽中状态（响应式，供「移出分组」放置区显隐） */
const isDraggingRepo = ref(false)
/** 拖拽中的仓库是否来自分组（决定是否显示「移出分组」放置区） */
const draggingRepoFromGroup = ref(false)

function onRepoDragStart(e: DragEvent, path: string) {
  draggingRepoPath = path
  isDraggingRepo.value = true
  draggingRepoFromGroup.value = !!repos.value.find(r => r.path === path)?.group
  if (e.dataTransfer) {
    e.dataTransfer.setData('text/plain', path)
    e.dataTransfer.effectAllowed = 'move'
  }
}

function onRepoDragEnd() {
  draggingRepoPath = ''
  isDraggingRepo.value = false
  dragOverGroup.value = ''
  dragOverRepoPath.value = ''
  repoInsertAbove.value = false
}

// ---------- 拖拽仓库改变顺序（组内 / 一级区域） ----------

/** 拖拽中：悬停的目标仓库路径 */
const dragOverRepoPath = ref('')
/** 拖拽中：插入方向（true=上方，false=下方） */
const repoInsertAbove = ref(false)

/** 拖拽仓库 over 另一个仓库项：计算插入位置，显示指示线 */
function onRepoItemDragOver(e: DragEvent, targetPath: string) {
  if (!draggingRepoPath || draggingRepoPath === targetPath) return
  e.preventDefault()
  if (e.dataTransfer) e.dataTransfer.dropEffect = 'move'
  const el = e.currentTarget as HTMLElement
  const rect = el.getBoundingClientRect()
  const isAbove = (e.clientY - rect.top) < rect.height / 2
  if (dragOverRepoPath.value !== targetPath || repoInsertAbove.value !== isAbove) {
    dragOverRepoPath.value = targetPath
    repoInsertAbove.value = isAbove
  }
}

function onRepoItemDragLeave() {
  if (dragOverRepoPath.value) {
    dragOverRepoPath.value = ''
  }
}

/** 拖拽仓库 drop 到另一个仓库项：组内重新排序 */
function onRepoItemDrop(e: DragEvent, targetPath: string) {
  e.preventDefault()
  const srcPath = e.dataTransfer?.getData('text/plain') || draggingRepoPath
  dragOverRepoPath.value = ''
  repoInsertAbove.value = false
  if (!srcPath || srcPath === targetPath) return

  const srcRepo = repos.value.find(r => r.path === srcPath)
  const tgtRepo = repos.value.find(r => r.path === targetPath)
  if (!srcRepo || !tgtRepo) return
  // 只允许同组内排序（组间移动用拖到分组 header / 放置区）
  if ((srcRepo.group || '') !== (tgtRepo.group || '')) return

  const order = loadRepoOrder()
  // 确保两个路径都在 order 中
  if (!order.includes(srcPath)) order.push(srcPath)
  if (!order.includes(targetPath)) order.push(targetPath)

  const srcIdx = order.indexOf(srcPath)
  order.splice(srcIdx, 1)
  let tgtIdx = order.indexOf(targetPath)
  // 插入到目标上方或下方
  const insertIdx = repoInsertAbove.value ? tgtIdx : tgtIdx + 1
  order.splice(insertIdx, 0, srcPath)

  saveRepoOrder(order)
}

function onGroupDragOver(e: DragEvent, group: string) {
  if (!draggingRepoPath) return
  e.preventDefault()
  if (e.dataTransfer) e.dataTransfer.dropEffect = 'move'
  dragOverGroup.value = group
}

function onGroupDragLeave(e: DragEvent, group: string) {
  if (dragOverGroup.value !== group) return
  // 子元素间移动不取消高亮
  const el = e.currentTarget as HTMLElement
  if (e.relatedTarget && el.contains(e.relatedTarget as Node)) return
  dragOverGroup.value = ''
}

async function onGroupDrop(e: DragEvent, group: string) {
  e.preventDefault()
  const path = e.dataTransfer?.getData('text/plain') || draggingRepoPath
  dragOverGroup.value = ''
  draggingRepoPath = ''
  if (!path) return
  const repo = repos.value.find(r => r.path === path)
  if (!repo || (repo.group || '') === group) return
  try {
    await applyRepoGroup(path, group)
    // 目标分组折叠时自动展开，让用户看到移动结果
    if (group && groupExpand.value[group] === false) {
      groupExpand.value[group] = true
      localStorage.setItem(GROUP_EXPAND_KEY, JSON.stringify(groupExpand.value))
    }
    ElMessage.success(`已移动到「${group || '未分组'}」`)
  } catch {
    // 拦截器已提示
  }
}

// ---------- 移出分组放置区（拖仓库到此处变一级仓库） ----------

const UNGROUP_ZONE_KEY = '__ungroup__'

function onUngroupZoneDragOver(e: DragEvent) {
  if (!draggingRepoPath) return
  e.preventDefault()
  if (e.dataTransfer) e.dataTransfer.dropEffect = 'move'
  dragOverGroup.value = UNGROUP_ZONE_KEY
}

function onUngroupZoneDragLeave() {
  if (dragOverGroup.value === UNGROUP_ZONE_KEY) dragOverGroup.value = ''
}

async function onUngroupZoneDrop(e: DragEvent) {
  e.preventDefault()
  const path = e.dataTransfer?.getData('text/plain') || draggingRepoPath
  dragOverGroup.value = ''
  draggingRepoPath = ''
  isDraggingRepo.value = false
  if (!path) return
  const repo = repos.value.find(r => r.path === path)
  if (!repo || !repo.group) return
  try {
    await applyRepoGroup(path, '')
    ElMessage.success('已移出分组（一级显示）')
  } catch {
    // 拦截器已提示
  }
}

// ---------- 分组拖拽排序 ----------

/** 拖拽中的分组名（排序用） */
let draggingGroupName = ''

function onGroupHeaderDragStart(e: DragEvent, name: string) {
  // 未分组固定排最后，不参与排序拖拽
  if (!name) {
    e.preventDefault()
    return
  }
  draggingGroupName = name
  if (e.dataTransfer) {
    e.dataTransfer.setData('text/group-name', name)
    e.dataTransfer.effectAllowed = 'move'
  }
}

function onGroupHeaderDragEnd() {
  draggingGroupName = ''
  dragOverGroup.value = ''
}

function onGroupHeaderDragOver(e: DragEvent, group: string) {
  if (!draggingGroupName || draggingGroupName === group) return
  e.preventDefault()
  if (e.dataTransfer) e.dataTransfer.dropEffect = 'move'
  dragOverGroup.value = group
}

function onGroupHeaderDrop(e: DragEvent, group: string) {
  e.preventDefault()
  const name = draggingGroupName
  draggingGroupName = ''
  dragOverGroup.value = ''
  if (!name || name === group) return
  // groupOrder 重排：插入目标分组当前位置
  const order = groupOrder.value
  const from = order.indexOf(name)
  if (from < 0) return
  order.splice(from, 1)
  const to = order.indexOf(group)
  order.splice(to < 0 ? order.length : to, 0, name)
  saveGroupOrder(order)
}

/** header 统一 dragover 分流：仓库拖入移组 or 分组拖动排序 */
function onHeaderDragOver(e: DragEvent, group: string) {
  if (draggingRepoPath) { onGroupDragOver(e, group); return }
  onGroupHeaderDragOver(e, group)
}

/** header 统一 drop 分流：仓库拖入移组 or 分组拖动排序 */
function onHeaderDrop(e: DragEvent, group: string) {
  if (draggingGroupName) { onGroupHeaderDrop(e, group); return }
  void onGroupDrop(e, group)
}

// ============================ 克隆仓库 ============================

const cloneVisible = ref(false)
const cloneUrl = ref('')
const cloneParentDir = ref('')
const cloneDirName = ref('')
const cloneLoading = ref(false)
/** 上次克隆保存位置（非敏感，可记住） */
const CLONE_PARENT_KEY = 'git-clone-parent-v1'

// 克隆增强：分支选择 + 高级选项 + 分组
const cloneBranches = ref<string[]>([])
const cloneBranch = ref('')
const cloneBranchLoading = ref(false)
const cloneShallow = ref(false)
const cloneRecurseSubmodules = ref(false)
const cloneNoCheckout = ref(false)
const cloneFilter = ref('')
const cloneOriginName = ref('')
const cloneGroup = ref('')
const cloneAdvancedExpand = ref(false)
let cloneBranchTimer: ReturnType<typeof setTimeout> | null = null

/** 防抖拉取远程分支 */
function fetchRemoteBranches(url: string) {
  if (cloneBranchTimer) clearTimeout(cloneBranchTimer)
  if (!url.trim() || (!url.startsWith('http') && !url.startsWith('git@') && !url.startsWith('ssh://'))) return
  cloneBranchTimer = setTimeout(async () => {
    cloneBranchLoading.value = true
    try {
      cloneBranches.value = await gitListRemoteBranches({ url: url.trim() })
      // 默认选中 main 或 master
      const preferred = cloneBranches.value.find(b => b === 'main' || b === 'master')
      cloneBranch.value = preferred || cloneBranches.value[0] || ''
    } catch {
      cloneBranches.value = []
      cloneBranch.value = ''
    } finally {
      cloneBranchLoading.value = false
    }
  }, 800)
}

/** 从仓库地址推断目录名：去查询串与尾斜杠后取末段，再去掉 .git 后缀 */
function inferDirName(url: string): string {
  const tail = url.split(/[?#]/)[0].replace(/\/+$/, '')
  const last = tail.split('/').pop() || ''
  return last.toLowerCase().endsWith('.git') ? last.slice(0, -4) : last
}

// 地址变化时自动推断目录名 + 防抖拉取远程分支
watch(cloneUrl, (url) => {
  cloneDirName.value = inferDirName(url.trim())
  fetchRemoteBranches(url.trim())
})

function openCloneDialog() {
  cloneVisible.value = true
  cloneUrl.value = ''
  cloneDirName.value = ''
  cloneBranches.value = []
  cloneBranch.value = ''
  cloneShallow.value = false
  cloneRecurseSubmodules.value = false
  cloneNoCheckout.value = false
  cloneFilter.value = ''
  cloneOriginName.value = ''
  cloneGroup.value = ''
  cloneAdvancedExpand.value = false
  try {
    cloneParentDir.value = localStorage.getItem(CLONE_PARENT_KEY) || ''
  } catch {
    cloneParentDir.value = ''
  }
}

async function chooseCloneParent() {
  const path = await selectFolder()
  if (path) cloneParentDir.value = path
}

/** 实时预览将执行的 git clone 命令 */
const cloneCommandPreview = computed(() => {
  const url = cloneUrl.value.trim()
  if (!url) return ''
  const parts = ['git clone']
  if (cloneBranch.value) { parts.push('--branch', cloneBranch.value) }
  if (cloneBranch.value) parts.push('--single-branch')
  if (cloneShallow.value) parts.push('--depth 1')
  if (cloneRecurseSubmodules.value) parts.push('--recurse-submodules')
  if (cloneNoCheckout.value) parts.push('--no-checkout')
  if (cloneFilter.value.trim()) parts.push('--filter', cloneFilter.value.trim())
  if (cloneOriginName.value.trim()) parts.push('--origin', cloneOriginName.value.trim())
  parts.push(url)
  const name = cloneDirName.value.trim()
  if (name) parts.push(name)
  return parts.join(' ')
})

async function confirmClone() {
  const url = cloneUrl.value.trim()
  const parent = cloneParentDir.value.trim()
  const name = cloneDirName.value.trim()
  if (!url) {
    ElMessage.warning('请输入仓库地址')
    return
  }
  if (!parent) {
    ElMessage.warning('请选择保存位置')
    return
  }
  try {
    localStorage.setItem(CLONE_PARENT_KEY, parent)
  } catch {
    /* 记不住不影响克隆 */
  }

  const op: RunningOp = { opId: newOpId(), label: 'git clone' }
  runningOp.value = op
  cloneLoading.value = true
  activeTab.value = 'log'
  appendLog('cmd', `$ ${cloneCommandPreview.value}`)
  appendLog('out', '克隆中…大仓库耗时较长（可随时取消）')
  const before = new Set(repos.value.map(r => r.path))
  try {
    const result = await gitClone({
      url, parentDir: parent, dirName: name, opId: op.opId,
      branch: cloneBranch.value || undefined,
      singleBranch: cloneBranch.value ? true : undefined,
      depth: cloneShallow.value ? 1 : undefined,
      recurseSubmodules: cloneRecurseSubmodules.value || undefined,
      noCheckout: cloneNoCheckout.value || undefined,
      filter: cloneFilter.value.trim() || undefined,
      originName: cloneOriginName.value.trim() || undefined,
      group: cloneGroup.value.trim() || undefined,
    })
    appendResult(result)
    if (result.success) {
      appendLog('ok', '✓ 克隆完成，已添加到仓库列表')
      cloneVisible.value = false
      cloneUrl.value = ''
      cloneDirName.value = ''
      await loadRepos()
      // 自动选中新克隆的仓库（对比前后列表差集）
      const added = repos.value.find(r => !before.has(r.path))
      if (added) currentPath.value = added.path
      ElMessage.success('克隆完成，已添加到仓库列表')
    } else if (cancelledOps.delete(op.opId)) {
      appendLog('warn', '⊘ 克隆已取消，半成品目录已清理')
      ElMessage.info('克隆已取消')
    } else {
      appendLog('err', `✗ 克隆失败（退出码 ${result.exitCode}），目录名冲突时可换名重试`)
    }
  } catch (e) {
    // silent 请求不弹全局错误，这里自行提示
    appendLog('err', `✗ 克隆异常: ${e instanceof Error ? e.message : String(e)}`)
    ElMessage.error(`克隆异常: ${e instanceof Error ? e.message : '网络或后端不可用'}`)
  } finally {
    cloneLoading.value = false
    if (runningOp.value?.opId === op.opId) runningOp.value = null
  }
}

/** 发现仓库弹窗状态 */
const discoverVisible = ref(false)
const discoverPath = ref('')
const discovered = ref<GitDiscoveredRepo[]>([])
const discoverChecked = ref(new Set<string>())

function toggleDiscovered(path: string, checked: boolean) {
  if (checked) discoverChecked.value.add(path)
  else discoverChecked.value.delete(path)
}

async function confirmDiscover() {
  const paths = [...discoverChecked.value]
  if (paths.length === 0) {
    ElMessage.warning('请至少勾选一个仓库')
    return
  }
  let added = 0
  for (const p of paths) {
    try {
      await addGitRepo({ path: p })
      added++
    } catch {
      // 单个失败继续添加其余（拦截器已提示具体原因）
    }
  }
  discoverVisible.value = false
  if (added > 0) {
    await loadRepos()
    if (!currentPath.value) currentPath.value = paths[0]
    ElMessage.success(`已添加 ${added} 个仓库`)
  }
}

async function removeRepoConfirm() {
  const repo = current.value
  if (!repo) return
  try {
    await ElMessageBox.confirm(
      `确认从列表移除「${repo.name}」？仅移除记录，不影响磁盘文件。`,
      '移除仓库',
      { type: 'warning', confirmButtonText: '移除', cancelButtonText: '取消' },
    )
  } catch {
    return
  }
  try {
    await removeGitRepo({ path: repo.path })
    currentPath.value = ''
    await loadRepos()
    ElMessage.success('已移除')
  } catch {
    // 拦截器已提示
  }
}

function openRepoFolder() {
  if (currentPath.value) void openOutputFolder(currentPath.value)
}

// ============================ 操作日志 ============================

type LogKind = 'cmd' | 'out' | 'err' | 'ok' | 'warn'
interface LogLine {
  kind: LogKind
  text: string
}

const logLines = ref<LogLine[]>([])
const logTerminalRef = ref<HTMLDivElement>()
const activeTab = ref<'log' | 'changes' | 'history' | 'knowledge' | 'terminal'>('changes')

// ============================ 运行中操作（取消用） ============================

/** 运行中操作（opId 与后端注册表对应，用于取消） */
interface RunningOp {
  opId: string
  /** 展示用命令文本（git pull / git clone 等） */
  label: string
}

const runningOp = ref<RunningOp | null>(null)
let opSeq = 0
/** 已发送取消信号的操作（请求返回后据此区分“已取消”与“失败”） */
const cancelledOps = new Set<string>()

/** 按钮.loading：当前运行中操作的 label，空串空闲（由 runningOp 派生） */
const actionLoading = computed(() => runningOp.value?.label ?? '')

function newOpId(): string {
  return `op-${Date.now()}-${++opSeq}`
}

/**
 * 取消运行中操作：后端杀对应 git 进程树，原请求随 WaitForExit 正常返回。
 * 克隆被取消时半成品目录由后端清理；合并被中断可能遗留中间状态，横幅会提示。
 */
async function cancelCurrent() {
  const op = runningOp.value
  if (!op) return
  appendLog('warn', `⊘ 正在取消「${op.label}」…`)
  try {
    const res = await gitCancel({ opId: op.opId })
    if (res.cancelled) {
      cancelledOps.add(op.opId)
      appendLog('warn', '已终止进程，等待命令退出…')
    } else {
      appendLog('err', `✗ 取消失败: ${res.message || '未找到运行中的操作'}`)
      ElMessage.warning(res.message || '未找到运行中的操作（可能刚好已结束）')
    }
  } catch {
    appendLog('err', '✗ 取消请求异常（后端不可用）')
    ElMessage.error('取消请求失败')
  }
}

function appendLog(kind: LogKind, text: string) {
  logLines.value.push({ kind, text })
}

/** 命令输出按行写入日志（git 的 stdout/stderr 后端已合并） */
function appendResult(result: GitCommandResult) {
  const text = (result.output || '').replace(/\r\n/g, '\n').trimEnd()
  if (text) {
    for (const line of text.split('\n')) appendLog('out', line)
  } else {
    appendLog('out', '（无输出）')
  }
}

function scrollToLogBottom() {
  void nextTick(() => {
    const el = logTerminalRef.value
    if (el) el.scrollTop = el.scrollHeight
  })
}

// 日志追加 / 切页签时自动滚到底部（display:none 期间滚动位置会丢失）；
// 历史与工作区页签惰性加载：首次进入或数据已过期（切仓库/操作后）时重载
watch(() => logLines.value.length, scrollToLogBottom)
watch(activeTab, (tab) => {
  if (tab === 'log') scrollToLogBottom()
  if (tab === 'history' && (historyDirty.value || historyEntries.value.length === 0)) reloadHistory()
  if (tab === 'changes') {
    if (changesDirty.value || changesStaged.value.length + changesUnstaged.value.length === 0) reloadChanges()
    startChangesPolling()
  } else {
    stopChangesPolling()
  }
})

function clearLog() {
  logLines.value = []
}

async function copyLog() {
  const text = logLines.value.map(l => l.text).join('\n')
  if (!text) {
    ElMessage.info('日志为空')
    return
  }
  await copyText(text, '日志已复制')
}

// ============================ 终端页签 ============================

const TERMINAL_HISTORY_KEY = 'git-terminal-history'
function loadTerminalHistory(): string[] {
  try {
    const list = JSON.parse(localStorage.getItem(TERMINAL_HISTORY_KEY) ?? '[]')
    if (!Array.isArray(list)) return []
    // 旧版历史存的是去掉 git 前缀的子命令，补前缀保证可直接回车执行
    return list
      .filter((c): c is string => typeof c === 'string' && c.trim().length > 0)
      .map(c => (c.startsWith('git ') ? c : `git ${c}`))
  } catch { return [] }
}

/** 命令输入框内容（完整命令，与 cmd 一致） */
const terminalInput = ref('')
/** 终端输出行 */
const terminalLines = ref<LogLine[]>([])
/** 终端运行中 */
const terminalRunning = ref(false)
/** 命令历史（最近50条） */
const terminalHistory = ref<string[]>(loadTerminalHistory())
/** 历史浏览指针（-1 表示未浏览） */
let terminalHistoryIdx = -1
/** 快捷提示列表是否展开 */
const terminalSuggestVisible = ref(false)
/** 当前匹配的知识库快捷提示 */
const terminalSuggestions = computed(() => {
  const kw = terminalInput.value.trim().toLowerCase()
  if (!kw) return [] // 空输入不带出提示，与 cmd 一致
  return gitCommands.filter(c =>
    c.command.toLowerCase().includes(kw) ||
    c.desc.toLowerCase().includes(kw)
  ).slice(0, 10)
})
const hasVisibleSuggestions = computed(() =>
  terminalSuggestVisible.value && terminalSuggestions.value.length > 0)
const terminalOutputRef = ref<HTMLDivElement>()
const terminalInputRef = ref<HTMLInputElement>()

function terminalScrollBottom() {
  void nextTick(() => {
    const el = terminalOutputRef.value
    if (el) el.scrollTop = el.scrollHeight
  })
}

watch(() => terminalLines.value.length, terminalScrollBottom)

function saveTerminalHistory(cmd: string) {
  const list = terminalHistory.value.filter(c => c !== cmd)
  list.unshift(cmd)
  terminalHistory.value = list.slice(0, 50)
  localStorage.setItem(TERMINAL_HISTORY_KEY, JSON.stringify(terminalHistory.value))
}

function applySuggestion(cmd: string) {
  // 直接填入完整命令（含 git 前缀），回车执行；点击建议项 / Tab 补全时调用
  terminalInput.value = cmd
  terminalSuggestVisible.value = false
}

async function runTerminalCommand() {
  const cmd = terminalInput.value.trim()
  if (!cmd) return
  terminalSuggestVisible.value = false
  // cls / clear：本地清屏，与 cmd 一致，不发后端
  if (cmd === 'cls' || cmd === 'clear') {
    clearTerminal()
    saveTerminalHistory(cmd)
    terminalHistoryIdx = -1
    terminalInput.value = ''
    return
  }
  if (!currentPath.value) { ElMessage.warning('请先在左侧选择仓库'); return }
  // 仅支持 git 命令（后端白名单同样限制）；非 git 命令给 cmd 风格报错
  const first = cmd.split(/\s+/)[0]
  if (first !== 'git') {
    terminalLines.value.push({ kind: 'cmd', text: `$ ${cmd}` })
    terminalLines.value.push({ kind: 'err', text: `'${first}' 不是内部或外部命令，也不是可运行的程序（本终端仅支持 git 命令）。` })
    terminalInput.value = ''
    return
  }
  terminalLines.value.push({ kind: 'cmd', text: `$ ${cmd}` })
  saveTerminalHistory(cmd)
  terminalHistoryIdx = -1
  terminalInput.value = ''
  terminalRunning.value = true
  try {
    const result = await gitExec({ path: currentPath.value, command: cmd })
    const text = (result.output || '').replace(/\r\n/g, '\n').trimEnd()
    if (text) {
      for (const line of text.split('\n'))
        terminalLines.value.push({ kind: result.success ? 'out' : 'err', text: line })
    } else {
      terminalLines.value.push({ kind: 'out', text: result.success ? '（命令成功，无输出）' : `（退出码 ${result.exitCode}）` })
    }
  } catch {
    terminalLines.value.push({ kind: 'err', text: '✗ 请求异常（后端不可用）' })
  } finally {
    terminalRunning.value = false
  }
}

function onTerminalKeydown(e: KeyboardEvent) {
  if (e.key === 'Tab') {
    e.preventDefault()
    if (hasVisibleSuggestions.value) {
      applySuggestion(terminalSuggestions.value[0].command)
    }
    return
  }
  if (e.key === 'Enter' && !e.shiftKey) {
    e.preventDefault()
    void runTerminalCommand()
    return
  }
  if (e.key === 'Escape') {
    terminalSuggestVisible.value = false
    return
  }
  if (e.key === 'ArrowUp') {
    e.preventDefault()
    terminalSuggestVisible.value = false
    const hist = terminalHistory.value
    if (hist.length === 0) return
    terminalHistoryIdx = Math.min(terminalHistoryIdx + 1, hist.length - 1)
    terminalInput.value = hist[terminalHistoryIdx]
    return
  }
  if (e.key === 'ArrowDown') {
    e.preventDefault()
    terminalSuggestVisible.value = false
    terminalHistoryIdx = Math.max(terminalHistoryIdx - 1, -1)
    terminalInput.value = terminalHistoryIdx >= 0 ? terminalHistory.value[terminalHistoryIdx] : ''
    return
  }
}

function onTerminalInput() {
  terminalHistoryIdx = -1
  terminalSuggestVisible.value = terminalInput.value.trim().length > 0
}

function clearTerminal() {
  terminalLines.value = []
}

/** 点击输出区域时聚焦输入框 */
function focusTerminalInput() {
  terminalInputRef.value?.focus()
}

async function copyText(text: string, tip = '已复制') {
  try {
    await navigator.clipboard.writeText(text)
    ElMessage.success(tip)
  } catch {
    ElMessage.error('复制失败：剪贴板不可用')
  }
}

// ============================ 核心操作 ============================

/**
 * 操作统一包装：写日志 → 执行（带 opId，可取消）→ 结果写日志 → 刷新状态。
 * label 为真实命令文本（如 git pull），日志与按钮 loading 均以它为准。
 * 请求为 silent 模式，HTTP 异常在这里自行提示。
 */
async function runAction(label: string, action: (opId: string) => Promise<GitCommandResult>): Promise<GitCommandResult | null> {
  if (!currentPath.value) {
    ElMessage.warning('请先在左侧选择仓库')
    return null
  }
  const repoName = current.value?.name || ''
  const op: RunningOp = { opId: newOpId(), label }
  runningOp.value = op
  activeTab.value = 'log'
  appendLog('cmd', `[${repoName}] $ ${label}`)
  try {
    const result = await action(op.opId)
    appendResult(result)
    if (result.success) {
      appendLog('ok', `✓ 完成（退出码 0）`)
    } else if (cancelledOps.delete(op.opId)) {
      appendLog('warn', `⊘ 已取消（退出码 ${result.exitCode}），合并被中断时可用“放弃合并”复位`)
    } else {
      appendLog('err', `✗ 失败（退出码 ${result.exitCode}）`)
    }
    void refreshCurrent()
    historyDirty.value = true
    changesDirty.value = true
    return result
  } catch (e) {
    appendLog('err', `✗ 执行异常: ${e instanceof Error ? e.message : String(e)}`)
    ElMessage.error(`执行异常: ${e instanceof Error ? e.message : '网络或后端不可用'}`)
    return null
  } finally {
    if (runningOp.value?.opId === op.opId) runningOp.value = null
  }
}

function pullCurrent() {
  return runAction('git pull', (opId) => gitPull({ path: currentPath.value, opId }))
}

function pushCurrent() {
  return runAction('git push', (opId) => gitPush({ path: currentPath.value, opId }))
}

// ============================ 合并 ============================

const mergeVisible = ref(false)
const mergeBranches = ref<GitBranch[]>([])
const mergeSource = ref('')

/** 合并中间状态（横幅展示；上次合并中断遗留时也会显示） */
const mergeState = ref<GitMergeState>({ inProgress: false, sourceBranch: '', conflicts: 0 })

async function loadMergeState() {
  if (!currentPath.value) return
  try {
    mergeState.value = await getGitMergeState({ path: currentPath.value })
  } catch {
    // 静默：状态查询失败不打扰
  }
}

// 切换仓库时同步合并状态（中断遗留的 MERGE_HEAD 会在此暴露）；历史与工作区切仓库后需重载
watch(currentPath, () => {
  void loadMergeState()
  historyDirty.value = true
  changesDirty.value = true
  // 历史筛选重置（loadHistoryPage 会默认选中该仓库的当前分支）
  historyBranch.value = ''
  // 正在历史页签时立即重载（含分支下拉数据源）；否则等切回页签时惰性重载
  if (activeTab.value === 'history') reloadHistory()
  // 正在工作区页签时立即重载；切仓库清空已勾选文件
  checkedUnstaged.value = []
  checkedStaged.value = []
  if (activeTab.value === 'changes') reloadChanges()
})

async function openMergeDialog() {
  if (!currentPath.value) {
    ElMessage.warning('请先在左侧选择仓库')
    return
  }
  try {
    mergeBranches.value = (await getGitBranches({ path: currentPath.value })).filter(b => !b.isCurrent)
    mergeSource.value = ''
    mergeVisible.value = true
  } catch {
    // 拦截器已提示
  }
}

async function confirmMerge() {
  if (!mergeSource.value) {
    ElMessage.warning('请选择来源分支')
    return
  }
  mergeVisible.value = false
  await runAction(`git merge --no-edit ${mergeSource.value}`, (opId) =>
    gitMerge({ path: currentPath.value, sourceBranch: mergeSource.value, opId }))
}

/** 放弃进行中的合并（git merge --abort，工作区回到合并前；runAction 内会刷新状态与横幅） */
function abortMerge() {
  return runAction('git merge --abort', (opId) =>
    gitExec({ path: currentPath.value, command: 'git merge --abort', opId }))
}

// ============================ 分支管理 ============================

const branchDrawerVisible = ref(false)
const branches = ref<GitBranch[]>([])
const branchLoading = ref(false)
const newBranchName = ref('')
/** 新分支起点（留空 = 当前 HEAD） */
const newBranchBase = ref('')

async function openBranchDrawer() {
  if (!currentPath.value) {
    ElMessage.warning('请先在左侧选择仓库')
    return
  }
  branchDrawerVisible.value = true
  await loadBranches()
}

async function loadBranches() {
  if (!currentPath.value) return
  branchLoading.value = true
  try {
    branches.value = await getGitBranches({ path: currentPath.value })
  } catch {
    // 拦截器已提示
  } finally {
    branchLoading.value = false
  }
}

/** 切换分支；远程分支由 git switch 自动创建本地跟踪分支 */
async function switchBranch(branch: GitBranch) {
  if (branch.isCurrent) return
  // 切换前检查未暂存/未提交改动
  const dirty = changesUnstaged.value.length + changesStaged.value.length
  if (dirty > 0) {
    try {
      await ElMessageBox.confirm(
        `当前工作区有 ${dirty} 个未提交文件改动，切换分支后这些改动会保留在工作区（已跟踪文件可能产生冲突）。建议先储藏或提交改动再切换。`,
        '工作区有未提交改动',
        { type: 'warning', confirmButtonText: '仍要切换', cancelButtonText: '取消' },
      )
    } catch {
      return
    }
  }
  const result = await runAction(`git switch ${branch.name}`, (opId) =>
    gitCheckout({ path: currentPath.value, branch: branch.name, newBranch: '', opId }))
  // 切换成功后刷新分支列表的"当前"标记（runAction 内已刷新仓库状态）
  if (result?.success) await loadBranches()
}

async function createBranch() {
  const name = newBranchName.value.trim()
  if (!name) {
    ElMessage.warning('请输入新分支名')
    return
  }
  const base = newBranchBase.value
  const result = await runAction(`git switch -c ${name}${base ? ' ' + base : ''}`, (opId) =>
    gitCheckout({ path: currentPath.value, branch: base, newBranch: name, opId }))
  if (result?.success) {
    newBranchName.value = ''
    newBranchBase.value = ''
    await loadBranches()
  }
}

// ============================ 工作区改动（暂存/提交） ============================

const changesLoading = ref(false)
/** 改动数据已过期（切仓库/本地操作后），下次进入页签时重载 */
const changesDirty = ref(false)
const changesStaged = ref<GitChangeFile[]>([])
const changesUnstaged = ref<GitChangeFile[]>([])
/** 已勾选的未暂存文件路径（批量暂存用） */
const checkedUnstaged = ref<string[]>([])
/** 已勾选的已暂存文件路径（批量取消暂存用） */
const checkedStaged = ref<string[]>([])
/** Shift 多选锚点路径（未暂存区） */
let lastClickedUnstaged: string | null = null
/** Shift 多选锚点路径（已暂存区） */
let lastClickedStaged: string | null = null

/**
 * 文件行点击处理：支持 Shift 连续范围选、Ctrl 单独加减选，普通点击预览 diff。
 * @param f       被点击的文件
 * @param staged  是否在已暂存区
 * @param e       鼠标事件
 */
function onFileItemClick(f: GitChangeFile, staged: boolean, e: MouseEvent) {
  const list = staged ? filteredStaged.value : filteredUnstaged.value
  const checked = staged ? checkedStaged : checkedUnstaged
  const lastClicked = staged ? lastClickedStaged : lastClickedUnstaged

  if (e.shiftKey && lastClicked) {
    // Shift+点击：计算从锚点到当前的范围，全部选中
    const anchorIdx = list.findIndex(x => x.path === lastClicked)
    const curIdx    = list.findIndex(x => x.path === f.path)
    if (anchorIdx >= 0 && curIdx >= 0) {
      const lo = Math.min(anchorIdx, curIdx)
      const hi = Math.max(anchorIdx, curIdx)
      const rangePaths = list.slice(lo, hi + 1).map(x => x.path)
      // 合并到已勾选（不去除已有勾选）
      const merged = Array.from(new Set([...checked.value, ...rangePaths]))
      checked.value = merged
    }
    // Shift 选范围不更新锚点，保持连续 Shift 扩展
    return
  }

  if (e.ctrlKey || e.metaKey) {
    // Ctrl+点击：加选或取消单项
    if (checked.value.includes(f.path)) {
      checked.value = checked.value.filter(p => p !== f.path)
    } else {
      checked.value = [...checked.value, f.path]
    }
    if (staged) lastClickedStaged = f.path
    else        lastClickedUnstaged = f.path
    return
  }

  // 普通点击：预览 diff，更新锚点
  if (staged) lastClickedStaged = f.path
  else        lastClickedUnstaged = f.path
  previewDiff(f, staged)
}
/** 当前选中预览 diff 的文件 */
const changesSelected = ref<{ file: GitChangeFile; staged: boolean } | null>(null)
const changesDiff = ref<GitFileDiff | null>(null)
const changesDiffLoading = ref(false)
const commitMessage = ref('')
const commitPush = ref(false)
const committing = ref(false)

/** 提交消息历史（最近 10 条，存 localStorage） */
const COMMIT_MSG_HISTORY_KEY = 'git-commit-msg-history'
function loadCommitMsgHistory(): string[] {
  try { return JSON.parse(localStorage.getItem(COMMIT_MSG_HISTORY_KEY) ?? '[]') } catch { return [] }
}
const commitMsgHistory = ref<string[]>(loadCommitMsgHistory())
function saveCommitMsg(msg: string) {
  const list = commitMsgHistory.value.filter(m => m !== msg)
  list.unshift(msg)
  commitMsgHistory.value = list.slice(0, 10)
  localStorage.setItem(COMMIT_MSG_HISTORY_KEY, JSON.stringify(commitMsgHistory.value))
}

/** Stash 储藏状态 */
const stashLoading = ref(false)
const stashList = ref<GitStashEntry[]>([])
const stashDrawerVisible = ref(false)
const stashMsgInput = ref('')
const stashMsgDialogVisible = ref(false)

async function loadStashList() {
  if (!currentPath.value) return
  stashLoading.value = true
  try { stashList.value = await getGitStashList({ path: currentPath.value }) }
  catch { stashList.value = [] }
  finally { stashLoading.value = false }
}

async function doStash() {
  if (!currentPath.value) return
  stashMsgDialogVisible.value = false
  const result = await gitStash({ path: currentPath.value, message: stashMsgInput.value.trim() })
  stashMsgInput.value = ''
  if (result.success) {
    appendLog('ok', `✓ 已储藏改动`)
    ElMessage.success('已储藏改动')
    await reloadChanges()
    void refreshCurrent()
  } else {
    appendLog('err', `✗ 储藏失败: ${result.output}`)
    ElMessage.error('储藏失败')
  }
}

async function doStashPop(index: number) {
  if (!currentPath.value) return
  const result = await gitStashPop({ path: currentPath.value, index })
  if (result.success) {
    appendLog('ok', `✓ 已应用 stash@{${index}}`)
    ElMessage.success('已应用储藏')
    stashDrawerVisible.value = false
    await reloadChanges()
    void refreshCurrent()
    await loadStashList()
  } else {
    appendLog('err', `✗ 应用储藏失败: ${result.output}`)
    ElMessage.error('应用储藏失败')
  }
}

async function doStashDrop(index: number) {
  try {
    await ElMessageBox.confirm(`确认删除 stash@{${index}}？此操作不可撤销。`, '删除储藏',
      { type: 'warning', confirmButtonText: '删除', cancelButtonText: '取消' })
  } catch { return }
  if (!currentPath.value) return
  const result = await gitStashDrop({ path: currentPath.value, index })
  if (result.success) {
    ElMessage.success('已删除储藏')
    await loadStashList()
  } else {
    ElMessage.error('删除储藏失败')
  }
}

// ---------- 拖拽分割 ----------
/** 左侧文件列表宽度（px） */
const changesListWidth = ref(Number(localStorage.getItem('git-changes-list-w')) || 380)
/** 未暂存区占整个左侧面板的高度百分比（0-100） */
const unstageHeightPct = ref(Number(localStorage.getItem('git-unstage-pct')) || 50)
/** 提交历史左侧（提交列表）宽度（px） */
const historyListWidth = ref(Number(localStorage.getItem('git-history-list-w')) || 560)

watch(changesListWidth, v => localStorage.setItem('git-changes-list-w', String(v)))
watch(unstageHeightPct, v => localStorage.setItem('git-unstage-pct', String(v)))
watch(historyListWidth, v => localStorage.setItem('git-history-list-w', String(v)))

function startHResize(e: MouseEvent) {
  e.preventDefault()
  const startX = e.clientX
  const startW = changesListWidth.value
  const onMove = (ev: MouseEvent) => {
    const w = Math.max(180, Math.min(700, startW + ev.clientX - startX))
    changesListWidth.value = w
  }
  const onUp = () => {
    document.removeEventListener('mousemove', onMove)
    document.removeEventListener('mouseup', onUp)
    document.body.style.cursor = ''
    document.body.style.userSelect = ''
  }
  document.body.style.cursor = 'col-resize'
  document.body.style.userSelect = 'none'
  document.addEventListener('mousemove', onMove)
  document.addEventListener('mouseup', onUp)
}

function startVResize(e: MouseEvent) {
  e.preventDefault()
  const startY = e.clientY
  const startPct = unstageHeightPct.value
  // 获取左侧面板的实际高度
  const listEl = (e.currentTarget as HTMLElement).closest('.changes-list') as HTMLElement | null
  const listH = listEl ? listEl.getBoundingClientRect().height : 0
  const onMove = (ev: MouseEvent) => {
    if (!listH) return
    const delta = ev.clientY - startY
    const deltaPct = (delta / listH) * 100
    unstageHeightPct.value = Math.max(15, Math.min(85, startPct + deltaPct))
  }
  const onUp = () => {
    document.removeEventListener('mousemove', onMove)
    document.removeEventListener('mouseup', onUp)
    document.body.style.cursor = ''
    document.body.style.userSelect = ''
  }
  document.body.style.cursor = 'row-resize'
  document.body.style.userSelect = 'none'
  document.addEventListener('mousemove', onMove)
  document.addEventListener('mouseup', onUp)
}

// ---------- 提交历史左右拖拽 ----------
function startHistoryHResize(e: MouseEvent) {
  e.preventDefault()
  const startX = e.clientX
  const startW = historyListWidth.value
  const onMove = (ev: MouseEvent) => {
    historyListWidth.value = Math.max(200, Math.min(900, startW + ev.clientX - startX))
  }
  const onUp = () => {
    document.removeEventListener('mousemove', onMove)
    document.removeEventListener('mouseup', onUp)
    document.body.style.cursor = ''
    document.body.style.userSelect = ''
  }
  document.body.style.cursor = 'col-resize'
  document.body.style.userSelect = 'none'
  document.addEventListener('mousemove', onMove)
  document.addEventListener('mouseup', onUp)
}

/** 提交详情面板内文件列表宽度（px） */
const detailFilesWidth = ref(Number(localStorage.getItem('git-detail-files-w')) || 220)
watch(detailFilesWidth, v => localStorage.setItem('git-detail-files-w', String(v)))

function startDetailHResize(e: MouseEvent) {
  e.preventDefault()
  const startX = e.clientX
  const startW = detailFilesWidth.value
  const onMove = (ev: MouseEvent) => {
    detailFilesWidth.value = Math.max(120, Math.min(600, startW + ev.clientX - startX))
  }
  const onUp = () => {
    document.removeEventListener('mousemove', onMove)
    document.removeEventListener('mouseup', onUp)
    document.body.style.cursor = ''
    document.body.style.userSelect = ''
  }
  document.body.style.cursor = 'col-resize'
  document.body.style.userSelect = 'none'
  document.addEventListener('mousemove', onMove)
  document.addEventListener('mouseup', onUp)
}

// ---------- 筛选 & 排序 ----------
/** 显示类型：'modified' | 'untracked' | 'conflict' | 'pending' | 'all' */
const changesFilter = ref<'modified' | 'untracked' | 'conflict' | 'pending' | 'all'>('modified')
/** 排序方式：'status' | 'path-asc' | 'path-desc' | 'name-asc' | 'name-desc' | 'checked' */
const changesSort = ref<'status' | 'path-asc' | 'path-desc' | 'name-asc' | 'name-desc' | 'checked'>('status')

/** 匹配显示类型筛选 */
function matchFilter(f: GitChangeFile, staged: boolean): boolean {
  switch (changesFilter.value) {
    case 'modified': return !f.isConflict  // 已修改 + 已删除 + 未跟踪（新文件），排除冲突
    case 'untracked': return f.status === '?'
    case 'conflict': return !!f.isConflict
    case 'pending': return staged   // 已暂存区有内容：在已暂存中显示全部
    default: return true
  }
}

/** 排序函数 */
function sortFiles(list: GitChangeFile[], checkedPaths: string[]): GitChangeFile[] {
  const s = changesSort.value
  return [...list].sort((a, b) => {
    if (s === 'path-asc') return a.path.localeCompare(b.path)
    if (s === 'path-desc') return b.path.localeCompare(a.path)
    if (s === 'name-asc') return basename(a.path).localeCompare(basename(b.path))
    if (s === 'name-desc') return basename(b.path).localeCompare(basename(a.path))
    if (s === 'checked') {
      const ca = checkedPaths.includes(a.path) ? 0 : 1
      const cb = checkedPaths.includes(b.path) ? 0 : 1
      if (ca !== cb) return ca - cb
    }
    // 默认按文件状态字母排序
    return a.status.localeCompare(b.status) || a.path.localeCompare(b.path)
  })
}

function basename(p: string): string {
  return p.split(/[\\/]/).pop() ?? p
}

const filteredUnstaged = computed(() =>
  sortFiles(changesUnstaged.value.filter(f => matchFilter(f, false)), checkedUnstaged.value)
)

const filteredStaged = computed(() =>
  sortFiles(changesStaged.value.filter(f => matchFilter(f, true)), checkedStaged.value)
)

async function reloadChanges(silent = false) {
  if (!currentPath.value) {
    ElMessage.warning('请先在左侧选择仓库')
    return
  }
  if (!silent) changesLoading.value = true
  try {
    const dto = await getGitChanges({ path: currentPath.value }, { silent })
    changesStaged.value = dto.staged
    changesUnstaged.value = dto.unstaged
    // 同步合并横幅（与 MergeState 端点同源数据）
    mergeState.value = dto.mergeState
    changesDirty.value = false
    // 选中项已不在列表中时清空预览
    const stillThere =
      changesSelected.value &&
      (dto.staged.some(f => f.path === changesSelected.value!.file.path) ||
       dto.unstaged.some(f => f.path === changesSelected.value!.file.path))
    if (!stillThere) {
      changesSelected.value = null
      changesDiff.value = null
    }
    // 清理已不存在的勾选项
    const unstagedPaths = new Set(dto.unstaged.map(f => f.path))
    const stagedPaths = new Set(dto.staged.map(f => f.path))
    checkedUnstaged.value = checkedUnstaged.value.filter(p => unstagedPaths.has(p))
    checkedStaged.value = checkedStaged.value.filter(p => stagedPaths.has(p))
  } catch {
    // 拦截器已提示
  } finally {
    changesLoading.value = false
  }
}

async function previewDiff(file: GitChangeFile, staged: boolean) {
  changesSelected.value = { file, staged }
  changesDiff.value = null
  changesDiffLoading.value = true
  try {
    changesDiff.value = await getGitFileDiff({
      path: currentPath.value,
      filePath: file.path,
      staged: staged && !file.isUntracked,
    })
  } catch {
    // 拦截器已提示；预览失败不影响列表操作
  } finally {
    changesDiffLoading.value = false
  }
}

/** 暂存/取消暂存单文件或全部（stage=true 暂存） */
async function stageFile(file: GitChangeFile | null, stage: boolean) {
  if (!currentPath.value) return
  try {
    const result = await gitStage({
      path: currentPath.value,
      filePath: file?.path ?? null,
      stage,
    })
    if (result.success) {
      appendLog('ok', file
        ? `${stage ? '已暂存' : '已取消暂存'}: ${file.path}`
        : (stage ? '已全部暂存' : '已全部取消暂存'))
      await reloadChanges()
      void refreshCurrent()
    } else {
      appendLog('err', `${stage ? '暂存' : '取消暂存'}失败: ${result.output}`)
      ElMessage.error(result.output || '操作失败')
    }
  } catch {
    // 拦截器已提示
  }
}

/** 暂存已勾选的未暂存文件 */
async function stageChecked() {
  if (!currentPath.value || checkedUnstaged.value.length === 0) return
  const files = checkedUnstaged.value.slice()
  for (const path of files) {
    try {
      await gitStage({ path: currentPath.value, filePath: path, stage: true })
    } catch { /* 拦截器已提示 */ }
  }
  appendLog('ok', `已暂存 ${files.length} 个文件`)
  checkedUnstaged.value = []
  await reloadChanges()
  void refreshCurrent()
}

/** 取消暂存已勾选的已暂存文件 */
async function unstageChecked() {
  if (!currentPath.value || checkedStaged.value.length === 0) return
  const files = checkedStaged.value.slice()
  for (const path of files) {
    try {
      await gitStage({ path: currentPath.value, filePath: path, stage: false })
    } catch { /* 拦截器已提示 */ }
  }
  appendLog('ok', `已取消暂存 ${files.length} 个文件`)
  checkedStaged.value = []
  await reloadChanges()
  void refreshCurrent()
}

/** 放弃单文件/全部未暂存改动（不可恢复，二次确认） */
async function discardChanges(file: GitChangeFile | null) {
  if (!currentPath.value) return
  const target = file ? `「${file.path}」` : `全部未暂存改动（含未跟踪文件）`
  try {
    await ElMessageBox.confirm(
      `放弃 ${target}？已跟踪文件还原到上次提交，未跟踪文件/目录将被物理删除，不可恢复。`,
      '放弃改动',
      { type: 'warning', confirmButtonText: '放弃', cancelButtonText: '取消' },
    )
  } catch {
    return
  }
  try {
    const result = await gitDiscard({
      path: currentPath.value,
      filePath: file?.path ?? null,
      includeUntracked: true,
    })
    if (result.success) {
      appendLog('warn', `⊘ 已放弃 ${target}`)
      await reloadChanges()
      void refreshCurrent()
    } else {
      appendLog('err', `放弃失败: ${result.output}`)
    }
  } catch {
    // 拦截器已提示
  }
}

/** 提交已暂存改动（可选顺带推送）；silent 长操作走 opId 可取消 */
async function commitStaged() {
  if (!currentPath.value) return
  const message = commitMessage.value.trim()
  if (!message) {
    ElMessage.warning('请输入提交说明')
    return
  }
  if (changesStaged.value.length === 0) {
    ElMessage.warning('没有已暂存的改动（先在左侧暂存文件）')
    return
  }
  const repoName = current.value?.name || ''
  const op: RunningOp = { opId: newOpId(), label: 'git commit' }
  runningOp.value = op
  committing.value = true
  activeTab.value = 'changes'
  appendLog('cmd', `[${repoName}] $ git commit${commitPush.value ? ' && git push' : ''}`)
  try {
    const result = await gitCommitChanges({
      path: currentPath.value,
      message,
      push: commitPush.value,
      opId: op.opId,
    })
    appendResult(result)
    if (result.success) {
      appendLog('ok', `✓ 提交完成${commitPush.value ? '，已推送' : ''}`)
      ElMessage.success(`提交完成${commitPush.value ? '，已推送' : ''}`)
      saveCommitMsg(message)
      commitMessage.value = ''
      await reloadChanges()
      void refreshCurrent()
      historyDirty.value = true
    } else if (cancelledOps.delete(op.opId)) {
      appendLog('warn', '⊘ 提交已取消')
    } else {
      appendLog('err', `✗ 提交失败（退出码 ${result.exitCode}），详见输出`)
    }
  } catch (e) {
    appendLog('err', `✗ 提交异常: ${e instanceof Error ? e.message : String(e)}`)
    ElMessage.error(`提交异常: ${e instanceof Error ? e.message : '网络或后端不可用'}`)
  } finally {
    committing.value = false
    if (runningOp.value?.opId === op.opId) runningOp.value = null
  }
}

/** 状态徽章图标（仿 Sourcetree） */
function fileBadgeIcon(status: string): string {
  if (status === 'A') return '+'
  if (status === 'D') return '−'
  if (status === 'M') return '✎'
  if (status === 'R') return '→'
  if (status === 'C') return '©'
  if (status === 'U') return '!'
  if (status === '?') return '+'
  return status || '·'
}

/** 状态徽章 CSS 类（仿 Sourcetree 配色） */
function fileBadgeClass(status: string): string {
  if (status === 'A') return 'fb-added'
  if (status === 'D') return 'fb-deleted'
  if (status === 'M') return 'fb-modified'
  if (status === 'R') return 'fb-renamed'
  if (status === 'C') return 'fb-copied'
  if (status === 'U') return 'fb-conflict'
  if (status === '?') return 'fb-untracked'
  return 'fb-default'
}

// ============================ 提交历史 ============================

/** 提交行高（SVG 与行样式共用） */
const COMMIT_ROW_H = 28
/** 分支图形 lane 列宽（px） */
const LANE_W = 14
const LANE_COLORS = ['#409eff', '#67c23a', '#e6a23c', '#f56c6c', '#9b59b6', '#00b8d4', '#ff7043', '#7cb342']

function laneColor(lane: number): string {
  return LANE_COLORS[lane % LANE_COLORS.length]
}

/** 行内连线段：竖线（进入/离开/贯穿）或合并曲线，渲染纯函数式 */
type GraphSeg =
  | { kind: 'line'; x: number; y1: number; y2: number; lane: number }
  | { kind: 'curve'; from: number; to: number; lane: number }

/** 提交行渲染模型（图形计算后缓存进对象，避免模板内重算） */
interface CommitRow {
  entry: GitLogEntry
  /** 节点所在列 */
  lane: number
  /** 本行 lane 总数（决定 SVG 宽度） */
  laneCount: number
  segs: GraphSeg[]
}

const historyLoading = ref(false)
const historyLoadingMore = ref(false)
/** 历史已过期（切仓库/本地操作完成），下次进入页签时重载 */
const historyDirty = ref(false)
const historyBranch = ref('')
const historyKeyword = ref('')
const historyBranches = ref<GitBranch[]>([])
const historyEntries = ref<GitLogEntry[]>([])
const historyRows = ref<CommitRow[]>([])
const historyHasMore = ref(false)
const historyScrollRef = ref<HTMLDivElement>()
const HISTORY_PAGE_SIZE = 50

/** lane 状态（列 → 等待延续的父提交哈希）；跨页保留续画，重载时清空 */
let lanes: string[] = []

/** 图形区宽度（所有行统一取最大列数，避免各行列坐标错位） */
const graphWidth = computed(() => {
  const maxLane = historyRows.value.reduce((m, r) => Math.max(m, r.laneCount), 0)
  return Math.max(maxLane, 1) * LANE_W + 12
})

/**
 * 单行连线计算：before/after 为处理本提交前后的 lane 快照。
 * 进入段（y:0→14）由 before 决定，离开段（y:14→28）由 after 决定，
 * 贯穿列两段自然拼成整线；父提交落在其它列时画合并曲线。
 */
function buildGraphSegs(before: string[], after: string[], lane: number, parents: string[]): GraphSeg[] {
  const segs: GraphSeg[] = []
  const mid = COMMIT_ROW_H / 2
  before.forEach((h, j) => {
    if (h) segs.push({ kind: 'line', x: j, y1: 0, y2: mid, lane: j })
  })
  after.forEach((h, j) => {
    if (!h) return
    if (j === lane) {
      segs.push({ kind: 'line', x: j, y1: mid, y2: COMMIT_ROW_H, lane: j })
    } else if (parents.includes(h)) {
      segs.push({ kind: 'curve', from: lane, to: j, lane })
    } else {
      segs.push({ kind: 'line', x: j, y1: mid, y2: COMMIT_ROW_H, lane: j })
    }
  })
  return segs
}

/** 把一页提交增量算进行模型（lane 状态续用，滚动翻页无缝续画） */
function appendHistoryPage(entries: GitLogEntry[]) {
  const rows: CommitRow[] = []
  for (const e of entries) {
    const before = [...lanes]
    // 本提交落位：已有子提交预留的列优先，否则占用空列/开新列
    let lane = lanes.indexOf(e.hash)
    if (lane < 0) {
      lane = lanes.indexOf('')
      if (lane < 0) {
        lanes.push(e.hash)
        lane = lanes.length - 1
      } else {
        lanes[lane] = e.hash
      }
    }
    // 释放本列后按父提交重新分配：first parent 继承本列，其余找空列/新列
    lanes[lane] = ''
    for (const p of e.parents) {
      if (lanes.includes(p)) continue // 已在其它列：本行画合并曲线即可，不动它
      if (lanes[lane] === '') {
        lanes[lane] = p
        continue
      }
      let k = lanes.indexOf('')
      if (k < 0) {
        lanes.push('')
        k = lanes.length - 1
      }
      lanes[k] = p
    }
    rows.push({
      entry: e,
      lane,
      laneCount: Math.max(before.length, lanes.length),
      segs: buildGraphSegs(before, [...lanes], lane, e.parents),
    })
  }
  historyRows.value.push(...rows)
}

async function loadHistoryPage(append: boolean) {
  if (!currentPath.value) return
  if (!append) {
    historyLoading.value = true
    lanes = []
    historyEntries.value = []
    historyRows.value = []
    selectedHash.value = ''
    selectedDetail.value = null
    historyDirty.value = false
    // 分支下拉数据源跟随当前仓库刷新（换仓库后旧列表失效）
    try {
      historyBranches.value = await getGitBranches({ path: currentPath.value })
    } catch {
      // 拦截器已提示
    }
    // 筛选默认选中当前分支（未选或所选分支不在本仓库时；游离 HEAD 回退全部历史）
    const cur = current.value?.branch || ''
    if (!historyBranches.value.some(b => b.name === historyBranch.value)) {
      historyBranch.value = historyBranches.value.some(b => b.name === cur) ? cur : ''
    }
  } else {
    historyLoadingMore.value = true
  }
  try {
    const page = await getGitLog({
      path: currentPath.value,
      branch: historyBranch.value,
      keyword: historyKeyword.value.trim() || undefined,
      skip: historyEntries.value.length,
      take: HISTORY_PAGE_SIZE,
    })
    historyEntries.value.push(...page)
    appendHistoryPage(page)
    historyHasMore.value = page.length >= HISTORY_PAGE_SIZE
  } catch {
    // 拦截器已提示
  } finally {
    historyLoading.value = false
    historyLoadingMore.value = false
  }
}

function reloadHistory() {
  if (!currentPath.value) {
    ElMessage.warning('请先在左侧选择仓库')
    return
  }
  void loadHistoryPage(false)
}

function onHistoryScroll(e: Event) {
  const el = e.target as HTMLDivElement
  if (!historyHasMore.value || historyLoading.value || historyLoadingMore.value) return
  if (el.scrollTop + el.clientHeight >= el.scrollHeight - 60) {
    historyLoadingMore.value = true
    void loadHistoryPage(true)
  }
}

// ---- 单提交详情 ----

const selectedHash = ref('')
const selectedDetail = ref<GitCommitDetail | null>(null)
const detailLoading = ref(false)
/** 展开 diff 的文件路径集合 */
const expandedDiffs = ref(new Set<string>())

async function selectCommit(entry: GitLogEntry) {
  if (selectedHash.value === entry.hash) return
  selectedHash.value = entry.hash
  selectedDetail.value = null
  expandedDiffs.value = new Set()
  detailLoading.value = true
  try {
    selectedDetail.value = await getGitCommitDetail({ path: currentPath.value, hash: entry.hash })
  } catch {
    // 拦截器已提示；选不中就取消选中
    selectedHash.value = ''
  } finally {
    detailLoading.value = false
  }
}

function toggleDiff(path: string) {
  const s = expandedDiffs.value
  if (s.has(path)) {
    // 已展开则折叠
    s.delete(path)
  } else {
    // 单文件对比：先清空其他展开项，再展开当前
    s.clear()
    s.add(path)
  }
  // 触发响应式更新
  expandedDiffs.value = new Set(s)
}

/** diff 行计算结果（含行号） */
export interface DiffLine {
  text: string
  cls: string
  lineOld: number | null
  lineNew: number | null
}

/** diff 行按行缓存（避免模板每次渲染重复 split 大文本） */
const diffLinesCache = new WeakMap<GitDiffFile, DiffLine[]>()
function diffLines(d: GitDiffFile): DiffLine[] {
  let lines = diffLinesCache.get(d)
  if (!lines) {
    lines = parseDiffLines(d.diff)
    diffLinesCache.set(d, lines)
  }
  return lines
}

function parseDiffLines(raw: string): DiffLine[] {
  const result: DiffLine[] = []
  let oldLine = 0
  let newLine = 0
  for (const text of raw.split('\n')) {
    const cls = diffLineClass(text)
    if (cls === 'dl-hunk') {
      // @@ -a,b +c,d @@ 提取起始行号
      const m = text.match(/^@@\s*-(\d+)(?:,\d+)?\s*\+(\d+)(?:,\d+)?/)
      if (m) { oldLine = parseInt(m[1]) - 1; newLine = parseInt(m[2]) - 1 }
      result.push({ text, cls, lineOld: null, lineNew: null })
    } else if (cls === 'dl-add') {
      newLine++
      result.push({ text, cls, lineOld: null, lineNew: newLine })
    } else if (cls === 'dl-del') {
      oldLine++
      result.push({ text, cls, lineOld: oldLine, lineNew: null })
    } else if (cls === 'dl-ctx') {
      oldLine++; newLine++
      result.push({ text, cls, lineOld: oldLine, lineNew: newLine })
    } else {
      result.push({ text, cls, lineOld: null, lineNew: null })
    }
  }
  return result
}

/** refs 标签配色：HEAD 绿 / tag 橙 / 远程灰 / 本地分支蓝 */
function refClass(ref: string): string {
  if (ref.includes('HEAD')) return 'ref-head'
  if (ref.startsWith('tag:')) return 'ref-tag'
  if (ref.includes('/')) return 'ref-remote'
  return 'ref-branch'
}

/** 变更文件状态标签配色 */
/** @deprecated 已由 fileBadgeClass/fileBadgeIcon 取代 */
function fileStatusType(status: string): string { return fileBadgeClass(status) }

/** diff 行配色：+绿 / -红 / @@灰蓝 / 头部元信息淡灰 */
function diffLineClass(line: string): string {
  if (line.startsWith('+')) return 'dl-add'
  if (line.startsWith('-')) return 'dl-del'
  if (line.startsWith('@@')) return 'dl-hunk'
  if (/^(diff |index |--- |\+\+\+ |Binary |new file|deleted file|rename |old mode|new mode|similarity )/.test(line)) return 'dl-meta'
  return 'dl-ctx'
}

// ============================ 命令知识库 ============================

const knowledgeSearch = ref('')
const knowledgeCategory = ref(GIT_CATEGORY_ALL)

const filteredCommands = computed(() => {
  const kw = knowledgeSearch.value.trim().toLowerCase()
  return gitCommands.filter(c =>
    (knowledgeCategory.value === GIT_CATEGORY_ALL || c.category === knowledgeCategory.value) &&
    (!kw || c.command.toLowerCase().includes(kw) || c.desc.toLowerCase().includes(kw)),
  )
})

/**
 * 对当前仓库执行知识库命令：
 * 含占位符（<branch> 等）先弹窗编辑；危险命令二次确认；执行后切到日志页签看输出。
 */
async function execCommand(entry: GitCommandEntry) {
  if (!currentPath.value) {
    ElMessage.warning('请先在左侧选择仓库')
    return
  }
  let command = entry.command
  if (command.includes('<')) {
    try {
      const { value } = await ElMessageBox.prompt(
        '命令包含占位符（如 <branch>），请替换为实际值后执行',
        '编辑命令',
        { inputValue: command, confirmButtonText: '执行', cancelButtonText: '取消' },
      )
      command = value.trim()
      if (!command) return
      if (command.includes('<')) {
        ElMessage.warning('命令仍包含占位符，请把 <...> 替换为实际值')
        return
      }
    } catch {
      return
    }
  }
  if (entry.danger) {
    try {
      await ElMessageBox.confirm(
        `该命令可能改写历史或丢弃改动，确认在「${current.value?.name}」执行：${command}`,
        '危险操作确认',
        { type: 'warning', confirmButtonText: '仍要执行', cancelButtonText: '取消' },
      )
    } catch {
      return
    }
  }
  await runAction(command, (opId) => gitExec({ path: currentPath.value, command, opId }))
}

// ============================ 环境检测与配置管理 ============================

const gitEnv = ref<GitEnv | null>(null)
const envChecking = ref(false)

async function checkEnv() {
  envChecking.value = true
  try {
    gitEnv.value = await getGitEnv()
  } finally {
    envChecking.value = false
  }
}

// 弹窗状态
const configDialogVisible = ref(false)
const configList = ref<GitConfigItem[]>([])
const configLoading = ref(false)
const configSaving = ref(false)
// 添加新行
const newConfigKey = ref('')
const newConfigValue = ref('')
// 内联编辑
const editingKey = ref<string | null>(null)
const editingValue = ref('')

async function openConfigDialog() {
  configDialogVisible.value = true
  await loadConfigList()
}

async function loadConfigList() {
  configLoading.value = true
  try {
    configList.value = await getGitConfigList()
  } finally {
    configLoading.value = false
  }
}

function startEdit(item: GitConfigItem) {
  editingKey.value = item.key
  editingValue.value = item.value
}

async function saveEdit(item: GitConfigItem) {
  if (editingKey.value !== item.key) return
  configSaving.value = true
  try {
    await gitConfigSet({ key: item.key, value: editingValue.value })
    await loadConfigList()
    editingKey.value = null
    // 身份改后同步环境检测状态
    if (item.key === 'user.name' || item.key === 'user.email') void checkEnv()
    ElMessage.success('已保存')
  } finally {
    configSaving.value = false
  }
}

async function deleteConfig(key: string) {
  await ElMessageBox.confirm(`确定删除配置项 ${key}？`, '删除配置', {
    confirmButtonText: '删除', cancelButtonText: '取消', type: 'warning',
  })
  configSaving.value = true
  try {
    await gitConfigSet({ key, value: null })
    await loadConfigList()
    if (key === 'user.name' || key === 'user.email') void checkEnv()
    ElMessage.success('已删除')
  } finally {
    configSaving.value = false
  }
}

async function addConfig() {
  const k = newConfigKey.value.trim()
  const v = newConfigValue.value.trim()
  if (!k || !v) { ElMessage.warning('键和值不能为空'); return }
  configSaving.value = true
  try {
    await gitConfigSet({ key: k, value: v })
    newConfigKey.value = ''
    newConfigValue.value = ''
    await loadConfigList()
    if (k === 'user.name' || k === 'user.email') void checkEnv()
    ElMessage.success('已添加')
  } finally {
    configSaving.value = false
  }
}
</script>

<template>
  <div class="git-workbench-page">
    <div class="workbench-layout">

      <!-- 左栏：仓库列表 -->
      <aside class="repo-sidebar">
        <div class="sidebar-header">
          <span class="sidebar-title">代码仓库</span>
          <el-icon class="header-icon" title="克隆仓库" @click="openCloneDialog"><Download /></el-icon>
          <el-icon class="header-icon" title="添加仓库" @click="addRepoFlow"><Plus /></el-icon>
          <el-icon class="header-icon" title="刷新列表" @click="loadRepos"><Refresh /></el-icon>
        </div>
        <!-- 搜索框 -->
        <div class="repo-search-bar">
          <el-input
            v-model="repoSearch"
            size="small"
            clearable
            placeholder="搜索仓库名 / 路径"
          >
            <template #prefix><el-icon><Search /></el-icon></template>
          </el-input>
        </div>
        <div
          v-loading="reposLoading"
          class="repo-list"
          @contextmenu="showContextMenu($event, [
            { icon: '↓', label: '克隆仓库…', action: openCloneDialog },
            { icon: '＋', label: '添加本地仓库…', action: addRepoFlow },
            { icon: '📁', label: '新建分组', divider: true, action: createGroup },
          ])"
        >
          <template v-for="group in groupedRepos" :key="group.name">
            <!-- 分组标题（右键管理分组 / 拖拽仓库放置目标 / 拖动排序） -->
            <div
              class="repo-group-header"
              :class="{ 'drag-over': dragOverGroup === group.name }"
              @click="toggleGroupExpand(group.name)"
              @contextmenu="showContextMenu($event, [
                { icon: '＋', label: '新建分组', action: createGroup },
                { icon: '✎', label: '重命名分组…', divider: true, action: () => renameGroup(group.name) },
                { icon: '🗑️', label: '解散分组', danger: true, action: () => dissolveGroup(group.name) },
              ])"
              :draggable="!!group.name"
              @dragstart="onGroupHeaderDragStart($event, group.name)"
              @dragend="onGroupHeaderDragEnd"
              @dragover="onHeaderDragOver($event, group.name)"
              @dragleave="onGroupDragLeave($event, group.name)"
              @drop="onHeaderDrop($event, group.name)"
            >
              <span class="repo-group-arrow">{{ group.expanded ? '▾' : '▸' }}</span>
              <span class="repo-group-label">{{ group.label }}</span>
              <span class="repo-group-count">{{ group.repos.length }}</span>
            </div>
            <template v-if="group.expanded">
              <div
                v-for="repo in group.repos"
                :key="repo.path"
                class="repo-item grouped"
                :class="{ selected: repo.path === currentPath, invalid: !repo.isRepo, 'drop-above': dragOverRepoPath === repo.path && repoInsertAbove, 'drop-below': dragOverRepoPath === repo.path && !repoInsertAbove }"
                :title="repo.message || repo.path"
                draggable="true"
                @dragstart="onRepoDragStart($event, repo.path)"
                @dragend="onRepoDragEnd"
                @dragover="onRepoItemDragOver($event, repo.path)"
                @dragleave="onRepoItemDragLeave"
                @drop="onRepoItemDrop($event, repo.path)"
                @click="selectRepo(repo.path)"
                @contextmenu="showContextMenu($event, repoMenuItems(repo))"
              >
                <div class="repo-item-top">
                  <span class="repo-name">{{ repo.name }}</span>
                  <el-icon
                    class="repo-pin-icon"
                    :class="{ pinned: pinnedPaths.includes(repo.path) }"
                    :title="pinnedPaths.includes(repo.path) ? '取消置顶' : '置顶'"
                    @click.stop="togglePin(repo.path)"
                  ><Star /></el-icon>
                </div>
                <div class="repo-item-meta">
                  <el-tag v-if="repo.isRepo && repo.branch" size="small" type="info">{{ repo.branch }}</el-tag>
                  <el-tag v-else size="small" type="danger">失效</el-tag>
                  <span v-if="repo.isRepo && repo.ahead > 0" class="meta-ahead">↑{{ repo.ahead }}</span>
                  <span v-if="repo.isRepo && repo.behind > 0" class="meta-behind">↓{{ repo.behind }}</span>
                  <span v-if="repo.isRepo" class="meta-changes" :class="{ dirty: repo.changes > 0 }">
                    {{ repo.changes > 0 ? `${repo.changes} 改动` : '干净' }}
                  </span>
                  <span v-else class="meta-error">{{ repo.message }}</span>
                </div>
              </div>
            </template>
          </template>

          <!-- 拖拽仓库时：「移出分组」放置区（拖入即变一级仓库） -->
          <div
            v-if="isDraggingRepo && draggingRepoFromGroup"
            class="repo-ungroup-dropzone"
            :class="{ 'drag-over': dragOverGroup === '__ungroup__' }"
            @dragover="onUngroupZoneDragOver($event)"
            @dragleave="onUngroupZoneDragLeave"
            @drop="onUngroupZoneDrop($event)"
          >⟲ 松开移出分组（一级显示）</div>

          <!-- 一级仓库（无分组）：与分组同级平铺 -->
          <div
            v-for="repo in ungroupedRepos"
            :key="repo.path"
            class="repo-item"
            :class="{ selected: repo.path === currentPath, invalid: !repo.isRepo, 'drop-above': dragOverRepoPath === repo.path && repoInsertAbove, 'drop-below': dragOverRepoPath === repo.path && !repoInsertAbove }"
            :title="repo.message || repo.path"
            draggable="true"
            @dragstart="onRepoDragStart($event, repo.path)"
            @dragend="onRepoDragEnd"
            @dragover="onRepoItemDragOver($event, repo.path)"
            @dragleave="onRepoItemDragLeave"
            @drop="onRepoItemDrop($event, repo.path)"
            @click="selectRepo(repo.path)"
            @contextmenu="showContextMenu($event, repoMenuItems(repo))"
          >
            <div class="repo-item-top">
              <span class="repo-name">{{ repo.name }}</span>
              <el-icon
                class="repo-pin-icon"
                :class="{ pinned: pinnedPaths.includes(repo.path) }"
                :title="pinnedPaths.includes(repo.path) ? '取消置顶' : '置顶'"
                @click.stop="togglePin(repo.path)"
              ><Star /></el-icon>
            </div>
            <div class="repo-item-meta">
              <el-tag v-if="repo.isRepo && repo.branch" size="small" type="info">{{ repo.branch }}</el-tag>
              <el-tag v-else size="small" type="danger">失效</el-tag>
              <span v-if="repo.isRepo && repo.ahead > 0" class="meta-ahead">↑{{ repo.ahead }}</span>
              <span v-if="repo.isRepo && repo.behind > 0" class="meta-behind">↓{{ repo.behind }}</span>
              <span v-if="repo.isRepo" class="meta-changes" :class="{ dirty: repo.changes > 0 }">
                {{ repo.changes > 0 ? `${repo.changes} 改动` : '干净' }}
              </span>
              <span v-else class="meta-error">{{ repo.message }}</span>
            </div>
          </div>

          <div v-if="!reposLoading && filteredRepos.length === 0" class="repo-empty">
            <template v-if="repoSearch">
              <div style="padding:12px;font-size:12px;color:#909399">无匹配结果</div>
            </template>
            <template v-else>
              <el-empty description="还没有仓库" :image-size="60" />
              <el-button type="primary" plain size="small" @click="addRepoFlow">
                <el-icon><Plus /></el-icon>&nbsp;添加仓库
              </el-button>
            </template>
          </div>
        </div>
        <div class="sidebar-footer">
          <el-button class="add-repo-btn" plain @click="addRepoFlow">
            <el-icon><FolderOpened /></el-icon>&nbsp;选择文件夹添加
          </el-button>
          <el-tooltip :content="gitEnv ? (gitEnv.installed ? `Git ${gitEnv.version}` : 'Git 未安装') : '检测中…'" placement="top">
            <el-icon
              class="header-icon settings-icon"
              :class="{ 'settings-icon--warn': gitEnv && (!gitEnv.installed || !gitEnv.userName || !gitEnv.userEmail) }"
              @click="openConfigDialog"
            ><Setting /></el-icon>
          </el-tooltip>
        </div>
      </aside>

      <!-- 主区 -->
      <section class="repo-main">
        <!-- 仓库概览条 -->
        <div v-if="current" class="repo-overview">
          <span class="overview-name" :title="current.path">{{ current.name }}</span>
          <el-tag v-if="current.branch" size="small">{{ current.branch }}</el-tag>
          <span
            v-if="current.shortHash"
            class="overview-hash"
            title="点击复制哈希"
            @click="copyText(current.shortHash, '哈希已复制')"
          >@{{ current.shortHash }}</span>
          <span v-if="current.ahead > 0" class="meta-ahead">领先 {{ current.ahead }}</span>
          <span v-if="current.behind > 0" class="meta-behind">落后 {{ current.behind }}</span>
          <span class="overview-changes" :class="{ dirty: current.changes > 0 }">
            {{ current.changes > 0 ? `${current.changes} 个文件改动` : '工作区干净' }}
          </span>
          <span v-if="current.lastCommit" class="overview-commit" :title="current.lastCommit">{{ current.lastCommit }}</span>
          <el-icon class="header-icon" title="刷新状态" @click="refreshCurrent"><Refresh /></el-icon>
          <el-icon class="header-icon" title="在资源管理器中打开" @click="openRepoFolder"><FolderOpened /></el-icon>
          <el-icon class="header-icon danger" title="移除仓库" @click="removeRepoConfirm"><Delete /></el-icon>
        </div>
        <div v-else class="repo-overview empty">
          <el-empty :description="repos.length > 0 ? '从左侧选择一个仓库' : '添加仓库后开始管理代码'" :image-size="60" />
        </div>

        <!-- 环境检测横幅：git 未安装（红）或身份未配置（黄）时提示 -->
        <div v-if="gitEnv && !gitEnv.installed" class="env-banner env-banner--error">
          <el-icon><Warning /></el-icon>
          <span class="env-banner-text">未检测到 Git（或未加入 PATH），所有 Git 功能不可用。</span>
          <a href="https://git-scm.com/download/win" target="_blank" class="env-banner-link">去下载</a>
          <el-button size="small" plain :loading="envChecking" @click="checkEnv">重新检测</el-button>
        </div>
        <div v-else-if="gitEnv && gitEnv.installed && (!gitEnv.userName || !gitEnv.userEmail)" class="env-banner env-banner--warn">
          <el-icon><Warning /></el-icon>
          <span class="env-banner-text">全局身份未配置（user.name / user.email），提交功能将失败。</span>
          <el-button size="small" type="warning" plain @click="openConfigDialog">去配置</el-button>
          <el-button size="small" plain :loading="envChecking" @click="checkEnv">重新检测</el-button>
        </div>

        <!-- 合并进行中横幅：本次冲突或上次中断遗留时提示，可一键放弃复位 -->
        <div v-if="current?.isRepo && mergeState.inProgress" class="merge-banner">
          <el-icon><Warning /></el-icon>
          <span class="merge-banner-text">
            合并进行中{{ mergeState.sourceBranch ? `（来自 ${mergeState.sourceBranch}）` : '' }}，
            {{ mergeState.conflicts > 0 ? `${mergeState.conflicts} 个文件冲突，解决后需手动提交` : '无冲突' }}
          </span>
          <el-button size="small" type="warning" plain :disabled="!!runningOp" @click="abortMerge">
            放弃合并
          </el-button>
        </div>

        <!-- Sourcetree 风格工具栏 -->
        <div class="sourcetree-toolbar">
          <!-- 拉取 -->
          <button
            class="toolbar-btn"
            :class="{ 'toolbar-btn--loading': actionLoading === 'git pull' }"
            :disabled="!current?.isRepo || !!runningOp"
            title="拉取（git pull）"
            @click="pullCurrent"
          >
            <span class="toolbar-btn-icon">&#x2193;</span>
            <span class="toolbar-btn-label">拉取</span>
            <span v-if="current?.behind > 0" class="toolbar-btn-badge">{{ current.behind }}</span>
          </button>
          <!-- 推送 -->
          <button
            class="toolbar-btn"
            :class="{ 'toolbar-btn--loading': actionLoading === 'git push' }"
            :disabled="!current?.isRepo || !!runningOp"
            title="推送（git push）"
            @click="pushCurrent"
          >
            <span class="toolbar-btn-icon">&#x2191;</span>
            <span class="toolbar-btn-label">推送</span>
            <span v-if="current?.ahead > 0" class="toolbar-btn-badge">{{ current.ahead }}</span>
          </button>
          <div class="toolbar-sep"></div>
          <!-- 分支管理 -->
          <button
            class="toolbar-btn"
            :disabled="!current?.isRepo || !!runningOp"
            title="分支管理"
            @click="openBranchDrawer"
          >
            <span class="toolbar-btn-icon">&#x2387;</span>
            <span class="toolbar-btn-label">分支</span>
          </button>
          <!-- 合并 -->
          <button
            class="toolbar-btn"
            :disabled="!current?.isRepo || !!runningOp"
            title="合并分支"
            @click="openMergeDialog"
          >
            <span class="toolbar-btn-icon">&#x21d4;</span>
            <span class="toolbar-btn-label">合并</span>
          </button>
          <div class="toolbar-sep"></div>
          <!-- 储藏 -->
          <button
            class="toolbar-btn"
            :disabled="!current?.isRepo || !!runningOp"
            title="储藏改动（git stash）"
            @click="stashMsgInput = ''; stashMsgDialogVisible = true"
          >
            <span class="toolbar-btn-icon">&#x2693;</span>
            <span class="toolbar-btn-label">储藏</span>
          </button>
          <!-- 放弃 -->
          <button
            class="toolbar-btn toolbar-btn--danger"
            :disabled="!current?.isRepo || !!runningOp || changesUnstaged.length === 0"
            title="放弃所有未暂存改动"
            @click="discardChanges(null)"
          >
            <span class="toolbar-btn-icon">&#x21a9;</span>
            <span class="toolbar-btn-label">放弃</span>
          </button>
          <div class="toolbar-spacer"></div>

          <!-- 右侧页签图标按钮 -->
          <div class="toolbar-tab-group">
            <button
              class="toolbar-tab-btn"
              :class="{ active: activeTab === 'log' }"
              title="操作日志"
              @click="activeTab = 'log'"
            >
              <span class="ttb-icon">&#x2261;</span>
              <span class="ttb-label">日志</span>
            </button>
            <button
              class="toolbar-tab-btn"
              :class="{ active: activeTab === 'changes' }"
              title="工作区改动"
              @click="activeTab = 'changes'"
            >
              <span class="ttb-icon">&#x270e;</span>
              <span class="ttb-label">改动</span>
              <span
                v-if="changesUnstaged.length > 0"
                class="ttb-badge"
              >{{ changesUnstaged.length }}</span>
            </button>
            <button
              class="toolbar-tab-btn"
              :class="{ active: activeTab === 'history' }"
              title="提交历史"
              @click="activeTab = 'history'"
            >
              <span class="ttb-icon">&#x25f7;</span>
              <span class="ttb-label">历史</span>
            </button>
            <button
              class="toolbar-tab-btn"
              :class="{ active: activeTab === 'knowledge' }"
              title="命令知识库"
              @click="activeTab = 'knowledge'"
            >
              <span class="ttb-icon">&#x1f4d6;</span>
              <span class="ttb-label">知识库</span>
            </button>
            <button
              class="toolbar-tab-btn"
              :class="{ active: activeTab === 'terminal' }"
              title="终端"
              @click="activeTab = 'terminal'"
            >
              <span class="ttb-icon">&gt;_</span>
              <span class="ttb-label">终端</span>
            </button>
          </div>

          <!-- 取消操作 -->
          <button
            v-if="runningOp"
            class="toolbar-btn toolbar-btn--cancel"
            :title="runningOp.label"
            @click="cancelCurrent"
          >
            <span class="toolbar-btn-icon">&#x2715;</span>
            <span class="toolbar-btn-label">取消</span>
          </button>
        </div>

        <!-- 主区页签：操作日志 / 命令知识库 -->
        <div class="main-tabs">

          <!-- 操作日志终端 -->
          <div v-show="activeTab === 'log'" ref="logTerminalRef" class="log-terminal">
            <pre v-if="logLines.length > 0"><span
              v-for="(line, i) in logLines"
              :key="i"
              :class="'log-' + line.kind"
>{{ line.text }}
</span></pre>
            <div v-else class="log-empty">操作记录将在这里显示（拉取 / 推送 / 合并 / 切换 / 命令执行）</div>
          </div>

          <!-- 工作区改动（Sourcetree 风格：上未暂存区 + 下已暂存区 + diff 预览 + 提交栏） -->
          <div v-show="activeTab === 'changes'" class="changes-pane" :class="{ 'is-fullscreen': changesFullscreen }">

            <!-- 工具栏：筛选下拉 + 排序下拉 + 刷新 -->
            <div class="changes-toolbar">
              <el-select v-model="changesFilter" size="small" style="width:130px" @change="checkedUnstaged = []; checkedStaged = []">
                <template #prefix><span style="font-size:11px;color:#909399">显示</span></template>
                <el-option label="所有" value="all" />
                <el-option label="已修改" value="modified" />
                <el-option label="未跟踪" value="untracked" />
                <el-option label="冲突" value="conflict" />
                <el-option label="已暂存" value="pending" />
              </el-select>
              <el-select v-model="changesSort" size="small" style="width:150px">
                <template #prefix><span style="font-size:11px;color:#909399">排序</span></template>
                <el-option label="文件状态" value="status" />
                <el-option label="路径升序" value="path-asc" />
                <el-option label="路径降序" value="path-desc" />
                <el-option label="文件名升序" value="name-asc" />
                <el-option label="文件名降序" value="name-desc" />
                <el-option label="已选在前" value="checked" />
              </el-select>
              <span class="tab-spacer"></span>
              <el-icon class="header-icon" title="刷新改动" @click="reloadChanges"><Refresh /></el-icon>
              <el-icon
                class="header-icon"
                :title="changesFullscreen ? '退出全屏 (ESC)' : '全屏显示'"
                @click="toggleChangesFullscreen"
              ><component :is="changesFullscreen ? Aim : FullScreen" /></el-icon>
            </div>

            <!-- 上下分区 + 右侧 diff 预览 -->
            <div class="changes-split">
              <!-- 左：上未暂存 + 下已暂存 -->
              <div v-loading="changesLoading" class="changes-list" :style="{ width: changesListWidth + 'px' }">

                <!-- 已暂存区（上） -->
                <div class="changes-section" :style="{ height: unstageHeightPct + '%' }">
                  <div class="changes-section-header">
                    <el-checkbox
                      :model-value="filteredStaged.length > 0 && checkedStaged.length === filteredStaged.length"
                      :indeterminate="checkedStaged.length > 0 && checkedStaged.length < filteredStaged.length"
                      :disabled="filteredStaged.length === 0"
                      @change="(v: boolean) => checkedStaged = v ? filteredStaged.map(f => f.path) : []"
                    />
                    <span class="section-title">已暂存文件（{{ changesStaged.length }}）</span>
                    <span class="tab-spacer"></span>
                    <el-button size="small" :disabled="changesStaged.length === 0 || !!runningOp" @click="stageFile(null, false)">取消所有</el-button>
                    <el-button size="small" :disabled="checkedStaged.length === 0 || !!runningOp" @click="unstageChecked">取消所选</el-button>
                  </div>
                  <div class="changes-file-list">
                    <div
                      v-for="f in filteredStaged"
                      :key="'s-' + f.path"
                      class="change-item"
                      :class="{ selected: changesSelected?.file.path === f.path && changesSelected?.staged, conflict: f.isConflict, checked: checkedStaged.includes(f.path) }"
                      @click="onFileItemClick(f, true, $event)"
                      @contextmenu="showContextMenu($event, [
                        { icon: '−', label: '取消暂存此文件', action: () => stageFile(f, false) },
                      ])"
                    >
                      <el-checkbox
                        :model-value="checkedStaged.includes(f.path)"
                        @change="(v: boolean) => { if(v) checkedStaged.push(f.path); else checkedStaged = checkedStaged.filter(p => p !== f.path) }"
                        @click.stop
                      />
                      <span :class="['file-badge', fileBadgeClass(f.status)]" :title="f.status">{{ fileBadgeIcon(f.status) }}</span>
                      <span class="change-path" :title="f.oldPath ? `${f.oldPath} → ${f.path}` : f.path">
                        {{ f.oldPath ? `${f.oldPath} → ${f.path}` : f.path }}
                      </span>
                      <span class="change-actions">
                        <el-button size="small" type="warning" link @click.stop="stageFile(f, false)">取消暂存</el-button>
                      </span>
                    </div>
                    <div v-if="filteredStaged.length === 0" class="changes-empty">
                      {{ changesStaged.length === 0 ? '没有已暂存改动' : '当前筛选条件下无匹配文件' }}
                    </div>
                  </div>
                </div>

                <!-- 横向分隔条（可拖拽调节上下比例） -->
                <div class="changes-divider" @mousedown="startVResize"></div>

                <!-- 未暂存区（下） -->
                <div class="changes-section" :style="{ height: (100 - unstageHeightPct) + '%' }">
                  <div class="changes-section-header">
                    <el-checkbox
                      :model-value="filteredUnstaged.length > 0 && checkedUnstaged.length === filteredUnstaged.length"
                      :indeterminate="checkedUnstaged.length > 0 && checkedUnstaged.length < filteredUnstaged.length"
                      :disabled="filteredUnstaged.length === 0"
                      @change="(v: boolean) => checkedUnstaged = v ? filteredUnstaged.map(f => f.path) : []"
                    />
                    <span class="section-title">未暂存文件（{{ changesUnstaged.length }}）</span>
                    <span class="tab-spacer"></span>
                    <el-button size="small" :disabled="changesUnstaged.length === 0 || !!runningOp" @click="stageFile(null, true)">暂存所有</el-button>
                    <el-button size="small" :disabled="checkedUnstaged.length === 0 || !!runningOp" @click="stageChecked">暂存所选</el-button>
                  </div>
                  <div class="changes-file-list">
                    <div
                      v-for="f in filteredUnstaged"
                      :key="'u-' + f.path"
                      class="change-item"
                      :class="{ selected: changesSelected?.file.path === f.path && !changesSelected?.staged, conflict: f.isConflict, checked: checkedUnstaged.includes(f.path) }"
                      @click="onFileItemClick(f, false, $event)"
                      @contextmenu="showContextMenu($event, [
                        { icon: '+', label: '暂存此文件', action: () => stageFile(f, true) },
                        { icon: '✕', label: '放弃此文件改动', divider: true, danger: true, action: () => discardChanges(f) },
                      ])"
                    >
                      <el-checkbox
                        :model-value="checkedUnstaged.includes(f.path)"
                        @change="(v: boolean) => { if(v) checkedUnstaged.push(f.path); else checkedUnstaged = checkedUnstaged.filter(p => p !== f.path) }"
                        @click.stop
                      />
                      <span :class="['file-badge', fileBadgeClass(f.status)]" :title="f.status">{{ fileBadgeIcon(f.status) }}</span>
                      <span class="change-path" :title="f.oldPath ? `${f.oldPath} → ${f.path}` : f.path">
                        {{ f.oldPath ? `${f.oldPath} → ${f.path}` : f.path }}
                      </span>
                      <span class="change-actions">
                        <el-button size="small" type="primary" link @click.stop="stageFile(f, true)">暂存</el-button>
                        <el-button size="small" type="danger" link @click.stop="discardChanges(f)">放弃</el-button>
                      </span>
                    </div>
                    <div v-if="filteredUnstaged.length === 0" class="changes-empty">
                      {{ changesUnstaged.length === 0 ? '没有未暂存改动' : '当前筛选条件下无匹配文件' }}
                    </div>
                  </div>
                </div>
              </div>

              <!-- 左右拖拽把手 -->
              <div class="changes-col-resizer" @mousedown="startHResize"></div>

              <!-- 右：选中文件 diff 预览 -->
              <div class="changes-preview">
                <template v-if="changesSelected">
                  <div class="changes-preview-head">
                    <span :class="['file-badge', fileBadgeClass(changesSelected.file.status)]" :title="changesSelected.file.status">{{ fileBadgeIcon(changesSelected.file.status) }}</span>
                    <span class="detail-file-path" :title="changesSelected.file.path">{{ changesSelected.file.path }}</span>
                    <el-tag v-if="changesSelected.staged" size="small" type="info">已暂存</el-tag>
                    <el-tag v-else size="small" type="warning" effect="plain">未暂存</el-tag>
                  </div>
                  <div v-loading="changesDiffLoading" class="changes-preview-body">
                    <template v-if="changesDiff && changesDiff.diff">
                      <div class="diff-table">
                        <div
                          v-for="(dl, li) in parseDiffLines(changesDiff.diff)"
                          :key="li"
                          :class="['diff-row', dl.cls]"
                        >
                          <span class="diff-ln old">{{ dl.lineOld ?? '' }}</span>
                          <span class="diff-ln new">{{ dl.lineNew ?? '' }}</span>
                          <span class="diff-code">{{ dl.text }}</span>
                        </div>
                      </div>
                    </template>
                    <div v-else class="changes-empty">无差异内容</div>
                  </div>
                </template>
                <div v-else class="changes-empty fullscreen">点击左侧文件查看 diff</div>
              </div>
            </div>

            <!-- 底部提交栏 -->
            <div class="commit-bar">
              <!-- 第一行：提交说明输入框 + 历史下拉 -->
              <div class="commit-input-wrap">
                <el-input
                  v-model="commitMessage"
                  type="textarea"
                  :rows="2"
                  placeholder="提交说明（必填，如：修复登录失败问题）"
                  maxlength="200"
                  resize="none"
                  class="commit-input"
                />
                <el-dropdown
                  v-if="commitMsgHistory.length > 0"
                  trigger="click"
                  class="commit-history-btn"
                  @command="(msg: string) => commitMessage = msg"
                >
                  <el-button size="small" title="提交消息历史">&#x2935;</el-button>
                  <template #dropdown>
                    <el-dropdown-menu>
                      <el-dropdown-item v-for="(msg, i) in commitMsgHistory" :key="i" :command="msg">
                        <span class="commit-history-item">{{ msg }}</span>
                      </el-dropdown-item>
                    </el-dropdown-menu>
                  </template>
                </el-dropdown>
              </div>
              <!-- 第二行：checkbox + 储藏按钮 + 主提交按钮 -->
              <div class="commit-actions-row">
                <el-checkbox v-model="commitPush" :disabled="committing">提交后推送</el-checkbox>
                <span class="commit-actions-spacer"></span>
                <el-button
                  size="small"
                  :disabled="changesUnstaged.length === 0 && changesStaged.length === 0 || !!runningOp"
                  @click="stashMsgInput = ''; stashMsgDialogVisible = true"
                  title="储藏当前未提交改动"
                >&#x2693; 储藏</el-button>
                <el-button
                  size="small"
                  :disabled="!currentPath"
                  @click="loadStashList(); stashDrawerVisible = true"
                  title="浏览储藏列表"
                >&#x2261; 列表</el-button>
                <el-button
                  type="primary"
                  :loading="committing"
                  :disabled="!commitMessage.trim() || changesStaged.length === 0 || !!runningOp"
                  @click="commitStaged"
                >提交 {{ changesStaged.length }} 个文件</el-button>
              </div>
            </div>

            <!-- Stash 说明输入弹窗 -->
            <el-dialog v-model="stashMsgDialogVisible" title="储藏改动" width="400px" :append-to-body="true">
              <el-input v-model="stashMsgInput" placeholder="储藏说明（空则自动生成）" maxlength="100" clearable />
              <template #footer>
                <el-button @click="stashMsgDialogVisible = false">取消</el-button>
                <el-button type="primary" @click="doStash">确认储藏</el-button>
              </template>
            </el-dialog>

            <!-- Stash 列表抄屉 -->
            <el-drawer v-model="stashDrawerVisible" title="储藏列表" size="380px" :append-to-body="true">
              <div v-loading="stashLoading" class="stash-list">
                <div v-if="stashList.length === 0 && !stashLoading" class="changes-empty">暂无储藏</div>
                <div v-for="s in stashList" :key="s.index" class="stash-item">
                  <div class="stash-item-head">
                    <span class="stash-index">stash@{{ '{' + s.index + '}' }}</span>
                    <span v-if="s.branch" class="stash-branch">{{ s.branch }}</span>
                  </div>
                  <div class="stash-item-msg">{{ s.message }}</div>
                  <div class="stash-item-date">{{ s.date }}</div>
                  <div class="stash-item-actions">
                    <el-button size="small" type="primary" @click="doStashPop(s.index)">应用</el-button>
                    <el-button size="small" type="danger" @click="doStashDrop(s.index)">删除</el-button>
                  </div>
                </div>
              </div>
            </el-drawer>
          </div>

          <!-- 提交历史（Sourcetree 式：分支图形 + 行 + 底部详情） -->
          <div v-show="activeTab === 'history'" class="history-pane" :class="{ 'is-fullscreen': historyFullscreen }">
            <div class="history-toolbar">
              <el-select
                v-model="historyBranch"
                class="history-branch"
                filterable
                clearable
                placeholder="全部历史（当前 HEAD）"
                @change="reloadHistory"
              >
                <el-option v-for="b in historyBranches" :key="b.name" :label="b.name + (b.isRemote ? '（远程）' : '')" :value="b.name" />
              </el-select>
              <el-input
                v-model="historyKeyword"
                placeholder="搜索提交消息..."
                clearable
                size="small"
                class="history-search"
                @change="reloadHistory"
                @clear="reloadHistory"
              >
                <template #prefix><el-icon><Search /></el-icon></template>
              </el-input>
              <span class="history-count">已加载 {{ historyEntries.length }} 条</span>
              <span class="tab-spacer"></span>
              <el-icon class="header-icon" title="刷新历史" @click="reloadHistory"><Refresh /></el-icon>
              <el-icon
                class="header-icon"
                :title="historyFullscreen ? '退出全屏 (ESC)' : '全屏显示'"
                @click="toggleHistoryFullscreen"
              ><component :is="historyFullscreen ? Aim : FullScreen" /></el-icon>
            </div>
            <div class="history-split">
              <!-- 左：提交列表 -->
              <div ref="historyScrollRef" class="commit-list" :style="{ width: historyListWidth + 'px' }" @scroll="onHistoryScroll">
                <div
                  v-for="row in historyRows"
                  :key="row.entry.hash"
                  class="commit-row"
                  :class="{ selected: selectedHash === row.entry.hash }"
                  @click="selectCommit(row.entry)"
                  @contextmenu="showContextMenu($event, [
                    { icon: '📋', label: '复制短哈希 (' + row.entry.shortHash + ')', action: () => copyText(row.entry.shortHash, '短哈希已复制') },
                    { icon: '📋', label: '复制完整哈希', action: () => copyText(row.entry.hash, '完整哈希已复制') },
                    { icon: '📝', label: '复制提交说明', action: () => copyText(row.entry.subject, '提交说明已复制') },
                    { icon: '📄', label: '查看提交详情', divider: true, action: () => selectCommit(row.entry) },
                  ])"
                >
                  <svg class="commit-graph" :width="graphWidth" :height="COMMIT_ROW_H">
                    <template v-for="(s, si) in row.segs" :key="si">
                      <line
                        v-if="s.kind === 'line'"
                        :x1="s.x * LANE_W + 8" :y1="s.y1"
                        :x2="s.x * LANE_W + 8" :y2="s.y2"
                        :stroke="laneColor(s.lane)" stroke-width="2"
                      />
                      <path
                        v-else
                        :d="`M ${s.from * LANE_W + 8} ${COMMIT_ROW_H / 2} C ${s.from * LANE_W + 8} ${COMMIT_ROW_H * 0.75}, ${s.to * LANE_W + 8} ${COMMIT_ROW_H * 0.75}, ${s.to * LANE_W + 8} ${COMMIT_ROW_H}`"
                        :stroke="laneColor(s.lane)" stroke-width="2" fill="none"
                      />
                    </template>
                    <circle :cx="row.lane * LANE_W + 8" :cy="COMMIT_ROW_H / 2" r="4" :fill="laneColor(row.lane)" stroke="#fff" stroke-width="1" />
                  </svg>
                  <span class="commit-refs">
                    <span v-for="r in row.entry.refs" :key="r" class="ref-tag" :class="refClass(r)">{{ r }}</span>
                  </span>
                  <span class="commit-subject" :title="row.entry.subject">{{ row.entry.subject }}</span>
                  <span class="commit-author" :title="row.entry.author">{{ row.entry.author }}</span>
                  <span class="commit-date">{{ row.entry.date }}</span>
                  <span class="commit-hash">{{ row.entry.shortHash }}</span>
                </div>
                <div v-if="historyLoadingMore" class="history-more">加载中…</div>
                <div v-else-if="historyEntries.length > 0 && !historyHasMore" class="history-more">已到底</div>
                <el-empty
                  v-if="!historyLoading && historyEntries.length === 0"
                  description="没有提交记录"
                  :image-size="60"
                />
              </div>

              <!-- 左右拖拽把手 -->
              <div class="history-col-resizer" @mousedown="startHistoryHResize"></div>

              <!-- 右：单提交详情 -->
              <div v-if="selectedDetail || detailLoading" v-loading="detailLoading" class="commit-detail">
                <div v-if="selectedDetail" class="detail-body">
                  <div class="detail-head">
                    <span class="detail-subject" :title="selectedDetail.commit.subject">{{ selectedDetail.commit.subject }}</span>
                    <span class="detail-meta">
                      {{ selectedDetail.commit.shortHash }} · {{ selectedDetail.commit.author }} · {{ selectedDetail.commit.date }}
                    </span>
                    <span class="tab-spacer"></span>
                    <el-icon class="header-icon" title="关闭详情" @click="selectedHash = ''; selectedDetail = null"><CircleClose /></el-icon>
                  </div>
                  <div class="detail-content">
                    <div class="detail-files" :style="{ width: detailFilesWidth + 'px' }">
                      <div
                        v-for="f in selectedDetail.files"
                        :key="f.path"
                        class="detail-file"
                        :class="{ expanded: expandedDiffs.has(f.path) }"
                        @click="toggleDiff(f.path)"
                      >
                        <span :class="['file-badge', fileBadgeClass(f.status)]" :title="f.status">{{ fileBadgeIcon(f.status) }}</span>
                        <span class="detail-file-path" :title="f.oldPath ? `${f.oldPath} → ${f.path}` : f.path">
                          {{ f.oldPath ? `${f.oldPath} → ${f.path}` : f.path }}
                        </span>
                      </div>
                      <div v-if="selectedDetail.files.length === 0" class="history-more">无文件变更</div>
                    </div>
                    <!-- 详情面板内左右拖拽把手 -->
                    <div class="detail-col-resizer" @mousedown="startDetailHResize"></div>
                    <div class="detail-diffs">
                      <div v-for="d in selectedDetail.diffs" v-show="expandedDiffs.has(d.path)" :key="d.path" class="diff-block">
                        <div class="diff-path" :title="d.path">{{ d.path }}</div>
                        <div class="diff-table">
                          <div
                            v-for="(dl, li) in diffLines(d)"
                            :key="li"
                            :class="['diff-row', dl.cls]"
                          >
                            <span class="diff-ln old">{{ dl.lineOld ?? '' }}</span>
                            <span class="diff-ln new">{{ dl.lineNew ?? '' }}</span>
                            <span class="diff-code">{{ dl.text }}</span>
                          </div>
                        </div>
                      </div>
                    </div>
                  </div>
                </div>
              </div>
            </div>
          </div>

          <!-- 命令知识库 -->
          <div v-show="activeTab === 'knowledge'" class="knowledge-pane">
            <div class="knowledge-toolbar">
              <el-input
                v-model="knowledgeSearch"
                placeholder="搜索命令或说明…"
                clearable
                :prefix-icon="Search"
                class="knowledge-search"
              />
              <el-select v-model="knowledgeCategory" class="knowledge-category">
                <el-option v-for="c in gitCategories" :key="c" :label="c" :value="c" />
              </el-select>
            </div>
            <div class="knowledge-list">
              <div
                v-for="entry in filteredCommands"
                :key="entry.command"
                class="knowledge-item"
                :class="{ danger: entry.danger }"
              >
                <div class="knowledge-main">
                  <code class="knowledge-cmd" title="点击复制" @click="copyText(entry.command)">{{ entry.command }}</code>
                  <span class="knowledge-desc">
                    {{ entry.desc }}<template v-if="entry.danger">（危险）</template>
                  </span>
                </div>
                <div class="knowledge-actions">
                  <el-icon class="header-icon" title="复制命令" @click="copyText(entry.command)"><CopyDocument /></el-icon>
                  <el-icon class="header-icon" title="对当前仓库执行" @click="execCommand(entry)"><Promotion /></el-icon>
                </div>
              </div>
              <el-empty v-if="filteredCommands.length === 0" description="没有匹配的命令" :image-size="60" />
            </div>
          </div>

          <!-- 终端页签 -->
          <div v-show="activeTab === 'terminal'" class="terminal-pane">
            <div class="terminal-path-bar">
              <el-icon><FolderOpened /></el-icon>
              <span class="terminal-path-text">{{ currentPath || '未选择仓库' }}</span>
            </div>
            <!-- 输出区（输入行也在内部，像真实终端一样是最后一行） -->
            <div ref="terminalOutputRef" class="terminal-output" @click="focusTerminalInput">
              <div
                v-for="(line, i) in terminalLines"
                :key="i"
                :class="['terminal-line', 'tl-' + line.kind]"
              >{{ line.text }}</div>
              <div v-if="terminalLines.length === 0" class="terminal-empty">输入 git 命令并回车执行，↑↓ 翻阅历史，Tab 补全，cls 清屏</div>
              <!-- 输入行（输出流最后一行） -->
              <div class="terminal-input-area">
                <div class="terminal-suggest-wrap">
                  <div v-show="hasVisibleSuggestions" class="terminal-suggest-list">
                    <div
                      v-for="s in terminalSuggestions"
                      :key="s.command"
                      class="terminal-suggest-item"
                      :class="{ danger: s.danger }"
                      @mousedown.prevent="applySuggestion(s.command)"
                    >
                      <code class="tsi-cmd">{{ s.command.startsWith('git ') ? s.command.slice(4) : s.command }}</code>
                      <span class="tsi-desc">{{ s.desc }}</span>
                    </div>
                  </div>
                </div>
                <div class="terminal-input-row">
                  <span class="terminal-prompt">$</span>
                  <input
                    ref="terminalInputRef"
                    v-model="terminalInput"
                    class="terminal-input"
                    placeholder="输入 git 命令，如 git log --oneline -10"
                    :disabled="terminalRunning"
                    @keydown="onTerminalKeydown"
                    @input="onTerminalInput"
                    @focus="terminalSuggestVisible = true"
                    @blur="terminalSuggestVisible = false"
                  />
                  <span v-if="terminalRunning" class="terminal-running-dot" />
                </div>
              </div>
            </div>
          </div>
        </div>
      </section>
    </div>

    <!-- 克隆仓库弹窗 -->
    <el-dialog v-model="cloneVisible" title="克隆仓库" width="600px" :close-on-click-modal="false">
      <el-form label-position="top" class="clone-form" @submit.prevent>
        <el-form-item label="仓库地址">
          <el-input v-model="cloneUrl" placeholder="https://gitee.com/user/repo.git" clearable />
        </el-form-item>
        <el-form-item label="保存位置">
          <el-input v-model="cloneParentDir" placeholder="选择克隆到哪个目录" readonly>
            <template #append>
              <el-button @click="chooseCloneParent">选择</el-button>
            </template>
          </el-input>
        </el-form-item>
        <el-form-item label="目录名">
          <el-input v-model="cloneDirName" placeholder="留空则从仓库地址推断" clearable />
        </el-form-item>
        <el-form-item label="分支">
          <el-select
            v-model="cloneBranch"
            filterable
            allow-create
            placeholder="默认分支"
            clearable
            :loading="cloneBranchLoading"
            style="width: 100%"
          >
            <el-option v-for="b in cloneBranches" :key="b" :label="b" :value="b" />
          </el-select>
          <div class="clone-hint">输入仓库地址后自动加载分支列表，也可直接输入分支名</div>
        </el-form-item>
        <el-form-item label="分组">
          <el-select v-model="cloneGroup" filterable allow-create clearable placeholder="未分组" style="width: 100%">
            <el-option v-for="g in existingGroups" :key="g" :label="g" :value="g" />
          </el-select>
        </el-form-item>

        <!-- 高级选项折叠 -->
        <div class="clone-advanced-toggle" @click="cloneAdvancedExpand = !cloneAdvancedExpand">
          <span>{{ cloneAdvancedExpand ? '▾' : '▸' }}</span> 高级选项
        </div>
        <div v-show="cloneAdvancedExpand" class="clone-advanced-panel">
          <el-form-item>
            <el-checkbox v-model="cloneShallow" :disabled="!!cloneFilter.trim()">浅克隆（--depth 1）</el-checkbox>
          </el-form-item>
          <el-form-item>
            <el-checkbox v-model="cloneRecurseSubmodules">递归初始化子模块（--recurse-submodules）</el-checkbox>
          </el-form-item>
          <el-form-item>
            <el-checkbox v-model="cloneNoCheckout">无检出（--no-checkout）</el-checkbox>
          </el-form-item>
          <el-form-item label="部分克隆">
            <el-input v-model="cloneFilter" placeholder="如 blob:none" clearable @input="cloneFilter.trim() && (cloneShallow = false)" />
            <div class="clone-hint">延迟下载大文件，按需获取。与浅克隆互斥</div>
          </el-form-item>
          <el-form-item label="自定义远程名">
            <el-input v-model="cloneOriginName" placeholder="默认 origin" clearable />
          </el-form-item>
        </div>

        <!-- 命令预览 -->
        <div v-if="cloneCommandPreview" class="clone-cmd-preview">
          <span class="clone-cmd-label">$</span> {{ cloneCommandPreview }}
        </div>
      </el-form>
      <template #footer>
        <el-button v-if="cloneLoading" type="warning" plain @click="cancelCurrent">取消克隆</el-button>
        <el-button :disabled="cloneLoading" @click="cloneVisible = false">关闭</el-button>
        <el-button
          type="primary"
          :loading="cloneLoading"
          :disabled="!cloneUrl.trim() || !cloneParentDir.trim()"
          @click="confirmClone"
        >
          {{ cloneLoading ? '克隆中…' : '开始克隆' }}
        </el-button>
      </template>
    </el-dialog>

    <!-- 发现仓库弹窗 -->
    <el-dialog v-model="discoverVisible" title="发现仓库" width="560px">
      <div class="discover-tip">
        在 {{ discoverPath }} 的一级子目录中发现 {{ discovered.length }} 个 Git 仓库，勾选后批量添加：
      </div>
      <div class="discover-list">
        <div v-for="f in discovered" :key="f.path" class="discover-item">
          <el-checkbox
            :model-value="discoverChecked.has(f.path)"
            @change="(v) => toggleDiscovered(f.path, v === true)"
          >
            <span class="discover-name">{{ f.name }}</span>
            <el-tag v-if="f.branch" size="small" type="info">{{ f.branch }}</el-tag>
            <span class="discover-path">{{ f.path }}</span>
          </el-checkbox>
        </div>
      </div>
      <template #footer>
        <el-button @click="discoverVisible = false">取消</el-button>
        <el-button type="primary" @click="confirmDiscover">添加勾选仓库</el-button>
      </template>
    </el-dialog>

    <!-- 合并分支弹窗 -->
    <el-dialog v-model="mergeVisible" title="合并分支" width="480px">
      <div v-if="current" class="merge-tip">
        把来源分支的提交合入当前分支 <el-tag size="small">{{ current.branch }}</el-tag>：
      </div>
      <el-select v-model="mergeSource" filterable placeholder="选择来源分支（可搜索，含远程分支）" style="width: 100%">
        <el-option
          v-for="b in mergeBranches"
          :key="b.name"
          :label="b.name + (b.isRemote ? '（远程）' : '')"
          :value="b.name"
        />
      </el-select>
      <template #footer>
        <el-button @click="mergeVisible = false">取消</el-button>
        <el-button type="primary" :disabled="!mergeSource" @click="confirmMerge">开始合并</el-button>
      </template>
    </el-dialog>

    <!-- 分支管理抽屉 -->
    <el-drawer v-model="branchDrawerVisible" title="分支管理" size="460px">
      <div v-if="current" class="branch-drawer">
        <div class="branch-create">
          <el-input v-model="newBranchName" placeholder="新分支名" clearable style="width: 150px" />
          <el-select
            v-model="newBranchBase"
            placeholder="起点（默认当前 HEAD）"
            clearable
            filterable
            style="width: 190px"
          >
            <el-option v-for="b in branches" :key="b.name" :label="b.name" :value="b.name" />
          </el-select>
          <el-button type="primary" :disabled="!newBranchName.trim()" @click="createBranch">新建并切换</el-button>
        </div>
        <div v-loading="branchLoading" class="branch-list">
          <div v-for="b in branches" :key="b.name" class="branch-item">
            <span class="branch-name">{{ b.name }}</span>
            <el-tag v-if="b.isCurrent" size="small" type="success">当前</el-tag>
            <el-tag v-if="b.isRemote" size="small" type="info" effect="plain">远程</el-tag>
            <span class="branch-spacer"></span>
            <el-button v-if="!b.isCurrent" size="small" type="primary" link @click="switchBranch(b)">
              {{ b.isRemote ? '检出跟踪分支' : '切换' }}
            </el-button>
          </div>
          <el-empty v-if="!branchLoading && branches.length === 0" description="暂无分支" :image-size="60" />
        </div>
      </div>
    </el-drawer>

    <!-- Git 配置弹窗 -->
    <el-dialog v-model="configDialogVisible" title="Git 全局配置" width="640px" :close-on-click-modal="false">
      <!-- 身份卡片区 -->
      <div class="config-identity">
        <div class="config-identity-title">提交身份</div>
        <div class="config-identity-row">
          <span class="config-identity-label">user.name</span>
          <el-input
            :value="configList.find(i => i.key === 'user.name')?.value ?? ''"
            placeholder="全局提交名字"
            style="flex:1"
            @change="(v: string) => gitConfigSet({ key: 'user.name', value: v }).then(() => { void loadConfigList(); void checkEnv() })"
          />
        </div>
        <div class="config-identity-row">
          <span class="config-identity-label">user.email</span>
          <el-input
            :value="configList.find(i => i.key === 'user.email')?.value ?? ''"
            placeholder="全局提交邮筱"
            style="flex:1"
            @change="(v: string) => gitConfigSet({ key: 'user.email', value: v }).then(() => { void loadConfigList(); void checkEnv() })"
          />
        </div>
      </div>

      <!-- 完整配置表格 -->
      <div v-loading="configLoading" class="config-table">
        <div class="config-table-header">
          <span class="config-col-key">配置键</span>
          <span class="config-col-val">配置值</span>
          <span class="config-col-actions"></span>
        </div>
        <div v-for="item in configList" :key="item.key" class="config-table-row">
          <span class="config-col-key" :title="item.key">{{ item.key }}</span>
          <div class="config-col-val">
            <el-input
              v-if="editingKey === item.key"
              v-model="editingValue"
              size="small"
              autofocus
              @keyup.enter="saveEdit(item)"
              @keyup.escape="editingKey = null"
            />
            <span v-else class="config-val-text" :title="item.value">{{ item.value }}</span>
          </div>
          <div class="config-col-actions">
            <template v-if="editingKey === item.key">
              <el-button size="small" type="primary" link :loading="configSaving" @click="saveEdit(item)">保存</el-button>
              <el-button size="small" link @click="editingKey = null">取消</el-button>
            </template>
            <template v-else>
              <el-button size="small" link @click="startEdit(item)">编辑</el-button>
              <el-button size="small" type="danger" link :loading="configSaving" @click="deleteConfig(item.key)">删除</el-button>
            </template>
          </div>
        </div>
        <div class="config-table-add">
          <el-input v-model="newConfigKey" size="small" placeholder="新配置键（如 http.proxy）" class="config-col-key" />
          <el-input v-model="newConfigValue" size="small" placeholder="值" class="config-col-val" @keyup.enter="addConfig" />
          <el-button size="small" type="primary" :loading="configSaving" @click="addConfig">添加</el-button>
        </div>
      </div>
      <template #footer>
        <el-button @click="configDialogVisible = false">关闭</el-button>
        <el-button plain :loading="configLoading" @click="loadConfigList">刷新</el-button>
      </template>
    </el-dialog>
  </div>

  <!-- 全局右键菜单（Teleport 到 body，防止被 overflow 裁切） -->
  <Teleport to="body">
    <div
      v-if="contextMenu.visible"
      class="ctx-menu"
      :style="{ left: contextMenu.x + 'px', top: contextMenu.y + 'px' }"
      @click.stop
    >
      <template v-for="(item, idx) in contextMenu.items" :key="idx">
        <!-- 分隔线（在本项上方，项本身仍渲染） -->
        <div v-if="item.divider" class="ctx-divider"></div>
        <!-- 带子菜单：悬停向右侧展开 -->
        <div v-if="item.children" class="ctx-item ctx-item--parent">
          <span v-if="item.icon" class="ctx-icon">{{ item.icon }}</span>
          <span class="ctx-label">{{ item.label }}</span>
          <span class="ctx-arrow">&#x25B8;</span>
          <div class="ctx-submenu">
            <template v-for="(sub, si) in item.children" :key="si">
              <div v-if="sub.divider" class="ctx-divider"></div>
              <div
                class="ctx-item"
                :class="{ 'ctx-item--danger': sub.danger, 'ctx-item--disabled': sub.disabled }"
                @click="runMenuItem(sub)"
              >
                <span v-if="sub.icon" class="ctx-icon">{{ sub.icon }}</span>
                <span class="ctx-label">{{ sub.label }}</span>
              </div>
            </template>
          </div>
        </div>
        <!-- 普通项 -->
        <div
          v-else
          class="ctx-item"
          :class="{ 'ctx-item--danger': item.danger, 'ctx-item--disabled': item.disabled }"
          @click="runMenuItem(item)"
        >
          <span v-if="item.icon" class="ctx-icon">{{ item.icon }}</span>
          <span class="ctx-label">{{ item.label }}</span>
        </div>
      </template>
    </div>
  </Teleport>
</template>

<style scoped>
.git-workbench-page {
  padding: 16px;
  /* height:100%（非 min-height）：主窗口与独立窗口（.standalone-page 为 100vh+overflow:hidden）
     下父容器高度均确定；正常高度时工作台内部各自滚动，极矮窗口时页面级滚动兜底 */
  height: 100%;
  overflow-y: auto;
  display: flex;
  flex-direction: column;
}

.workbench-layout {
  flex: 1;
  min-height: 520px;
  display: flex;
  gap: 16px;
}

/* ============================ 左栏：仓库列表 ============================ */

.repo-sidebar {
  width: 250px;
  flex-shrink: 0;
  display: flex;
  flex-direction: column;
  background: #fff;
  border: 1px solid #e4e7ed;
  border-radius: 8px;
  overflow: hidden;
}

.sidebar-header {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 12px 14px;
  border-bottom: 1px solid #e4e7ed;
}

.sidebar-title {
  flex: 1;
  font-weight: 600;
  font-size: 15px;
}

.repo-list {
  flex: 1;
  overflow-y: auto;
  padding: 8px;
}

/* 分组标题（一级：静态浅背景，与子项形成层级） */
.repo-group-header {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 6px 8px;
  cursor: pointer;
  user-select: none;
  border-radius: 4px;
  margin: 4px 0 2px;
  background: #f0f2f5;
  transition: background 0.15s;
}

.repo-group-header:hover {
  background: #ebeef5;
}

/* 拖拽仓库悬停时高亮放置目标 */
.repo-group-header.drag-over {
  background: #ecf5ff;
  outline: 2px dashed #409eff;
  outline-offset: -2px;
}

/* 拖动排序中的分组 header */
.repo-group-header:active {
  cursor: grabbing;
}

/* 拖拽仓库时的「移出分组」放置区 */
.repo-ungroup-dropzone {
  margin: 4px 8px;
  padding: 10px 8px;
  text-align: center;
  font-size: 12px;
  color: #909399;
  border: 1px dashed #dcdfe6;
  border-radius: 4px;
  background: #fafafa;
  user-select: none;
}

.repo-ungroup-dropzone.drag-over {
  color: #409eff;
  border-color: #409eff;
  background: #ecf5ff;
}

/* 仓库项可拖拽 */
.repo-item[draggable='true'] {
  cursor: grab;
}

.repo-group-arrow {
  font-size: 12px;
  color: #909399;
  width: 12px;
  text-align: center;
  flex-shrink: 0;
}

.repo-group-label {
  font-size: 13px;
  font-weight: 600;
  color: #606266;
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.repo-group-count {
  font-size: 11px;
  color: #909399;
  background: #ebeef5;
  border-radius: 10px;
  padding: 0 6px;
  line-height: 18px;
  flex-shrink: 0;
}

.repo-item {
  position: relative;
  padding: 10px 12px;
  border-radius: 6px;
  cursor: pointer;
  border: 1px solid transparent;
  margin-bottom: 4px;
  transition: background 0.2s, border-color 0.2s;
}

/* 分组内仓库（二级：缩进 + 左侧引导线） */
.repo-item.grouped {
  margin-left: 18px;
  border-left: 2px solid #e4e7ed;
}

.repo-item.grouped.selected {
  border-left-color: #409eff;
}

/* 拖拽排序插入指示线 */
.repo-item.drop-above {
  position: relative;
}
.repo-item.drop-above::before {
  content: '';
  position: absolute;
  top: -1px;
  left: 4px;
  right: 4px;
  height: 2px;
  background: #409eff;
  border-radius: 1px;
  z-index: 2;
}
.repo-item.drop-below {
  position: relative;
}
.repo-item.drop-below::after {
  content: '';
  position: absolute;
  bottom: -1px;
  left: 4px;
  right: 4px;
  height: 2px;
  background: #409eff;
  border-radius: 1px;
  z-index: 2;
}

.repo-item:hover {
  background: #f5f7fa;
}

.repo-item.selected {
  background: #ecf5ff;
  border-color: #409eff;
}

/* 目录失效的仓库灰显 */
.repo-item.invalid {
  opacity: 0.6;
}

.repo-search-bar {
  padding: 0 8px 6px;
  flex-shrink: 0;
}

.repo-item-top {
  display: flex;
  align-items: flex-start;
  gap: 8px;
}

/* 置顶图标：正常态仅悬停上展示，已置顶始终显示金色 */
.repo-pin-icon {
  flex-shrink: 0;
  font-size: 14px;
  color: #c0c4cc;
  opacity: 0;
  transition: opacity .15s, color .15s;
  cursor: pointer;
  margin-top: 1px;
}

.repo-item:hover .repo-pin-icon {
  opacity: 1;
}

.repo-pin-icon.pinned {
  color: #e6a23c;
  opacity: 1;
}

.repo-pin-icon:hover {
  color: #f0a020;
}

/* 置顶/普通分隔线 */
.repo-pin-divider {
  height: 1px;
  background: #e4e7ed;
  margin: 4px 0;
}

.repo-name {
  flex: 1;
  min-width: 0;
  /* 长仓库名最多两行、超出省略（title 提示完整名）；overflow-wrap 优先在中文处断行，
     不把 czk_ 这类前缀拆成单字符（break-all 的碎行问题） */
  display: -webkit-box;
  -webkit-box-orient: vertical;
  -webkit-line-clamp: 2;
  overflow: hidden;
  overflow-wrap: anywhere;
  line-height: 1.4;
  font-weight: 600;
  font-size: 13px;
}

.repo-item-meta {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
  margin-top: 4px;
  font-size: 12px;
  color: #909399;
}

.meta-ahead {
  color: #e6a23c;
}

.meta-behind {
  color: #409eff;
}

.meta-changes {
  color: #67c23a;
}

.meta-changes.dirty {
  color: #e6a23c;
}

.meta-error {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  color: #f56c6c;
}

.repo-empty {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 8px;
  padding: 20px 0;
}

.sidebar-footer {
  padding: 10px;
  border-top: 1px solid #e4e7ed;
}

.add-repo-btn {
  width: 100%;
}

/* ============================ 主区 ============================ */

.repo-main {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.repo-overview {
  display: flex;
  align-items: center;
  gap: 10px;
  flex-wrap: wrap;
  background: #f5f7fa;
  border-radius: 8px;
  padding: 12px 16px;
  min-height: 52px;
}

.repo-overview.empty {
  justify-content: center;
  background: #fff;
  border: 1px dashed #e4e7ed;
}

.overview-name {
  font-weight: 600;
  font-size: 15px;
}

.overview-hash {
  font-family: 'Consolas', 'Monaco', monospace;
  color: #909399;
  font-size: 13px;
  cursor: pointer;
}

.overview-hash:hover {
  color: #409eff;
}

.overview-changes {
  font-size: 13px;
  color: #67c23a;
}

.overview-changes.dirty {
  color: #e6a23c;
}

.overview-commit {
  flex: 1;
  min-width: 120px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-size: 12px;
  color: #909399;
}

/* ============================ Sourcetree 风格工具栏 ============================ */

.sourcetree-toolbar {
  display: flex;
  align-items: center;
  gap: 2px;
  padding: 6px 10px;
  background: #f5f7fa;
  border-bottom: 1px solid #e4e7ed;
  flex-shrink: 0;
}

.toolbar-btn {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  min-width: 52px;
  padding: 5px 8px;
  border: none;
  border-radius: 5px;
  background: transparent;
  cursor: pointer;
  transition: background 0.15s;
  position: relative;
  color: #303133;
}

.toolbar-btn:hover:not(:disabled) {
  background: #e6ecf6;
}

.toolbar-btn:active:not(:disabled) {
  background: #d0ddf6;
}

.toolbar-btn:disabled {
  opacity: 0.45;
  cursor: not-allowed;
}

.toolbar-btn--loading {
  opacity: 0.7;
  cursor: wait;
}

.toolbar-btn--danger .toolbar-btn-icon,
.toolbar-btn--danger .toolbar-btn-label {
  color: #f56c6c;
}

.toolbar-btn--cancel .toolbar-btn-icon,
.toolbar-btn--cancel .toolbar-btn-label {
  color: #e6a23c;
}

.toolbar-btn-icon {
  font-size: 18px;
  line-height: 1;
  margin-bottom: 2px;
  color: #409eff;
}

.toolbar-btn--danger .toolbar-btn-icon { color: #f56c6c; }
.toolbar-btn--cancel .toolbar-btn-icon { color: #e6a23c; }

.toolbar-btn-label {
  font-size: 11px;
  line-height: 1;
  white-space: nowrap;
  color: #606266;
}

.toolbar-btn-badge {
  position: absolute;
  top: 2px;
  right: 4px;
  min-width: 16px;
  height: 16px;
  padding: 0 4px;
  border-radius: 8px;
  background: #f56c6c;
  color: #fff;
  font-size: 10px;
  line-height: 16px;
  text-align: center;
}

.toolbar-sep {
  width: 1px;
  height: 30px;
  background: #dcdfe6;
  margin: 0 4px;
  flex-shrink: 0;
}

.toolbar-spacer {
  flex: 1;
}

/* ============================ 工具栏页签图标按钮 ============================ */

.toolbar-tab-group {
  display: flex;
  align-items: center;
  gap: 2px;
  margin-left: 4px;
  padding-left: 8px;
  border-left: 1px solid #dcdfe6;
}

.toolbar-tab-btn {
  position: relative;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  width: 44px;
  height: 44px;
  border: none;
  border-radius: 6px;
  background: transparent;
  cursor: pointer;
  color: #606266;
  transition: background 0.15s, color 0.15s;
  padding: 0;
  flex-shrink: 0;
}

.toolbar-tab-btn:hover {
  background: #e6ecf6;
}

.toolbar-tab-btn.active {
  background: #409eff;
  color: #fff;
}

.ttb-icon {
  font-size: 15px;
  line-height: 1;
}

.ttb-label {
  font-size: 10px;
  line-height: 1;
  margin-top: 2px;
  white-space: nowrap;
}

.ttb-badge {
  position: absolute;
  top: 4px;
  right: 4px;
  min-width: 14px;
  height: 14px;
  border-radius: 7px;
  background: #f56c6c;
  color: #fff;
  font-size: 9px;
  line-height: 14px;
  text-align: center;
  padding: 0 3px;
}

.action-bar {
  display: flex;
}

/* ============================ 合并进行中横幅 ============================ */

.merge-banner {
  display: flex;
  align-items: center;
  gap: 10px;
  background: #fdf6ec;
  border: 1px solid #f3d19e;
  border-radius: 8px;
  padding: 8px 16px;
  font-size: 13px;
  color: #b88230;
}

.merge-banner > .el-icon {
  color: #e6a23c;
  flex-shrink: 0;
}

.merge-banner-text {
  flex: 1;
  min-width: 0;
  line-height: 1.5;
}

/* ============================ 主区页签 ============================ */

.main-tabs {
  flex: 1;
  min-height: 0;
  display: flex;
  flex-direction: column;
  background: #fff;
  border: 1px solid #e4e7ed;
  border-radius: 8px;
  overflow: hidden;
}

/* .tab-header/.tab-btn 已移除，页签改为工具栏右侧图标按钮 */

.tab-spacer {
  flex: 1;
}

/* ============================ 操作日志终端 ============================ */

.log-terminal {
  flex: 1;
  min-height: 200px;
  background: #1e1e1e;
  color: #d4d4d4;
  padding: 12px;
  overflow: auto;
  font-family: 'Consolas', 'Monaco', monospace;
  font-size: 12px;
  line-height: 1.6;
}

.log-terminal pre {
  margin: 0;
  white-space: pre-wrap;
  word-break: break-all;
}

.log-terminal .log-cmd { color: #61afef; font-weight: 600; }
.log-terminal .log-out { color: #d4d4d4; }
.log-terminal .log-err { color: #f56c6c; }
.log-terminal .log-ok { color: #67c23a; }
.log-terminal .log-warn { color: #e6a23c; }

.log-empty {
  color: #6a737d;
  padding: 24px 0;
  text-align: center;
}

/* .tab-count.dirty-count 已移除，角标改为 .ttb-badge */

/* ============================ 全屏模式 ============================ */

/* changes-pane 和 history-pane 共用的全屏 class */
.changes-pane.is-fullscreen,
.history-pane.is-fullscreen {
  position: fixed;
  inset: 0;
  z-index: 2000;
  background: #fff;
  display: flex;
  flex-direction: column;
  /* 在全屏模式下覆盖任何 overflow 限制 */
  overflow: hidden;
}

/* ============================ 工作区改动（暂存/提交） ============================ */

.changes-pane {
  flex: 1;
  min-height: 0;
  display: flex;
  flex-direction: column;
}

.changes-toolbar {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 6px 12px;
  border-bottom: 1px solid #e4e7ed;
  flex-shrink: 0;
}

.changes-split {
  flex: 1;
  min-height: 0;
  display: flex;
}

/* 左：上未暂存区 + 分隔条 + 下已暂存区 */
.changes-list {
  /* 宽度由 JS changesListWidth 内联覆盖，这里只做默认展现 */
  width: 380px;
  flex-shrink: 0;
  border-right: none; /* 分隔由 changes-col-resizer 承担 */
  display: flex;
  flex-direction: column;
  min-height: 0;
  overflow: hidden;
}

/* 上下各占一半，独立滚动 */
.changes-section {
  /* 高度由 JS unstageHeightPct 内联覆盖 */
  flex-shrink: 0;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.changes-section-header {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 5px 10px;
  background: #f5f7fa;
  border-bottom: 1px solid #e4e7ed;
  flex-shrink: 0;
  position: sticky;
  top: 0;
  z-index: 1;
}

.section-title {
  font-size: 12px;
  font-weight: 600;
  color: #606266;
  white-space: nowrap;
}

.changes-file-list {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  padding: 2px 0;
}

/* 横向分隔条（上下拖拽） */
.changes-divider {
  height: 5px;
  background: #e4e7ed;
  flex-shrink: 0;
  cursor: row-resize;
  transition: background .15s;
}

.changes-divider:hover {
  background: #c0c4cc;
}

/* 竖向分隔把手（左右拖拽） */
.changes-col-resizer {
  width: 5px;
  flex-shrink: 0;
  cursor: col-resize;
  background: #e4e7ed;
  transition: background .15s;
  position: relative;
  z-index: 1;
}

.changes-col-resizer:hover {
  background: #c0c4cc;
}

.change-item {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 3px 10px;
  cursor: pointer;
  font-size: 12px;
}

.change-item:hover {
  background: #f5f7fa;
}

.change-item.selected {
  background: #ecf5ff;
}

/* 冲突文件红底警示 */
.change-item.conflict {
  background: #fdecea;
}

.change-item.conflict:hover {
  background: #fbd9d9;
}

.change-path {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-family: 'Consolas', 'Monaco', monospace;
  color: #303133;
}

.change-actions {
  display: none;
  gap: 2px;
  flex-shrink: 0;
}

.change-item:hover .change-actions {
  display: flex;
}

.changes-empty {
  padding: 8px 12px;
  font-size: 12px;
  color: #909399;
}

.changes-empty.fullscreen {
  display: flex;
  align-items: center;
  justify-content: center;
  height: 100%;
}

/* 右：diff 预览 */
.changes-preview {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
}

.changes-preview-head {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 8px 12px;
  background: #fafafa;
  border-bottom: 1px solid #f0f2f5;
}

.changes-preview-body {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
}

/* 底部提交栏 */
.commit-bar {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 10px 12px;
  border-top: 1px solid #e4e7ed;
  flex-shrink: 0;
}

.commit-input {
  flex: 1;
}

.commit-actions-row {
  display: flex;
  align-items: center;
  gap: 8px;
}

.commit-actions-spacer {
  flex: 1;
}

/* ============================ 提交历史 ============================ */

.history-pane {
  flex: 1;
  min-height: 0;
  display: flex;
  flex-direction: column;
}

.history-toolbar {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 10px 12px;
  border-bottom: 1px solid #e4e7ed;
}

.history-branch {
  width: 220px;
}

.history-count {
  font-size: 12px;
  color: #909399;
}

.history-split {
  flex: 1;
  min-height: 0;
  display: flex;
  flex-direction: row;
}

.commit-list {
  /* 宽度由 JS historyListWidth 内联覆盖 */
  width: 560px;
  flex-shrink: 0;
  min-height: 0;
  overflow-y: auto;
}

/* 提交历史左右拖拽把手 */
.history-col-resizer {
  width: 5px;
  flex-shrink: 0;
  cursor: col-resize;
  background: #e4e7ed;
  transition: background .15s;
}

.history-col-resizer:hover {
  background: #c0c4cc;
}

.commit-row {
  display: flex;
  align-items: center;
  gap: 8px;
  height: 28px;
  padding: 0 12px;
  cursor: pointer;
  font-size: 13px;
}

.commit-row:hover {
  background: #f5f7fa;
}

.commit-row.selected {
  background: #ecf5ff;
}

.commit-graph {
  flex-shrink: 0;
  display: block;
}

.commit-refs {
  display: flex;
  gap: 4px;
  flex-shrink: 0;
  max-width: 320px;
  overflow: hidden;
}

/* refs 彩色标签（Sourcetree 同款语义） */
.ref-tag {
  font-size: 11px;
  border-radius: 8px;
  padding: 0 6px;
  line-height: 16px;
  white-space: nowrap;
  border: 1px solid transparent;
}

.ref-tag.ref-head {
  color: #388e3c;
  background: #e8f5e9;
  border-color: #a5d6a7;
}

.ref-tag.ref-branch {
  color: #1565c0;
  background: #e3f2fd;
  border-color: #90caf9;
}

.ref-tag.ref-remote {
  color: #607d8b;
  background: #eceff1;
  border-color: #b0bec5;
}

.ref-tag.ref-tag {
  color: #e65100;
  background: #fff3e0;
  border-color: #ffcc80;
}

.commit-subject {
  flex: 1;
  min-width: 120px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  color: #303133;
}

.commit-author {
  flex-shrink: 0;
  max-width: 140px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  color: #909399;
}

.commit-date {
  flex-shrink: 0;
  color: #909399;
}

.commit-hash {
  flex-shrink: 0;
  font-family: 'Consolas', 'Monaco', monospace;
  color: #c7254e;
  font-size: 12px;
}

.history-more {
  padding: 10px 0;
  text-align: center;
  font-size: 12px;
  color: #909399;
}

/* 单提交详情（左右布局：右侧充满剩余宽度） */
.commit-detail {
  flex: 1;
  min-width: 0;
  border-left: none;
  border-top: none;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.detail-body {
  display: flex;
  flex-direction: column;
  height: 100%;
  min-height: 0;
}

.detail-head {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 8px 12px;
  background: #f5f7fa;
  border-bottom: 1px solid #e4e7ed;
}

.detail-subject {
  font-weight: 600;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.detail-meta {
  flex-shrink: 0;
  font-size: 12px;
  color: #909399;
}

.detail-content {
  flex: 1;
  min-height: 0;
  display: flex;
  gap: 0;
}

/* 左：文件列表；右：展开的 diff */
.detail-files {
  /* width 由 :style 动态绑定 */
  flex-shrink: 0;
  overflow-y: auto;
  border-right: none;  /* 由拖拽条替代边框 */
  padding: 6px 0;
}

.detail-col-resizer {
  width: 5px;
  flex-shrink: 0;
  background: transparent;
  border-left: 1px solid #e4e7ed;
  cursor: col-resize;
  transition: background 0.15s;
}

.detail-col-resizer:hover,
.detail-col-resizer:active {
  background: #409eff33;
  border-left-color: #409eff;
}

.detail-file {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 4px 12px;
  cursor: pointer;
  font-size: 12px;
}

.detail-file:hover {
  background: #f5f7fa;
}

.detail-file.expanded {
  background: #ecf5ff;
}

.detail-file-path {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  color: #303133;
  font-family: 'Consolas', 'Monaco', monospace;
}

.detail-diffs {
  flex: 1;
  min-width: 0;
  overflow-y: auto;
}

.diff-block {
  border-bottom: 1px solid #e4e7ed;
}

.diff-path {
  padding: 6px 12px;
  font-size: 12px;
  font-family: 'Consolas', 'Monaco', monospace;
  color: #606266;
  background: #fafafa;
  border-bottom: 1px solid #f0f2f5;
  word-break: break-all;
}

.diff-text {
  margin: 0;
  padding: 6px 0;
  font-family: 'Consolas', 'Monaco', monospace;
  font-size: 12px;
  line-height: 1.5;
  white-space: pre;
  overflow-x: auto;
}

.diff-text span {
  display: block;
  padding: 0 12px;
}

.dl-add { color: #2e7d32; background: #e8f5e9; }
.dl-del { color: #c62828; background: #fdecea; }
.dl-hunk { color: #0277bd; background: #e1f5fe; }
.dl-meta { color: #909399; }
.dl-ctx { color: #303133; }

/* ============================ 命令知识库 ============================ */

.knowledge-pane {
  flex: 1;
  min-height: 0;
  display: flex;
  flex-direction: column;
}

.knowledge-toolbar {
  display: flex;
  gap: 10px;
  padding: 12px;
  border-bottom: 1px solid #e4e7ed;
}

.knowledge-search {
  width: 260px;
}

.knowledge-category {
  width: 140px;
}

.knowledge-list {
  flex: 1;
  overflow-y: auto;
  padding: 10px 12px;
}

.knowledge-item {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 9px 12px;
  border-radius: 6px;
  border: 1px solid transparent;
}

.knowledge-item:hover {
  background: #f5f7fa;
  border-color: #e4e7ed;
}

.knowledge-main {
  flex: 1;
  min-width: 0;
  display: flex;
  align-items: baseline;
  gap: 12px;
  flex-wrap: wrap;
}

.knowledge-cmd {
  font-family: 'Consolas', 'Monaco', monospace;
  font-size: 13px;
  color: #c7254e;
  background: #f9f2f4;
  border-radius: 3px;
  padding: 2px 6px;
  cursor: pointer;
}

.knowledge-cmd:hover {
  background: #f0e0e5;
}

/* 危险命令：命令文本红色警示 */
.knowledge-item.danger .knowledge-cmd {
  color: #f56c6c;
}

.knowledge-desc {
  font-size: 12px;
  color: #909399;
}

.knowledge-actions {
  display: flex;
  gap: 6px;
  flex-shrink: 0;
}

/* ============================ 弹窗 / 抽屉 ============================ */

.header-icon {
  font-size: 16px;
  color: #909399;
  cursor: pointer;
  transition: color 0.2s;
  flex-shrink: 0;
}

.header-icon:hover {
  color: #409eff;
}

.header-icon.danger:hover {
  color: #f56c6c;
}

.clone-form :deep(.el-form-item) {
  margin-bottom: 14px;
}

.clone-hint {
  font-size: 12px;
  color: #909399;
  margin-top: 4px;
  line-height: 1.4;
}

/* 高级选项折叠 */
.clone-advanced-toggle {
  display: flex;
  align-items: center;
  gap: 4px;
  padding: 6px 0;
  cursor: pointer;
  user-select: none;
  font-size: 13px;
  color: #606266;
  font-weight: 500;
  transition: color 0.15s;
}

.clone-advanced-toggle:hover {
  color: #409eff;
}

.clone-advanced-panel {
  padding: 4px 0 0 4px;
  border-left: 2px solid #e4e7ed;
  margin-left: 6px;
  margin-bottom: 8px;
}

/* 命令预览 */
.clone-cmd-preview {
  background: #f5f7fa;
  border: 1px solid #e4e7ed;
  border-radius: 6px;
  padding: 10px 14px;
  font-family: 'Cascadia Code', Consolas, 'Courier New', monospace;
  font-size: 12px;
  color: #303133;
  word-break: break-all;
  line-height: 1.6;
  margin-top: 4px;
}

.clone-cmd-label {
  color: #909399;
  margin-right: 4px;
  user-select: none;
}

.discover-tip {
  font-size: 13px;
  color: #606266;
  margin-bottom: 12px;
}

.discover-list {
  max-height: 360px;
  overflow-y: auto;
}

.discover-item {
  padding: 8px 10px;
  border-radius: 6px;
}

.discover-item:hover {
  background: #f5f7fa;
}

.discover-name {
  font-weight: 600;
  margin-right: 8px;
}

.discover-path {
  display: block;
  font-size: 12px;
  color: #909399;
  margin-top: 2px;
}

.merge-tip {
  font-size: 13px;
  color: #606266;
  margin-bottom: 12px;
}

.branch-drawer {
  display: flex;
  flex-direction: column;
  height: 100%;
}

.branch-create {
  display: flex;
  gap: 8px;
  margin-bottom: 14px;
}

.branch-list {
  flex: 1;
  overflow-y: auto;
}

.branch-item {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 9px 10px;
  border-radius: 6px;
}

.branch-item:hover {
  background: #f5f7fa;
}

.branch-name {
  font-family: 'Consolas', 'Monaco', monospace;
  font-size: 13px;
}

.branch-spacer {
  flex: 1;
}

/* ============================ 环境检测横幅 ============================ */

.env-banner {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 8px 14px;
  font-size: 13px;
  flex-shrink: 0;
}

.env-banner--error {
  background: #fef0f0;
  color: #f56c6c;
  border-bottom: 1px solid #fbc4c4;
}

.env-banner--warn {
  background: #fdf6ec;
  color: #e6a23c;
  border-bottom: 1px solid #f5dab1;
}

.env-banner > .el-icon { flex-shrink: 0; font-size: 16px; }

.env-banner-text { flex: 1; min-width: 0; }

.env-banner-link {
  color: inherit;
  font-weight: 600;
  text-decoration: underline;
  white-space: nowrap;
}

/* 侧栏底部齿轮图标 */
.settings-icon {
  font-size: 16px;
  cursor: pointer;
  color: #909399;
  flex-shrink: 0;
}

.settings-icon:hover { color: #409eff; }
.settings-icon--warn { color: #e6a23c; }
.settings-icon--warn:hover { color: #cf9236; }

/* sidebar-footer 改为 flex 容纳齿轮图标 */
.sidebar-footer {
  display: flex;
  align-items: center;
  gap: 6px;
}

.sidebar-footer .add-repo-btn { flex: 1; }

/* ============================ Git 配置弹窗 ============================ */

.config-identity {
  border: 1px solid #e4e7ed;
  border-radius: 6px;
  padding: 12px 16px;
  margin-bottom: 16px;
}

.config-identity-title {
  font-size: 12px;
  color: #909399;
  margin-bottom: 10px;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: .04em;
}

.config-identity-row {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-bottom: 8px;
}

.config-identity-row:last-child { margin-bottom: 0; }

.config-identity-label {
  font-family: 'Consolas', 'Monaco', monospace;
  font-size: 13px;
  color: #606266;
  width: 90px;
  flex-shrink: 0;
}

.config-table {
  border: 1px solid #e4e7ed;
  border-radius: 6px;
  overflow: hidden;
  max-height: 360px;
  overflow-y: auto;
}

.config-table-header,
.config-table-row,
.config-table-add {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 6px 10px;
}

.config-table-header {
  background: #f5f7fa;
  font-size: 12px;
  color: #909399;
  font-weight: 600;
  border-bottom: 1px solid #e4e7ed;
}

.config-table-row {
  border-bottom: 1px solid #f0f2f5;
  font-size: 13px;
}

.config-table-row:last-of-type { border-bottom: none; }

.config-table-add {
  border-top: 1px solid #e4e7ed;
  background: #fafafa;
  gap: 6px;
}

.config-col-key {
  width: 200px;
  flex-shrink: 0;
  font-family: 'Consolas', 'Monaco', monospace;
  font-size: 13px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.config-col-val {
  flex: 1;
  min-width: 0;
  overflow: hidden;
}

.config-val-text {
  display: block;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-family: 'Consolas', 'Monaco', monospace;
  font-size: 12px;
  color: #606266;
}

.config-col-actions {
  width: 90px;
  flex-shrink: 0;
  display: flex;
  gap: 2px;
  justify-content: flex-end;
}

/* 拉取/推送按钮上的数量角标 */
.action-btn-badge {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 16px;
  height: 16px;
  padding: 0 4px;
  margin-left: 5px;
  border-radius: 8px;
  background: rgba(255, 255, 255, 0.35);
  color: inherit;
  font-size: 11px;
  font-weight: 700;
  line-height: 1;
  vertical-align: middle;
}

/* 已勾选行高亮（Shift/Ctrl 多选） */
.change-item.checked {
  background: #e8f4ff;
}

.change-item.checked:hover {
  background: #d4eaff;
}

/* ===== 文件状态徽章（仿 Sourcetree）===== */
.file-badge {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 16px;
  height: 16px;
  border-radius: 3px;
  font-size: 11px;
  font-weight: 700;
  line-height: 1;
  flex-shrink: 0;
  color: #fff;
  user-select: none;
}
.fb-added     { background: #2ecc71; }
.fb-modified  { background: #e6a23c; }
.fb-deleted   { background: #f56c6c; }
.fb-renamed   { background: #409eff; }
.fb-copied    { background: #9b59b6; }
.fb-conflict  { background: #c0392b; }
.fb-untracked { background: #2ecc71; }
.fb-default   { background: #b0b8c5; }

/* ===== 仓库列表改动数角标 ===== */
.repo-changes-badge {
  position: absolute;
  top: 4px;
  right: 6px;
  min-width: 16px;
  height: 16px;
  padding: 0 4px;
  border-radius: 8px;
  background: #e6a23c;
  color: #fff;
  font-size: 10px;
  font-weight: 700;
  line-height: 16px;
  text-align: center;
  z-index: 1;
  pointer-events: none;
}

/* ===== diff 行号表格 ===== */
.diff-table {
  font-family: 'Consolas', 'Monaco', 'Courier New', monospace;
  font-size: 12px;
  line-height: 1.5;
  overflow-x: auto;
  background: #1e1e2e;
  border-radius: 0 0 4px 4px;
}

.diff-row {
  display: flex;
  align-items: stretch;
  min-height: 20px;
}

.diff-row:hover {
  filter: brightness(1.08);
}

.diff-ln {
  display: inline-block;
  min-width: 40px;
  padding: 0 6px;
  color: #636e8a;
  text-align: right;
  user-select: none;
  flex-shrink: 0;
  border-right: 1px solid #2d2d44;
  font-size: 11px;
  line-height: 20px;
}

.diff-code {
  flex: 1;
  padding: 0 8px;
  white-space: pre;
  overflow: hidden;
  color: #cdd6f4;
  line-height: 20px;
}

/* diff 行混入配色 */
.diff-row.dl-add  { background: #1a3d2b; }
.diff-row.dl-add .diff-code { color: #a6e3a1; }
.diff-row.dl-add .diff-ln   { background: #15332200; color: #52a472; }
.diff-row.dl-del  { background: #3d1a1a; }
.diff-row.dl-del .diff-code { color: #f38ba8; }
.diff-row.dl-del .diff-ln   { background: #33151500; color: #a45252; }
.diff-row.dl-hunk { background: #1a2a40; }
.diff-row.dl-hunk .diff-code { color: #89b4fa; font-style: italic; }
.diff-row.dl-meta { background: #111122; }
.diff-row.dl-meta .diff-code { color: #585b70; font-style: italic; }
.diff-row.dl-ctx  { background: transparent; }

/* ===== 提交栏输入包裹 ===== */
.commit-input-wrap {
  display: flex;
  align-items: flex-start;
  gap: 4px;
  flex: 1;
  min-width: 0;
}

.commit-input-wrap .commit-input { flex: 1; }

.commit-history-btn {
  flex-shrink: 0;
  align-self: flex-start;
}

.commit-history-item {
  display: block;
  max-width: 320px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-size: 12px;
}

/* ===== Stash 列表抄屉 ===== */
.stash-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 4px 0;
}

.stash-item {
  border: 1px solid #e4e7ed;
  border-radius: 6px;
  padding: 10px 12px;
  background: #fafafa;
}

.stash-item-head {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 4px;
}

.stash-index {
  font-family: 'Consolas', monospace;
  font-size: 12px;
  color: #409eff;
  font-weight: 600;
}

.stash-branch {
  font-size: 11px;
  color: #909399;
  background: #f0f2f5;
  border-radius: 3px;
  padding: 1px 5px;
}

.stash-item-msg {
  font-size: 13px;
  color: #303133;
  margin-bottom: 4px;
  word-break: break-all;
}

.stash-item-date {
  font-size: 11px;
  color: #909399;
  margin-bottom: 8px;
}

.stash-item-actions {
  display: flex;
  gap: 8px;
}

/* ===== 历史搜索框 ===== */
.history-search {
  width: 200px;
  flex-shrink: 0;
}

/* ===== 终端页签 ===== */
.terminal-pane {
  display: flex;
  flex-direction: column;
  height: 100%;
  overflow: hidden;
  background: #1a1b27;
  color: #cdd6f4;
  font-family: 'Consolas', 'Monaco', 'Courier New', monospace;
  font-size: 13px;
}

.terminal-path-bar {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 5px 12px;
  background: #12131e;
  color: #6c7086;
  font-size: 12px;
  flex-shrink: 0;
  border-bottom: 1px solid #2a2b3d;
  overflow: hidden;
}

.terminal-path-text {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  flex: 1;
}

.terminal-output {
  flex: 1;
  overflow-y: auto;
  padding: 8px 12px;
  display: flex;
  flex-direction: column;
  gap: 1px;
}

.terminal-empty {
  color: #585b70;
  font-style: italic;
  font-size: 12px;
  margin-top: 8px;
}

.terminal-line {
  white-space: pre-wrap;
  word-break: break-all;
  line-height: 1.55;
}

.tl-cmd  { color: #89dceb; font-weight: 700; }
.tl-out  { color: #cdd6f4; }
.tl-err  { color: #f38ba8; }
.tl-ok   { color: #a6e3a1; }
.tl-warn { color: #f9e2af; }

/* 终端输入区（在输出流内部，像真实终端的最后一行） */
.terminal-input-area {
  position: relative;
}

.terminal-input-row {
  display: flex;
  align-items: center;
  padding: 2px 0;
}

.terminal-prompt {
  color: #6c7086;
  margin-right: 8px;
  user-select: none;
  flex-shrink: 0;
}

.terminal-suggest-wrap {
  position: relative;
  flex: 1;
  min-width: 0;
}

.terminal-input {
  flex: 1;
  min-width: 0;
  background: transparent;
  border: none;
  color: #cdd6f4;
  font-family: inherit;
  font-size: 13px;
  outline: none;
  padding: 0;
}

.terminal-input:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.terminal-running-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: #f9e2af;
  flex-shrink: 0;
  animation: terminal-pulse 1s ease-in-out infinite;
}

@keyframes terminal-pulse {
  0%, 100% { opacity: 1; }
  50% { opacity: 0.3; }
}

.terminal-suggest-list {
  position: absolute;
  bottom: 100%;
  left: 0;
  right: 0;
  background: #1e1f30;
  border: 1px solid #3a3b55;
  border-radius: 6px 6px 0 0;
  max-height: 280px;
  overflow-y: auto;
  z-index: 100;
  box-shadow: 0 -4px 16px rgba(0,0,0,0.4);
}

.terminal-suggest-item {
  display: flex;
  align-items: baseline;
  gap: 10px;
  padding: 7px 12px;
  cursor: pointer;
  border-bottom: 1px solid #2a2b3d;
  transition: background 0.15s;
}

.terminal-suggest-item:last-child { border-bottom: none; }

.terminal-suggest-item:hover {
  background: #2a2b42;
}

.tsi-cmd {
  color: #89dceb;
  font-family: inherit;
  font-size: 12px;
  font-weight: 700;
  white-space: nowrap;
  flex-shrink: 0;
  min-width: 130px;
}

.tsi-desc {
  color: #6c7086;
  font-size: 12px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-family: var(--el-font-family, sans-serif);
}

.terminal-suggest-item.danger .tsi-cmd { color: #f38ba8; }
</style>

<!-- 右键菜单使用 Teleport 脱离组件根节点，scoped 样式水印不到，需用全局样式 -->
<style>
.ctx-menu {
  position: fixed;
  z-index: 9999;
  min-width: 160px;
  background: #fff;
  border: 1px solid #e4e7ed;
  border-radius: 6px;
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.12);
  padding: 4px 0;
  user-select: none;
  font-size: 13px;
}

.ctx-divider {
  height: 1px;
  background: #f0f2f5;
  margin: 3px 0;
}

.ctx-item {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 6px 14px;
  cursor: pointer;
  color: #303133;
  transition: background .12s;
  white-space: nowrap;
}

.ctx-item:hover {
  background: #f5f7fa;
}

.ctx-item--danger {
  color: #f56c6c;
}

.ctx-item--danger:hover {
  background: #fef0f0;
}

.ctx-item--disabled {
  color: #c0c4cc;
  cursor: not-allowed;
}

.ctx-item--disabled:hover {
  background: transparent;
}

.ctx-icon {
  font-size: 13px;
  width: 16px;
  text-align: center;
  flex-shrink: 0;
}

.ctx-label {
  flex: 1;
}

/* 带子菜单的项（悬停向右展开） */
.ctx-item--parent {
  position: relative;
}

.ctx-arrow {
  font-size: 11px;
  color: #909399;
  margin-left: 16px;
  flex-shrink: 0;
}

.ctx-submenu {
  display: none;
  position: absolute;
  left: 100%;
  top: -5px;
  min-width: 150px;
  background: #fff;
  border: 1px solid #e4e7ed;
  border-radius: 6px;
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.12);
  padding: 4px 0;
  max-height: 320px;
  overflow-y: auto;
}

.ctx-item--parent:hover > .ctx-submenu {
  display: block;
}
</style>

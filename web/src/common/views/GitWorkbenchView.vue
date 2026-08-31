<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, ref, watch } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import {
  CircleClose, Connection, CopyDocument, Delete, Download, FullScreen, FolderOpened, Aim, Plus, Promotion, Refresh, Search, Setting, Star, Switch, Upload, Warning,
} from '@element-plus/icons-vue'
import {
  addGitRepo, discoverGitRepos, getGitBranches, getGitChanges, getGitCommitDetail, getGitConfigList, getGitEnv, getGitFileDiff, getGitLog,
  getGitMergeState, getGitRepos, getGitStatus, gitCancel, gitCheckout, gitClone, gitCommitChanges, gitConfigSet, gitDiscard,
  gitExec, gitMerge, gitPull, gitPush, gitStage, removeGitRepo,
  type GitBranch, type GitChangeFile, type GitCommandResult, type GitCommitDetail, type GitConfigItem, type GitDiffFile,
  type GitDiscoveredRepo, type GitEnv, type GitFileDiff, type GitLogEntry, type GitMergeState, type GitRepoStatus,
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

const filteredRepos = computed(() => {
  const q = repoSearch.value.trim().toLowerCase()
  const list = q
    ? repos.value.filter(r => r.name.toLowerCase().includes(q) || r.path.toLowerCase().includes(q))
    : repos.value
  const pinned = pinnedPaths.value
  return [...list].sort((a, b) => {
    const ap = pinned.includes(a.path) ? 0 : 1
    const bp = pinned.includes(b.path) ? 0 : 1
    return ap - bp
  })
})

/** 搜索结果中是否同时存在置顶和普通仓库（用于显示分隔线） */
const hasPinDivider = computed(() => {
  const pinned = pinnedPaths.value
  return filteredRepos.value.some(r => pinned.includes(r.path)) &&
         filteredRepos.value.some(r => !pinned.includes(r.path))
})

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
  }
}

// ============================ 右键菜单 ============================

interface ContextMenuItem {
  label: string
  icon?: string          // emoji/文字图标
  danger?: boolean       // 红色危险项
  divider?: boolean      // 在本项上方显示分隔线
  disabled?: boolean
  action: () => void
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
  if (item.disabled) return
  hideContextMenu()
  item.action()
}

function onDocumentClick() {
  hideContextMenu()
}
/** 当前选中仓库路径（唯一标识） */
const currentPath = ref('')
const current = computed(() => repos.value.find(r => r.path === currentPath.value))

async function loadRepos() {
  reposLoading.value = true
  try {
    repos.value = await getGitRepos()
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
    const status = await getGitStatus({ path: currentPath.value })
    const idx = repos.value.findIndex(r => r.path === currentPath.value)
    if (idx >= 0) repos.value[idx] = status
  } catch {
    // 静默：状态刷新失败不打扰
  }
  void loadMergeState()
}

onMounted(async () => {
  await loadRepos()
  if (repos.value.length > 0 && !currentPath.value) {
    currentPath.value = repos.value[0].path
  }
  void checkEnv()
  document.addEventListener('keydown', onGlobalKeydown)
  document.addEventListener('click', onDocumentClick)
})

onUnmounted(() => {
  document.removeEventListener('keydown', onGlobalKeydown)
  document.removeEventListener('click', onDocumentClick)
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

// ============================ 克隆仓库 ============================

const cloneVisible = ref(false)
const cloneUrl = ref('')
const cloneParentDir = ref('')
const cloneDirName = ref('')
const cloneLoading = ref(false)
/** 上次克隆保存位置（非敏感，可记住） */
const CLONE_PARENT_KEY = 'git-clone-parent-v1'

/** 从仓库地址推断目录名：去查询串与尾斜杠后取末段，再去掉 .git 后缀 */
function inferDirName(url: string): string {
  const tail = url.split(/[?#]/)[0].replace(/\/+$/, '')
  const last = tail.split('/').pop() || ''
  return last.toLowerCase().endsWith('.git') ? last.slice(0, -4) : last
}

// 地址变化时自动推断目录名（覆盖旧值，与提示文案一致）
watch(cloneUrl, (url) => {
  cloneDirName.value = inferDirName(url.trim())
})

function openCloneDialog() {
  cloneVisible.value = true
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
  appendLog('cmd', `$ git clone ${url}${name ? ' ' + name : ''}`)
  appendLog('out', '克隆中…大仓库耗时较长（可随时取消）')
  const before = new Set(repos.value.map(r => r.path))
  try {
    const result = await gitClone({ url, parentDir: parent, dirName: name, opId: op.opId })
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
const activeTab = ref<'log' | 'changes' | 'history' | 'knowledge'>('log')

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
  if (tab === 'changes' && (changesDirty.value || changesStaged.value.length + changesUnstaged.value.length === 0)) reloadChanges()
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
/** 当前选中预览 diff 的文件 */
const changesSelected = ref<{ file: GitChangeFile; staged: boolean } | null>(null)
const changesDiff = ref<GitFileDiff | null>(null)
const changesDiffLoading = ref(false)
const commitMessage = ref('')
const commitPush = ref(false)
const committing = ref(false)

// ---------- 拖拽分割 ----------
/** 左侧文件列表宽度（px） */
const changesListWidth = ref(380)
/** 未暂存区占整个左侧面板的高度百分比（0-100） */
const unstageHeightPct = ref(50)
/** 提交历史左侧（提交列表）宽度（px） */
const historyListWidth = ref(560)

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

// ---------- 筛选 & 排序 ----------
/** 显示类型：'modified' | 'untracked' | 'conflict' | 'pending' | 'all' */
const changesFilter = ref<'modified' | 'untracked' | 'conflict' | 'pending' | 'all'>('modified')
/** 排序方式：'status' | 'path-asc' | 'path-desc' | 'name-asc' | 'name-desc' | 'checked' */
const changesSort = ref<'status' | 'path-asc' | 'path-desc' | 'name-asc' | 'name-desc' | 'checked'>('status')

/** 匹配显示类型筛选 */
function matchFilter(f: GitChangeFile, staged: boolean): boolean {
  switch (changesFilter.value) {
    case 'modified': return f.status !== '?' && !f.isConflict
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

async function reloadChanges() {
  if (!currentPath.value) {
    ElMessage.warning('请先在左侧选择仓库')
    return
  }
  changesLoading.value = true
  try {
    const dto = await getGitChanges({ path: currentPath.value })
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
  if (status === '?') return '?'
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

/** diff 行按行缓存（避免模板每次渲染重复 split 大文本） */
const diffLinesCache = new WeakMap<GitDiffFile, string[]>()
function diffLines(d: GitDiffFile): string[] {
  let lines = diffLinesCache.get(d)
  if (!lines) {
    lines = d.diff.split('\n')
    diffLinesCache.set(d, lines)
  }
  return lines
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
        <div v-loading="reposLoading" class="repo-list">
          <template v-for="(repo, idx) in filteredRepos" :key="repo.path">
            <!-- 置顶与普通之间的分隔线 -->
            <div
              v-if="hasPinDivider && idx > 0 && !pinnedPaths.includes(repo.path) && pinnedPaths.includes(filteredRepos[idx - 1].path)"
              class="repo-pin-divider"
            ></div>
            <div
              class="repo-item"
              :class="{ selected: repo.path === currentPath, invalid: !repo.isRepo }"
              :title="repo.message || repo.path"
              @click="selectRepo(repo.path)"
              @contextmenu="showContextMenu($event, [
                { icon: pinnedPaths.includes(repo.path) ? '★' : '☆', label: pinnedPaths.includes(repo.path) ? '取消置顶' : '置顶', action: () => togglePin(repo.path) },
                { icon: '↓', label: '拉取', divider: true, disabled: !repo.isRepo || !!runningOp, action: () => { selectRepo(repo.path); pullCurrent() } },
                { icon: '↑', label: '推送', disabled: !repo.isRepo || !!runningOp, action: () => { selectRepo(repo.path); pushCurrent() } },
                { icon: '🔀', label: '合并', disabled: !repo.isRepo || !!runningOp, action: () => { selectRepo(repo.path); nextTick(openMergeDialog) } },
                { icon: '⎇', label: '分支管理', disabled: !repo.isRepo, action: () => { selectRepo(repo.path); nextTick(openBranchDrawer) } },
                { icon: '📂', label: '在资源管理器打开', divider: true, disabled: !repo.isRepo, action: () => { selectRepo(repo.path); nextTick(openRepoFolder) } },
                { icon: '📋', label: '复制路径', action: () => copyText(repo.path, '路径已复制') },
                { icon: '🗑️', label: '移除仓库', divider: true, danger: true, action: () => { selectRepo(repo.path); nextTick(removeRepoConfirm) } },
              ])"
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

        <!-- 操作按钮（运行中禁用其余操作，避免并发导致取消目标混乱） -->
        <div class="action-bar">
          <el-button type="primary" :disabled="!current?.isRepo || !!runningOp" :loading="actionLoading === 'git pull'" @click="pullCurrent">
            <el-icon><Download /></el-icon>&nbsp;拉取
          </el-button>
          <el-button :disabled="!current?.isRepo || !!runningOp" :loading="actionLoading === 'git push'" @click="pushCurrent">
            <el-icon><Upload /></el-icon>&nbsp;推送
          </el-button>
          <el-button :disabled="!current?.isRepo || !!runningOp" @click="openMergeDialog">
            <el-icon><Connection /></el-icon>&nbsp;合并
          </el-button>
          <el-button :disabled="!current?.isRepo || !!runningOp" @click="openBranchDrawer">
            <el-icon><Switch /></el-icon>&nbsp;分支管理
          </el-button>
          <el-button v-if="runningOp" type="warning" plain :title="runningOp.label" @click="cancelCurrent">
            <el-icon><CircleClose /></el-icon>&nbsp;取消操作
          </el-button>
        </div>

        <!-- 主区页签：操作日志 / 命令知识库 -->
        <div class="main-tabs">
          <div class="tab-header">
            <div class="tab-btn" :class="{ active: activeTab === 'log' }" @click="activeTab = 'log'">操作日志</div>
            <div class="tab-btn" :class="{ active: activeTab === 'changes' }" @click="activeTab = 'changes'">
              工作区改动<span
                v-if="changesUnstaged.length + changesStaged.length > 0"
                class="tab-count dirty-count"
              >{{ changesUnstaged.length + changesStaged.length }}</span>
            </div>
            <div class="tab-btn" :class="{ active: activeTab === 'history' }" @click="activeTab = 'history'">
              提交历史<span v-if="historyEntries.length > 0" class="tab-count">{{ historyEntries.length }}</span>
            </div>
            <div class="tab-btn" :class="{ active: activeTab === 'knowledge' }" @click="activeTab = 'knowledge'">
              命令知识库<span class="tab-count">{{ gitCommands.length }}</span>
            </div>
            <span class="tab-spacer"></span>
            <template v-if="activeTab === 'log'">
              <el-icon class="header-icon" title="复制日志" @click="copyLog"><CopyDocument /></el-icon>
              <el-icon class="header-icon danger" title="清空日志" @click="clearLog"><Delete /></el-icon>
            </template>
          </div>

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
                      :class="{ selected: changesSelected?.file.path === f.path && changesSelected?.staged, conflict: f.isConflict }"
                      @click="previewDiff(f, true)"
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
                      :class="{ selected: changesSelected?.file.path === f.path && !changesSelected?.staged, conflict: f.isConflict }"
                      @click="previewDiff(f, false)"
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
                    <pre v-if="changesDiff && changesDiff.diff" class="diff-text"><span
                      v-for="(line, li) in changesDiff.diff.split('\n')"
                      :key="li"
                      :class="diffLineClass(line)"
                    >{{ line }}
</span></pre>
                    <div v-else class="changes-empty">无差异内容</div>
                  </div>
                </template>
                <div v-else class="changes-empty fullscreen">点击左侧文件查看 diff</div>
              </div>
            </div>

            <!-- 底部提交栏 -->
            <div class="commit-bar">
              <el-input
                v-model="commitMessage"
                type="textarea"
                :rows="2"
                placeholder="提交说明（必填，如：修复登录失败问题）"
                maxlength="200"
                resize="none"
                class="commit-input"
              />
              <div class="commit-actions">
                <el-checkbox v-model="commitPush" :disabled="committing">提交并推送</el-checkbox>
                <el-button
                  type="primary"
                  :loading="committing"
                  :disabled="!commitMessage.trim() || changesStaged.length === 0 || !!runningOp"
                  @click="commitStaged"
                >
                  提交（{{ changesStaged.length }} 个文件）
                </el-button>
              </div>
            </div>
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
                    <div class="detail-files">
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
                    <div class="detail-diffs">
                      <div v-for="d in selectedDetail.diffs" v-show="expandedDiffs.has(d.path)" :key="d.path" class="diff-block">
                        <div class="diff-path" :title="d.path">{{ d.path }}</div>
                        <pre class="diff-text"><span
                          v-for="(line, li) in diffLines(d)"
                          :key="li"
                          :class="diffLineClass(line)"
                        >{{ line }}
</span></pre>
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
        </div>
      </section>
    </div>

    <!-- 克隆仓库弹窗 -->
    <el-dialog v-model="cloneVisible" title="克隆仓库" width="540px" :close-on-click-modal="false">
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
          <div class="clone-hint">已从仓库地址自动推断，可修改；目标目录已存在且非空时会中止</div>
        </el-form-item>
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
        <div v-if="item.divider" class="ctx-divider"></div>
        <div
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

.repo-item {
  padding: 10px 12px;
  border-radius: 6px;
  cursor: pointer;
  border: 1px solid transparent;
  margin-bottom: 4px;
  transition: background 0.2s, border-color 0.2s;
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

.tab-header {
  display: flex;
  align-items: center;
  gap: 4px;
  padding: 0 12px;
  border-bottom: 1px solid #e4e7ed;
}

.tab-btn {
  padding: 11px 14px;
  font-size: 14px;
  color: #606266;
  cursor: pointer;
  border-bottom: 2px solid transparent;
  margin-bottom: -1px;
  user-select: none;
}

.tab-btn:hover {
  color: #409eff;
}

.tab-btn.active {
  color: #409eff;
  font-weight: 600;
  border-bottom-color: #409eff;
}

.tab-count {
  margin-left: 4px;
  font-size: 11px;
  background: #f0f2f5;
  border-radius: 8px;
  padding: 1px 6px;
  color: #909399;
}

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

/* 页签角标有改动时红点提示 */
.tab-count.dirty-count {
  background: #fdecea;
  color: #f56c6c;
}

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
  align-items: flex-end;
  gap: 12px;
  padding: 10px 12px;
  border-top: 1px solid #e4e7ed;
}

.commit-input {
  flex: 1;
}

.commit-actions {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: 8px;
  flex-shrink: 0;
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
  width: 320px;
  flex-shrink: 0;
  overflow-y: auto;
  border-right: 1px solid #e4e7ed;
  padding: 6px 0;
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
.fb-untracked { background: #909399; }
.fb-default   { background: #b0b8c5; }
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
</style>

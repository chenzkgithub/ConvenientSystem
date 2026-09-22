<script setup lang="ts">
import { onMounted, ref, computed, nextTick, reactive, provide } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Menu, Plus, Refresh, Search, Folder, Document, Link, ArrowRight } from '@element-plus/icons-vue'
import { getMenus, saveMenus } from '@/common/api/menu'
import { getViews, type ViewDto } from '@/common/api/view'
import { viewComponentOptions } from '@/common/viewComponents'
import { useMenuStore } from '@/common/stores/menu'
import type { MenuNode } from '@/common/types'
import CommonDialog from '@/common/components/CommonDialog.vue'
import MenuTreeRow from '@/common/components/MenuTreeRow.vue'

// 视图列表（供编辑抽屉下拉选择）
const viewOptions = ref<ViewDto[]>([])

// 原始菜单树（含“系统管理”节点，保存时完整提交）
const rawMenus = ref<MenuNode[]>([])
const loading = ref(false)

// 顶部工具条：搜索关键词 + 树展开状态（展开集语义：默认全部折叠，点箭头展开）
const keyword = ref('')
const expandedKeys = ref<Set<string>>(new Set())

// 当前选中行（详情面板展示）
const selectedPath = ref<number[]>([])

/** 节点在展开状态里的 key：优先数据库 Id，未保存的新节点退回路径 */
function nodeKey(node: MenuNode, path: number[]): string {
  return node.id != null ? `id:${node.id}` : `tmp:${path.join('-')}`
}

// 过滤掉"系统管理→菜单管理"节点，只用于展示
function filterSelfMenus(nodes: MenuNode[]): MenuNode[] {
  return nodes
    .filter(n => !(n.title === '系统管理' && n.children?.length === 1 && n.children[0].title === '菜单管理'))
    .map(n => ({
      ...n,
      children: n.children?.length ? filterSelfMenus(n.children) : [],
    }))
}

/** 搜索过滤：保留命中节点（整棵子树）与命中节点的祖先链 */
function matchNode(n: MenuNode, kw: string): boolean {
  const k = kw.toLowerCase()
  return (n.title || '').toLowerCase().includes(k)
    || (n.page || '').toLowerCase().includes(k)
    || (n.name || '').toLowerCase().includes(k)
    || (n.component || '').toLowerCase().includes(k)
}
function filterByKeyword(nodes: MenuNode[], kw: string): MenuNode[] {
  const out: MenuNode[] = []
  for (const n of nodes) {
    if (matchNode(n, kw)) {
      out.push(n)
    } else if (n.children?.length) {
      const kids = filterByKeyword(n.children, kw)
      if (kids.length) out.push({ ...n, children: kids })
    }
  }
  return out
}

const displayMenus = computed(() => {
  const base = filterSelfMenus(rawMenus.value)
  const kw = keyword.value.trim()
  return kw ? filterByKeyword(base, kw) : base
})

/** 搜索时强制展开所有节点，否则默认折叠、仅展开集中的节点可见 */
function isRowExpanded(node: MenuNode, path: number[]): boolean {
  if (keyword.value.trim()) return true
  return expandedKeys.value.has(nodeKey(node, path))
}

function toggleExpand(node: MenuNode, path: number[]) {
  const key = nodeKey(node, path)
  const next = new Set(expandedKeys.value)
  if (next.has(key)) next.delete(key)
  else next.add(key)
  expandedKeys.value = next
}

function collectFolderKeys(nodes: MenuNode[], parentPath: number[], out: Set<string>) {
  for (let i = 0; i < nodes.length; i++) {
    const p = [...parentPath, i]
    if (nodes[i].children?.length) {
      out.add(nodeKey(nodes[i], p))
      collectFolderKeys(nodes[i].children!, p, out)
    }
  }
}

const allExpanded = computed(() => {
  const folders = new Set<string>()
  collectFolderKeys(displayMenus.value, [], folders)
  return folders.size > 0 && folders.size === expandedKeys.value.size &&
    [...folders].every(k => expandedKeys.value.has(k))
})

function toggleExpandAll() {
  if (allExpanded.value) {
    expandedKeys.value = new Set()
    return
  }
  const folders = new Set<string>()
  collectFolderKeys(displayMenus.value, [], folders)
  expandedKeys.value = folders
}

// 通过路径获取节点和父数组
function getNodeByPath(path: number[]): { parent: MenuNode[]; node: MenuNode; index: number } | null {
  if (path.length === 0) return null
  let current: MenuNode[] = rawMenus.value
  let node: MenuNode | null = null
  for (let i = 0; i < path.length; i++) {
    const idx = path[i]
    if (idx >= current.length) return null
    node = current[idx]
    if (i < path.length - 1) {
      current = node.children || []
    }
  }
  return node ? { parent: current, node, index: path[path.length - 1] } : null
}

// 获取指定路径的 children 数组
function getChildrenByPath(path: number[]): MenuNode[] {
  if (path.length === 0) return rawMenus.value
  let current: MenuNode[] = rawMenus.value
  for (let i = 0; i < path.length; i++) {
    const idx = path[i]
    if (idx >= current.length) return []
    if (i === path.length - 1) return current[idx].children || []
    current = current[idx].children || []
  }
  return []
}

/** 将后端返回的菜单树进行规范化，确保布尔字段有明确的默认值 */
function normalizeMenus(nodes: MenuNode[]): MenuNode[] {
  return nodes.map((n) => ({
    ...n,
    id: n.id,
    visible: n.visible !== false,
    float: n.float === true,
    editable: n.editable !== false,
    external: n.external === true,
    enabled: n.enabled !== false,
    children: n.children?.length ? normalizeMenus(n.children) : [],
  }))
}

async function loadMenus() {
  loading.value = true
  try {
    // 先记录展开节点的索引路径：保存后数据库 Id 全量刷新（全删全插），
    // 展开键（id 优先）会失效，但树结构不变，按索引路径重映射即可恢复展开状态
    const expandedPaths: number[][] = []
    const collectExpanded = (nodes: MenuNode[], parentPath: number[]) => {
      for (let i = 0; i < nodes.length; i++) {
        const p = [...parentPath, i]
        if (nodes[i].children?.length) {
          if (expandedKeys.value.has(nodeKey(nodes[i], p))) expandedPaths.push(p)
          collectExpanded(nodes[i].children!, p)
        }
      }
    }
    collectExpanded(rawMenus.value, [])

    rawMenus.value = normalizeMenus(await getMenus())

    // 按原索引路径在新树上恢复展开状态
    const restored = new Set<string>()
    for (const p of expandedPaths) {
      const info = getNodeByPath(p)
      if (info && info.node.children?.length) restored.add(nodeKey(info.node, p))
    }
    expandedKeys.value = restored

    // Id 全量刷新后旧选中路径可能失效，越界则清空
    if (selectedPath.value.length && !getNodeByPath(selectedPath.value)) {
      selectedPath.value = []
    }
  } catch { /* 错误已由 request.ts 弹出提示 */ } finally {
    loading.value = false
  }
}

async function loadViewOptions() {
  try { viewOptions.value = await getViews() } catch { viewOptions.value = [] }
}

onMounted(async () => { await loadMenus(); await loadViewOptions() })

const menuStore = useMenuStore()

async function handleSave(successMsg = '已保存') {
  try {
    const res = await saveMenus(rawMenus.value)
    if (res.ok) {
      ElMessage.success(successMsg)
      // 保存后数据库菜单 Id 已全部刷新（全删全插），必须重新加载拿到新 Id，
      // 否则下次保存会提交过期 Id，导致角色-菜单关联被清空
      await loadMenus()
      // 刷新全局菜单，让侧边栏和动态路由立即生效
      menuStore.load().catch(() => {})
    } else {
      ElMessage.error('保存失败：' + (res.msg || '未知错误'))
    }
  } catch { /* 错误已由 request.ts 弹出提示 */ }
}

// ── 选中与详情 ──
function select(path: number[]) {
  selectedPath.value = [...path]
}

const isSelected = (path: number[]) =>
  selectedPath.value.length === path.length && selectedPath.value.every((v, i) => v === path[i])

const isKeywordHit = (node: MenuNode) => {
  const kw = keyword.value.trim()
  return !!kw && matchNode(node, kw)
}

const selectedInfo = computed(() => (selectedPath.value.length ? getNodeByPath(selectedPath.value) : null))
const selectedNode = computed(() => selectedInfo.value?.node ?? null)

/** 详情面板：上级菜单标题链 */
const selectedParentTitle = computed(() => {
  if (!selectedPath.value.length) return ''
  const parentPath = selectedPath.value.slice(0, -1)
  if (parentPath.length === 0) return '顶级菜单'
  const chain: string[] = []
  let current: MenuNode[] = rawMenus.value
  for (let i = 0; i < parentPath.length; i++) {
    const n = current[parentPath[i]]
    if (!n) break
    chain.push(n.title || '')
    current = n.children || []
  }
  return chain.join(' / ') || '顶级菜单'
})

/** 详情面板：可见性文案 */
const selectedVisibility = computed(() => {
  const n = selectedNode.value
  if (!n) return ''
  const parts: string[] = []
  parts.push(n.float ? '悬浮菜单' : '非悬浮')
  parts.push(n.visible !== false ? '主界面显示' : '不在主界面显示')
  parts.push(n.editable !== false ? '允许编辑' : '已锁定编辑')
  return parts.join(' · ')
})

// ── 概览统计 ──
const stats = computed(() => {
  let total = 0, enabled = 0, external = 0, folders = 0
  const walk = (nodes: MenuNode[]) => {
    for (const n of nodes) {
      total++
      if (n.enabled !== false) enabled++
      if (n.external) external++
      if (n.children?.length) { folders++; walk(n.children) }
    }
  }
  walk(displayMenus.value)
  return { total, enabled, disabled: total - enabled, external, folders }
})

// 编辑抽屉
const drawerVisible = ref(false)
const drawerTitle = ref('')
const editForm = ref({ title: '', page: '', name: '', component: '', external: false, float: false, visible: true, editable: true, enabled: true })
/** 级联选择虚拟根键：选中即「顶级菜单（根级）」 */
const TOP_LEVEL_KEY = '__root__'
/** 上级菜单级联选中键；经 editParentData.map 映射回真实菜单路径 */
const editParentKey = ref<string>(TOP_LEVEL_KEY)
// 存储编辑目标：用路径数组表示层级，如 [0, 2] 表示 rawMenus[0].children[2]
const editPath = ref<number[]>([])
const editIsNew = ref(false)
const advancedOpen = ref(false)

/** 折叠区摘要：让用户不开折叠也能看到当前设置 */
const advSummary = computed(() => {
  const f = editForm.value
  return [
    f.float ? '悬浮' : '不悬浮',
    f.visible ? '主界面' : '非主界面',
    f.editable ? '允许编辑' : '锁定',
    f.enabled ? '启用' : '停用',
  ].join(' · ')
})

// 新增顶级菜单
async function addRootMenu() {
  drawerTitle.value = '新增顶级菜单'
  resetEditForm()
  editPath.value = []
  editIsNew.value = true
  await nextTick(() => { drawerVisible.value = true })
}

// 新增子菜单（path 指向父节点）
async function addChildMenu(parentPath: number[]) {
  drawerTitle.value = '新增子菜单'
  resetEditForm()
  editParentKey.value = parentPath.length ? parentPath.join('-') : TOP_LEVEL_KEY
  editPath.value = parentPath
  editIsNew.value = true
  await nextTick(() => { drawerVisible.value = true })
}

function resetEditForm() {
  editForm.value = { title: '', page: '', name: '', component: '', external: false, float: false, visible: true, editable: true, enabled: true }
  editParentKey.value = TOP_LEVEL_KEY
  advancedOpen.value = false
}

/** 编辑表单当前匹配的视图 Name（自动高亮下拉选项） */
const selectedViewName = computed(() => {
  const name = editForm.value.name
  if (!name) return ''
  return viewOptions.value.find((v) => v.name === name)?.name ?? ''
})

/** 选择视图后自动填充表单字段 */
function onViewSelect(viewName: string) {
  if (!viewName) return
  const v = viewOptions.value.find((x) => x.name === viewName)
  if (!v) return
  editForm.value.name = v.name
  editForm.value.title = editForm.value.title || v.title
  if (v.routePath) editForm.value.page = v.routePath
  if (v.component) editForm.value.component = v.component
}

// 编辑菜单
function editMenu(path: number[]) {
  const info = getNodeByPath(path)
  if (!info) return
  drawerTitle.value = '编辑菜单'
  editForm.value = {
    title: info.node.title || '',
    page: info.node.page || '',
    name: info.node.name || '',
    component: info.node.component || '',
    external: info.node.external === true,
    float: info.node.float || false,
    visible: info.node.visible !== false,
    editable: info.node.editable !== false,
    enabled: info.node.enabled !== false,
  }
  editParentKey.value = path.length > 1 ? path.slice(0, -1).join('-') : TOP_LEVEL_KEY
  editPath.value = [...path]
  editIsNew.value = false
  drawerVisible.value = true
}

// 删除菜单
async function deleteMenu(path: number[]) {
  const info = getNodeByPath(path)
  if (!info) return
  const childCount = countDescendants(info.node)
  const msg = childCount > 0
    ? `确定要删除菜单「${info.node.title}」及其 ${childCount} 个子菜单吗？此操作不可撤销。`
    : `确定要删除菜单「${info.node.title}」吗？此操作不可撤销。`
  try {
    await ElMessageBox.confirm(msg, '确认删除', {
      confirmButtonText: '删除',
      cancelButtonText: '取消',
      type: 'warning',
      confirmButtonClass: 'el-button--danger',
    })
  } catch { return }

  // 从父数组中移除
  const parentPath = path.slice(0, -1)
  const parentArr = parentPath.length === 0 ? rawMenus.value : getNodeByPath(parentPath)!.node.children!
  parentArr.splice(path[path.length - 1], 1)
  if (isSelected(path)) selectedPath.value = []
  // 删除后自动保存，只弹一条提示（保存失败时仍提示失败）。
  await handleSave('已删除')
}

function countDescendants(node: MenuNode): number {
  if (!node.children || node.children.length === 0) return 0
  let count = node.children.length
  for (const child of node.children) count += countDescendants(child)
  return count
}

async function confirmEdit() {
  const title = editForm.value.title.trim()
  if (!title) { ElMessage.warning('菜单标题不能为空'); return }

  // 编辑时将启用改为停用需确认提示，确认后同步禁用子孙菜单
  if (!editIsNew.value && editForm.value.enabled === false) {
    const info = getNodeByPath(editPath.value)
    if (info && info.node.enabled !== false) {
      try {
        await ElMessageBox.confirm(
          hasChildren(info.node)
            ? `确定禁用菜单「${info.node.title}」吗？其所有子孙菜单也将一并禁用。`
            : `确定禁用菜单「${info.node.title}」吗？`,
          '确认禁用',
          { confirmButtonText: '禁用', cancelButtonText: '取消', type: 'warning', confirmButtonClass: 'el-button--danger' },
        )
      } catch { return }
    }
  }

  const page = editForm.value.page.trim()
  const name = editForm.value.name.trim()
  const component = editForm.value.component.trim()
  const targetParentPath = resolveEditParentPath()

  // 编辑时禁止把自己或子孙选为父级
  if (!editIsNew.value && (arraysEqual(targetParentPath, editPath.value) || isDescendantOf(targetParentPath, editPath.value))) {
    ElMessage.warning('不能选择当前菜单或其子菜单作为上级菜单')
    return
  }

  if (editIsNew.value) {
    const targetArr = getChildrenByPath(targetParentPath)
    targetArr.push({
      title,
      page: page || undefined,
      name: name || undefined,
      component: component || undefined,
      external: editForm.value.external || undefined,
      children: [],
      float: editForm.value.float || undefined,
      visible: editForm.value.visible === false ? false : undefined,
      editable: editForm.value.editable === false ? false : undefined,
      enabled: editForm.value.enabled === false ? false : undefined,
    } as MenuNode)
  } else {
    const info = getNodeByPath(editPath.value)
    if (!info) { ElMessage.error('未找到要编辑的菜单'); return }
    const node = info.node
    node.title = title
    if (page) node.page = page
    else delete node.page
    if (name) node.name = name
    else delete node.name
    if (component) node.component = component
    else delete node.component
    if (editForm.value.external) node.external = true
    else delete node.external
    if (editForm.value.float) node.float = true
    else delete node.float
    if (editForm.value.visible === false) node.visible = false
    else delete node.visible
    if (editForm.value.editable === false) node.editable = false
    else delete node.editable
    if (editForm.value.enabled === false) {
      // 禁用同步到子孙菜单
      setEnabledRecursively(node, false)
    } else {
      delete node.enabled
    }
    // 同级全部同状态时同步父级
    syncParentEnabled(editPath.value, editForm.value.enabled)

    // 父级变更时移动节点（保留 children）
    const oldParentPath = editPath.value.slice(0, -1)
    if (!arraysEqual(oldParentPath, targetParentPath)) {
      const nodeToMove = { ...node }
      const oldArr = oldParentPath.length === 0 ? rawMenus.value : getNodeByPath(oldParentPath)!.node.children!
      oldArr.splice(editPath.value[editPath.value.length - 1], 1)
      const newArr = getChildrenByPath(targetParentPath)
      newArr.push(nodeToMove)
    }
  }
  drawerVisible.value = false
  await handleSave()
}

function hasChildren(node: MenuNode) {
  return !!(node.children && node.children.length > 0)
}

// ========== 父级级联选择（移动到弹窗 + 编辑抽屉共用）==========
/** 能否作为父级：非外链，且（有子级 或 无路由地址的分组节点）；末级菜单不作为上级列出 */
function canBeParent(node: MenuNode): boolean {
  if (node.external) return false
  return hasChildren(node) || !node.page
}

/**
 * 构建父级级联选项：第一层直接平铺「顶级菜单（根级）+ 各一级分组」，逐级展开带出下级；
 * 开启搜索后输入子级名称会连同完整父级路径一起列出。
 * excludePath 及其子孙被排除（防循环）；keepPath 即使不是分组也强制保留用于回显（如向末级菜单新增子级）。
 * 返回「键 → 真实菜单路径」映射供提交时解析。
 */
function buildParentCascade(excludePath: number[], keepPath: number[] = []) {
  const map = new Map<string, number[]>()
  map.set(TOP_LEVEL_KEY, [])
  interface CascaderOption { label: string; value: string; children?: CascaderOption[] }
  function build(nodes: MenuNode[], parentPath: number[]): CascaderOption[] {
    const out: CascaderOption[] = []
    for (let i = 0; i < nodes.length; i++) {
      const nodePath = [...parentPath, i]
      if (excludePath.length && (arraysEqual(nodePath, excludePath) || isDescendantOf(nodePath, excludePath))) continue
      if (!canBeParent(nodes[i]) && !(keepPath.length && arraysEqual(nodePath, keepPath))) continue
      const key = nodePath.join('-')
      map.set(key, nodePath)
      const label = nodes[i].title || '(未命名)'
      const children = build(nodes[i].children ?? [], nodePath)
      out.push(children.length ? { label, value: key, children } : { label, value: key })
    }
    return out
  }
  return {
    options: [{ label: '顶级菜单（根级）', value: TOP_LEVEL_KEY }, ...build(rawMenus.value, [])],
    map,
  }
}

// 移动到弹窗
const moveDialogVisible = ref(false)
const moveNodePath = ref<number[]>([])
/** 选中的级联键；经 moveTargetData.map 映射回真实菜单路径 */
const moveTargetKey = ref('')
/** 移动目标级联（排除被移动节点自身及其子孙，防止移进自己内部） */
const moveTargetData = computed(() => buildParentCascade(moveNodePath.value))

/** 编辑抽屉「上级菜单」级联（编辑时排除当前节点及其子孙；当前上级强制保留以便回显） */
const editParentData = computed(() => {
  const keep = editIsNew.value ? editPath.value : editPath.value.slice(0, -1)
  return buildParentCascade(editIsNew.value ? [] : editPath.value, keep)
})

/** 解析上级选择：键缺失时回退原始父级（新增回退所选上级），避免误移动 */
function resolveEditParentPath(): number[] {
  return editParentData.value.map.get(editParentKey.value)
    ?? (editIsNew.value ? [...editPath.value] : editPath.value.slice(0, -1))
}

function openMoveDialog(path: number[]) {
  moveNodePath.value = path
  moveTargetKey.value = ''
  moveDialogVisible.value = true
}

function isDescendantOf(child: number[], parent: number[]): boolean {
  if (child.length <= parent.length) return false
  for (let i = 0; i < parent.length; i++) {
    if (child[i] !== parent[i]) return false
  }
  return true
}

function arraysEqual(a: number[], b: number[]): boolean {
  if (a.length !== b.length) return false
  for (let i = 0; i < a.length; i++) if (a[i] !== b[i]) return false
  return true
}

async function confirmMove() {
  const info = getNodeByPath(moveNodePath.value)
  if (!info) return
  const targetPath = moveTargetData.value.map.get(moveTargetKey.value)
  if (!targetPath) {
    ElMessage.warning('请选择目标分组')
    return
  }

  // 先解析目标节点引用再删除：同父级时删除会引起索引位移，提前取引用避免错位
  const targetNode = targetPath.length === 0 ? null : getNodeByPath(targetPath)?.node
  if (targetPath.length > 0 && !targetNode) {
    ElMessage.error('目标分组已变化，请重新选择')
    return
  }

  const node = { ...info.node }
  // 从原位置删除
  const parentPath = moveNodePath.value.slice(0, -1)
  const parentArr = parentPath.length === 0 ? rawMenus.value : getNodeByPath(parentPath)!.node.children!
  parentArr.splice(moveNodePath.value[moveNodePath.value.length - 1], 1)

  // 插入到目标父级末尾
  if (targetNode) {
    if (!targetNode.children) targetNode.children = []
    targetNode.children.push(node)
  } else {
    rawMenus.value.push(node)
  }

  moveDialogVisible.value = false
  await handleSave('已移动')
}

function formatComponent(path?: string | null): string {
  if (!path) return ''
  return path.replace(/^\/src\//, '').replace(/^\.\.\//, 'src/')
}

function isEditable(node: MenuNode): boolean {
  return node.editable !== false
}

/** 详情面板图标配色（与列表行内图标同体系：分组蓝 / 外链琥珀 / 叶子灰） */
const detailIconClass = computed(() => {
  const n = selectedNode.value
  if (!n) return 'tint-leaf'
  if (hasChildren(n)) return 'tint-folder'
  if (n.external) return 'tint-external'
  return 'tint-leaf'
})

// 快速切换启用/停用状态：禁用需确认提示，且启用状态同步到所有子孙菜单
async function toggleEnabled(path: number[], val: boolean) {
  const info = getNodeByPath(path)
  if (!info) return
  const hasChild = hasChildren(info.node)
  if (!val) {
    try {
      await ElMessageBox.confirm(
        hasChild
          ? `确定禁用菜单「${info.node.title}」吗？其所有子孙菜单也将一并禁用。`
          : `确定禁用菜单「${info.node.title}」吗？`,
        '确认禁用',
        { confirmButtonText: '禁用', cancelButtonText: '取消', type: 'warning', confirmButtonClass: 'el-button--danger' },
      )
    } catch {
      return
    }
  }
  setEnabledRecursively(info.node, val)
  syncParentEnabled(path, val)
  await handleSave(val ? (hasChild ? '已启用（含子孙菜单）' : '已启用') : '已禁用（含子孙菜单）')
}

/** 递归设置启用状态：enabled=false 显式存储，true 删除字段（与后端序列化约定一致） */
function setEnabledRecursively(node: MenuNode, val: boolean) {
  setNodeEnabled(node, val)
  node.children?.forEach(c => setEnabledRecursively(c, val))
}

/** 设置单个节点启用状态（不递归子级） */
function setNodeEnabled(node: MenuNode, val: boolean) {
  if (val) delete node.enabled
  else node.enabled = false
}

/** 同级子菜单全部达到同一启用状态时，同步父级并逐级向上冒泡 */
function syncParentEnabled(path: number[], val: boolean) {
  if (path.length <= 1) return
  const parentInfo = getNodeByPath(path.slice(0, -1))
  if (!parentInfo) return
  const siblings = parentInfo.node.children ?? []
  const parentState = parentInfo.node.enabled !== false
  if (parentState !== val && siblings.every(s => (s.enabled !== false) === val)) {
    setNodeEnabled(parentInfo.node, val)
    syncParentEnabled(path.slice(0, -1), val)
  }
}

// ========== 拖拽排序 ==========
const dragState = reactive({
  dragPath: null as number[] | null,
  dragOverPath: null as number[] | null,
  dropPosition: null as 'before' | 'after' | null,
})

function clearDragState() {
  dragState.dragPath = null
  dragState.dragOverPath = null
  dragState.dropPosition = null
}

function onDragStart(path: number[], e: DragEvent) {
  // 从交互控件（按钮/开关/输入/下拉）上发起的拖拽不触发排序
  const target = e.target as HTMLElement
  if (target.closest('button') || target.closest('.el-switch') || target.closest('.el-input') || target.closest('.el-tooltip__popper')) {
    e.preventDefault()
    return
  }
  dragState.dragPath = path
  e.dataTransfer!.effectAllowed = 'move'
  e.dataTransfer!.setData('text/plain', path.join(','))
}

function onDragOver(path: number[], e: DragEvent) {
  if (!dragState.dragPath) return
  // 不允许拖到自身或子孙节点上
  if (arraysEqual(path, dragState.dragPath) || isDescendantOf(path, dragState.dragPath)) return
  e.preventDefault()
  e.stopPropagation()
  e.dataTransfer!.dropEffect = 'move'
  // 根据鼠标 Y 坐标判断插入到目标行之前还是之后
  const rect = (e.currentTarget as HTMLElement).getBoundingClientRect()
  const midY = rect.top + rect.height / 2
  const newPos = e.clientY < midY ? 'before' : 'after'
  // 只在目标或位置实际变化时才更新，避免高频重渲染导致抖动
  if (dragState.dragOverPath && arraysEqual(path, dragState.dragOverPath) && dragState.dropPosition === newPos) return
  dragState.dragOverPath = path
  dragState.dropPosition = newPos
}

function onDragLeave(_path: number[]) {
  // 不在 dragLeave 中清除状态——子元素间移动会频繁触发 dragLeave/dragOver 造成抖动
  // 状态在 dragOver 中自然更新，在 dragEnd/drop 中统一清除
}

async function onDrop(path: number[], e: DragEvent) {
  e.preventDefault()
  e.stopPropagation()

  const fromPath = dragState.dragPath
  if (!fromPath || !dragState.dropPosition) {
    clearDragState()
    return
  }

  // 不允许拖到自身
  if (arraysEqual(fromPath, path)) {
    clearDragState()
    return
  }

  // 不允许拖到子孙节点上（会造成循环引用）
  if (isDescendantOf(path, fromPath)) {
    clearDragState()
    return
  }

  const fromInfo = getNodeByPath(fromPath)
  if (!fromInfo) { clearDragState(); return }
  // 深拷贝节点，避免引用问题
  const nodeCopy: MenuNode = JSON.parse(JSON.stringify(fromInfo.node))

  // 在移除之前获取目标父级路径和索引
  const toParentPath = path.slice(0, -1)
  const toIdx = path[path.length - 1]

  const fromParentPath = fromPath.slice(0, -1)
  const sameParent = fromParentPath.length === toParentPath.length &&
                     fromParentPath.every((v, i) => v === toParentPath[i])

  // 获取目标数组引用（移除之前，确保引用有效）
  let toArr: MenuNode[]
  if (toParentPath.length === 0) {
    toArr = rawMenus.value
  } else {
    const toParentInfo = getNodeByPath(toParentPath)
    if (!toParentInfo) { clearDragState(); return }
    if (!toParentInfo.node.children) toParentInfo.node.children = []
    toArr = toParentInfo.node.children
  }

  // 从原位置移除
  const fromArr = fromParentPath.length === 0 ? rawMenus.value : getNodeByPath(fromParentPath)!.node.children!
  const fromIdx = fromPath[fromPath.length - 1]
  fromArr.splice(fromIdx, 1)

  // 计算插入位置
  let insertIdx: number
  if (sameParent) {
    // 同一父级下，移除后索引会偏移
    const adjustedToIdx = fromIdx < toIdx ? toIdx - 1 : toIdx
    insertIdx = dragState.dropPosition === 'before' ? adjustedToIdx : adjustedToIdx + 1
  } else {
    insertIdx = dragState.dropPosition === 'before' ? toIdx : toIdx + 1
  }
  insertIdx = Math.max(0, Math.min(insertIdx, toArr.length))

  toArr.splice(insertIdx, 0, nodeCopy)

  clearDragState()
  await handleSave('已移动')
}

function onDragEnd() {
  clearDragState()
}

// 向递归子组件注入操作集合
provide('menuActions', {
  hasChildren,
  isEditable,
  toggleEnabled,
  addChildMenu,
  editMenu,
  deleteMenu,
  openMoveDialog,
  select,
  isSelected,
  isKeywordHit,
  isRowExpanded,
  toggleExpand,
  dragState,
  onDragStart,
  onDragOver,
  onDragLeave,
  onDrop,
  onDragEnd,
})
</script>

<template>
  <div class="menu-manage">
    <!-- 页头工具条 -->
    <div class="page-header">
      <div class="header-title">
        <span class="header-icon">
          <el-icon :size="20"><Menu /></el-icon>
        </span>
        <div class="title-text">
          <h2>菜单管理</h2>
          <span class="title-sub">共 {{ stats.total }} 个菜单 · {{ stats.enabled }} 个启用</span>
        </div>
      </div>
      <div class="header-actions">
        <el-input
          v-model="keyword"
          placeholder="搜索标题 / 地址 / 组件"
          :prefix-icon="Search"
          clearable
          class="search-input"
        />
        <el-button @click="toggleExpandAll">
          {{ allExpanded ? '全部收起' : '全部展开' }}
        </el-button>
        <el-button :icon="Refresh" @click="loadMenus">刷新</el-button>
        <el-button type="primary" :icon="Plus" @click="addRootMenu">新增顶级菜单</el-button>
      </div>
    </div>

    <div class="body">
      <!-- 左栏：菜单树 -->
      <div class="tree-panel" v-loading="loading">
        <div v-if="displayMenus.length === 0 && !loading" class="empty-hint">
          <el-empty :description="keyword ? '没有匹配搜索的菜单' : '暂无菜单，点击「新增顶级菜单」添加'" />
        </div>
        <ul v-else class="menu-tree root">
          <MenuTreeRow
            v-for="(node, idx) in displayMenus"
            :key="idx"
            :node="node"
            :path="[idx]"
            :depth="0"
          />
        </ul>
      </div>

      <!-- 右栏：详情 / 概览 -->
      <div class="detail-panel">
        <template v-if="selectedNode">
          <div class="detail-card">
            <div class="detail-head">
              <span class="detail-icon" :class="detailIconClass">
                <el-icon :size="18">
                  <Folder v-if="hasChildren(selectedNode)" />
                  <Link v-else-if="selectedNode.external" />
                  <Document v-else />
                </el-icon>
              </span>
              <div class="detail-title-wrap">
                <div class="detail-title">{{ selectedNode.title }}</div>
                <div class="detail-tags">
                  <span v-if="selectedNode.external" class="tag tag-blue">
                    <el-icon :size="10"><Link /></el-icon>外链
                  </span>
                  <span v-if="selectedNode.enabled === false" class="tag tag-gray">已停用</span>
                  <span v-if="!hasChildren(selectedNode) && !selectedNode.page" class="tag tag-green">分组</span>
                </div>
              </div>
            </div>

            <div class="detail-rows">
              <div class="d-row">
                <span class="d-label">{{ selectedNode.external ? '链接地址' : '路由地址' }}</span>
                <code class="d-value">{{ selectedNode.page || '—' }}</code>
              </div>
              <div class="d-row">
                <span class="d-label">路由名称</span>
                <span class="d-value">{{ selectedNode.name || '—' }}</span>
              </div>
              <div class="d-row">
                <span class="d-label">组件</span>
                <span class="d-value mono">{{ formatComponent(selectedNode.component) || '—' }}</span>
              </div>
              <div class="d-row">
                <span class="d-label">上级菜单</span>
                <span class="d-value">{{ selectedParentTitle }}</span>
              </div>
              <div class="d-row">
                <span class="d-label">子菜单</span>
                <span class="d-value">{{ selectedNode.children?.length ?? 0 }} 个</span>
              </div>
              <div class="d-row">
                <span class="d-label">显示与权限</span>
                <span class="d-value">{{ selectedVisibility }}</span>
              </div>
            </div>

            <div class="detail-actions" v-if="isEditable(selectedNode)">
              <el-button v-if="$has('menu-manage:save')" type="primary" @click="editMenu(selectedPath)">编辑</el-button>
              <el-button @click="addChildMenu(selectedPath)">新增子级</el-button>
              <el-button @click="openMoveDialog(selectedPath)">移动</el-button>
              <el-button type="danger" plain @click="deleteMenu(selectedPath)">删除</el-button>
            </div>
            <div class="detail-locked" v-else>该菜单已锁定编辑（允许编辑 = 否）</div>
          </div>
        </template>

        <template v-else>
          <div class="detail-card overview">
            <div class="ov-title">菜单概览</div>
            <div class="ov-grid">
              <div class="ov-item">
                <div class="ov-num">{{ stats.total }}</div>
                <div class="ov-label">菜单总数</div>
              </div>
              <div class="ov-item">
                <div class="ov-num green">{{ stats.enabled }}</div>
                <div class="ov-label">已启用</div>
              </div>
              <div class="ov-item">
                <div class="ov-num gray">{{ stats.disabled }}</div>
                <div class="ov-label">已停用</div>
              </div>
              <div class="ov-item">
                <div class="ov-num blue">{{ stats.external }}</div>
                <div class="ov-label">外部链接</div>
              </div>
            </div>
            <div class="ov-divider" v-if="keyword">
              <span>当前搜索「{{ keyword }}」：命中 {{ stats.total }} 个菜单</span>
            </div>
            <div class="ov-tip">
              点击左侧任意菜单查看详情与操作；拖拽行首手柄可排序，箭头可展开收起。
            </div>
          </div>
        </template>
      </div>
    </div>

    <!-- 编辑抽屉 -->
    <el-drawer
      v-model="drawerVisible"
      :title="drawerTitle"
      size="480px"
      :close-on-click-modal="false"
      destroy-on-close
      @closed="resetEditForm"
    >
      <el-form :model="editForm" label-width="88px">
        <el-form-item label="上级菜单">
          <el-cascader
            v-model="editParentKey"
            :options="editParentData.options"
            :props="{ checkStrictly: true, emitPath: false }"
            filterable
            placeholder="逐级选择上级分组，或输入名称搜索"
            style="width: 100%"
          />
          <div class="form-hint">只列出分组类菜单；搜索子级会自动带出完整父级路径</div>
        </el-form-item>
        <el-form-item label="菜单标题">
          <el-input v-model="editForm.title" placeholder="请输入菜单标题" />
        </el-form-item>
        <el-form-item label="类型">
          <el-radio-group v-model="editForm.external">
            <el-radio-button :value="false">内部页面</el-radio-button>
            <el-radio-button :value="true">外部链接</el-radio-button>
          </el-radio-group>
        </el-form-item>
        <template v-if="editForm.external">
          <el-form-item label="链接地址">
            <el-input v-model="editForm.page" placeholder="如 https://www.example.com" />
          </el-form-item>
        </template>
        <template v-else>
          <el-form-item label="关联视图">
            <el-select
              :model-value="selectedViewName"
              filterable
              clearable
              placeholder="从视图注册表选择（可选，自动填充路由和组件）"
              style="width: 100%"
              @change="onViewSelect"
            >
              <el-option
                v-for="v in viewOptions"
                :key="v.id"
                :label="`${v.title}（${v.name}）`"
                :value="v.name"
              />
            </el-select>
            <div class="form-hint">选择视图后自动填充路由名称/地址/组件，权限点由视图管理维护</div>
          </el-form-item>
          <el-form-item label="路由名称">
            <el-input v-model="editForm.name" placeholder="如 attendance（可选，用于标识内部路由）" />
          </el-form-item>
          <el-form-item label="路由地址">
            <el-input v-model="editForm.page" placeholder="如 /attendance 或 /menu-manage（分组菜单留空）" />
            <div class="form-hint">内部路由以 <code>/</code> 开头；分组菜单可不填</div>
          </el-form-item>
          <el-form-item label="组件">
            <el-select
              v-model="editForm.component"
              filterable
              clearable
              placeholder="请选择对应的 Vue 组件"
              style="width: 100%"
            >
              <el-option
                v-for="opt in viewComponentOptions"
                :key="opt.value"
                :label="opt.label"
                :value="opt.value"
              />
            </el-select>
            <div class="form-hint">必须选择一个组件，内部路由才能正常渲染</div>
          </el-form-item>
        </template>

        <!-- 低频设置折叠区 -->
        <div class="adv-toggle" @click="advancedOpen = !advancedOpen">
          <el-icon class="adv-arrow" :class="{ open: advancedOpen }"><ArrowRight /></el-icon>
          <span class="adv-title">显示与权限</span>
          <span class="adv-summary">{{ advSummary }}</span>
        </div>
        <div v-show="advancedOpen" class="adv-body">
          <el-form-item label="悬浮菜单">
            <el-checkbox v-model="editForm.float">在悬浮按钮菜单中显示</el-checkbox>
          </el-form-item>
          <el-form-item label="主界面">
            <el-checkbox v-model="editForm.visible">在侧边栏和首页显示</el-checkbox>
          </el-form-item>
          <el-form-item label="允许编辑">
            <el-checkbox v-model="editForm.editable">允许在菜单管理中编辑</el-checkbox>
          </el-form-item>
          <el-form-item label="启用状态">
            <el-switch v-model="editForm.enabled" inline-prompt active-text="启用" inactive-text="停用" />
            <div class="form-hint">停用后不在侧栏/首页显示，也不可在权限管理中分配</div>
          </el-form-item>
        </div>
      </el-form>
      <template #footer>
        <el-button @click="drawerVisible = false">取消</el-button>
        <el-button v-if="$has('menu-manage:save')" type="primary" @click="confirmEdit">保存并生效</el-button>
      </template>
    </el-drawer>

    <!-- 移动弹窗 -->
    <CommonDialog
      v-model="moveDialogVisible"
      title="移动菜单"
      width="440px"
    >
      <el-form label-width="90px">
        <el-form-item label="移动到">
          <el-cascader
            v-model="moveTargetKey"
            :options="moveTargetData.options"
            :props="{ checkStrictly: true, emitPath: false }"
            filterable
            clearable
            placeholder="输入名称搜索，或逐级选择目标分组"
            style="width: 100%"
          />
        </el-form-item>
      </el-form>
      <div class="form-hint">只列出可作为父级的分组（末级菜单不显示）；选「顶级菜单（根级）」移回最外层。</div>
      <template #footer>
        <el-button @click="moveDialogVisible = false">取消</el-button>
        <el-button type="primary" :disabled="!moveTargetKey" @click="confirmMove">确定</el-button>
      </template>
    </CommonDialog>
  </div>
</template>

<style scoped>
.menu-manage {
  padding: 20px;
  height: 100%;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

/* 页头 */
.page-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  margin-bottom: 16px;
  flex-shrink: 0;
  flex-wrap: wrap;
}

.header-title {
  display: flex;
  align-items: center;
  gap: 12px;
}

.header-icon {
  width: 36px;
  height: 36px;
  border-radius: var(--radius-sm, 8px);
  background: var(--brand-gradient, linear-gradient(135deg, #3b82f6, #2563eb));
  color: #fff;
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
}

.title-text {
  display: flex;
  flex-direction: column;
  gap: 1px;
}

.title-text h2 {
  margin: 0;
  font-size: 18px;
  font-weight: 700;
  color: var(--text-main, #0f172a);
}

.title-sub {
  font-size: 12px;
  color: var(--text-sub, #64748b);
}

.header-actions {
  display: flex;
  gap: 8px;
  align-items: center;
  flex-wrap: wrap;
}

.search-input {
  width: 220px;
}

/* 双栏主体 */
.body {
  flex: 1;
  display: flex;
  gap: 16px;
  min-height: 0;
}

.tree-panel {
  flex: 1.5;
  min-width: 0;
  overflow: auto;
  background: var(--surface, #fff);
  border: 1px solid var(--border, #e2e8f0);
  border-radius: var(--radius, 12px);
  padding: 6px 4px;
  box-shadow: var(--shadow-sm, 0 1px 2px rgba(0, 0, 0, 0.04));
}

.detail-panel {
  flex: 1;
  min-width: 300px;
  max-width: 460px;
  overflow: auto;
}

.empty-hint {
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: 300px;
}

.menu-tree.root {
  list-style: none;
  padding: 0;
  margin: 0;
}

/* 详情卡片 */
.detail-card {
  background: var(--surface, #fff);
  border: 1px solid var(--border, #e2e8f0);
  border-radius: var(--radius, 12px);
  padding: 18px;
  box-shadow: var(--shadow-sm, 0 1px 2px rgba(0, 0, 0, 0.04));
}

.detail-head {
  display: flex;
  align-items: center;
  gap: 12px;
  padding-bottom: 14px;
  border-bottom: 1px solid var(--border, #e2e8f0);
}

.detail-icon {
  width: 40px;
  height: 40px;
  border-radius: 10px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
}

.detail-icon.tint-folder { background: #eff6ff; color: #2563eb; }
.detail-icon.tint-leaf { background: #f1f5f9; color: #475569; }
.detail-icon.tint-external { background: #fffbeb; color: #d97706; }

html.dark .detail-icon.tint-folder { background: rgba(59, 130, 246, 0.16); color: #7db9ff; }
html.dark .detail-icon.tint-leaf { background: rgba(100, 116, 139, 0.2); color: #94a3b8; }
html.dark .detail-icon.tint-external { background: rgba(245, 158, 11, 0.16); color: #fbbf24; }

.detail-title-wrap {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.detail-title {
  font-size: 16px;
  font-weight: 600;
  color: var(--text-main, #0f172a);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.detail-tags {
  display: flex;
  gap: 6px;
}

.tag {
  font-size: 11px;
  padding: 1px 7px;
  border-radius: 999px;
  display: inline-flex;
  align-items: center;
  gap: 3px;
}

.tag-blue { color: #2563eb; background: #eff6ff; }
.tag-gray { color: var(--text-sub, #64748b); background: var(--page-bg, #f8fafc); }
.tag-green { color: #16a34a; background: #f0fdf4; }

.detail-rows {
  padding: 12px 0;
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.d-row {
  display: flex;
  gap: 12px;
  padding: 6px 0;
  align-items: baseline;
}

.d-label {
  width: 72px;
  flex-shrink: 0;
  font-size: 12px;
  color: var(--text-sub, #64748b);
}

.d-value {
  flex: 1;
  min-width: 0;
  font-size: 13px;
  color: var(--text-main, #0f172a);
  word-break: break-all;
}

.d-value.mono,
.d-row code.d-value {
  font-family: 'SF Mono', 'Cascadia Code', Consolas, monospace;
  font-size: 12px;
  background: var(--page-bg, #f8fafc);
  padding: 2px 6px;
  border-radius: 4px;
}

.detail-actions {
  display: flex;
  gap: 8px;
  flex-wrap: wrap;
  padding-top: 14px;
  border-top: 1px solid var(--border, #e2e8f0);
}

.detail-locked {
  margin-top: 14px;
  padding-top: 14px;
  border-top: 1px solid var(--border, #e2e8f0);
  font-size: 12px;
  color: var(--text-sub, #64748b);
}

/* 概览 */
.ov-title {
  font-size: 15px;
  font-weight: 600;
  color: var(--text-main, #0f172a);
  margin-bottom: 14px;
}

.ov-grid {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 10px;
}

.ov-item {
  background: var(--page-bg, #f8fafc);
  border-radius: 10px;
  padding: 12px 6px;
  text-align: center;
}

.ov-num {
  font-size: 22px;
  font-weight: 700;
  color: var(--text-main, #0f172a);
  line-height: 1.2;
}

.ov-num.green { color: #16a34a; }
.ov-num.gray { color: var(--text-sub, #64748b); }
.ov-num.blue { color: #2563eb; }

.ov-label {
  font-size: 12px;
  color: var(--text-sub, #64748b);
  margin-top: 4px;
}

.ov-divider {
  margin-top: 14px;
  padding: 8px 12px;
  background: var(--brand-50, #eff6ff);
  border-radius: 8px;
  font-size: 12px;
  color: var(--brand, #3b82f6);
}

.ov-tip {
  margin-top: 14px;
  font-size: 12px;
  color: var(--text-sub, #64748b);
  line-height: 1.7;
}

/* 抽屉内折叠区 */
.adv-toggle {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 10px 0;
  margin-top: 4px;
  border-top: 1px dashed var(--border, #e2e8f0);
  cursor: pointer;
  user-select: none;
}

.adv-arrow {
  color: var(--text-sub, #64748b);
  transition: transform 0.15s;
}

.adv-arrow.open {
  transform: rotate(90deg);
}

.adv-title {
  font-size: 13px;
  font-weight: 600;
  color: var(--text-main, #0f172a);
}

.adv-summary {
  flex: 1;
  min-width: 0;
  font-size: 12px;
  color: var(--text-sub, #64748b);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  text-align: right;
}

.adv-body {
  padding-top: 4px;
}

/* 表单提示 */
.form-hint {
  font-size: 12px;
  color: var(--text-sub, #64748b);
  margin-top: 4px;
  line-height: 1.5;
}

.form-hint code {
  background: var(--page-bg, #f8fafc);
  padding: 1px 4px;
  border-radius: 3px;
  font-size: 11px;
}

/* 窄屏降级：双栏变单栏 */
@media (max-width: 1100px) {
  .body {
    flex-direction: column;
    overflow: auto;
  }

  .tree-panel {
    flex: none;
    max-height: 55%;
  }

  .detail-panel {
    flex: none;
    max-width: none;
    min-width: 0;
  }
}
</style>

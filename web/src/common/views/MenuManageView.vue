<script setup lang="ts">
import { onMounted, ref, computed, nextTick, reactive, provide } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Menu, Plus, Refresh } from '@element-plus/icons-vue'
import { getMenus, saveMenus } from '@/common/api/menu'
import { getViews, type ViewDto } from '@/common/api/view'
import { viewComponentOptions } from '@/common/viewComponents'
import { useMenuStore } from '@/common/stores/menu'
import type { MenuNode } from '@/common/types'
import CommonTooltip from '@/common/components/CommonTooltip.vue'
import CommonDialog from '@/common/components/CommonDialog.vue'
import MenuTreeRow from '@/common/components/MenuTreeRow.vue'

// 视图列表（供编辑弹窗下拉选择）
const viewOptions = ref<ViewDto[]>([])

// 原始菜单树（含“系统管理”节点，保存时完整提交）
const rawMenus = ref<MenuNode[]>([])
const loading = ref(false)
const dirty = ref(false)

// 编辑弹窗
const dialogVisible = ref(false)
const dialogTitle = ref('')
const editForm = ref({ title: '', page: '', name: '', component: '', external: false, float: false, visible: true, editable: true, enabled: true })
// 存储编辑目标：用路径数组表示层级，如 [0, 2] 表示 rawMenus[0].children[2]
const editPath = ref<number[]>([])
const editIsNew = ref(false)

// 过滤掉"系统管理→菜单管理"节点，只用于展示
function filterMenus(nodes: MenuNode[]): MenuNode[] {
  return nodes
    .filter(n => !(n.title === '系统管理' && n.children?.length === 1 && n.children[0].title === '菜单管理'))
    .map(n => ({
      ...n,
      children: n.children?.length ? filterMenus(n.children) : [],
    }))
}

const displayMenus = computed(() => filterMenus(rawMenus.value))

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
    rawMenus.value = normalizeMenus(await getMenus())
    dirty.value = false
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
      dirty.value = false
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

// 新增顶级菜单
async function addRootMenu() {
  dialogTitle.value = '新增顶级菜单'
  resetEditForm()
  editPath.value = []
  editIsNew.value = true
  await nextTick(() => { dialogVisible.value = true })
}

// 新增子菜单（path 指向父节点）
async function addChildMenu(parentPath: number[]) {
  dialogTitle.value = '新增子菜单'
  resetEditForm()
  editPath.value = parentPath
  editIsNew.value = true
  await nextTick(() => { dialogVisible.value = true })
}

function resetEditForm() {
  editForm.value = { title: '', page: '', name: '', component: '', external: false, float: false, visible: true, editable: true, enabled: true }
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
  dialogTitle.value = '编辑菜单'
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
  editPath.value = [...path]
  editIsNew.value = false
  dialogVisible.value = true
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
  dirty.value = true
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

  // 编辑时将启用改为禁用需确认提示，确认后同步禁用子孙菜单
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

  const targetArr = getChildrenByPath(editPath.value)
  if (editIsNew.value) {
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
  }
  dialogVisible.value = false
  dirty.value = true
  await handleSave()
}

function hasChildren(node: MenuNode) {
  return !!(node.children && node.children.length > 0)
}

// ========== 移动到弹窗（跨父级移动）==========
const moveDialogVisible = ref(false)
const moveNodePath = ref<number[]>([])
const moveTargetPath = ref<number[]>([])

function openMoveDialog(path: number[]) {
  moveNodePath.value = path
  moveTargetPath.value = []
  moveDialogVisible.value = true
}

// 获取所有可移动目标：任意其它菜单均可作为目标父级（排除自身、子孙节点、外链菜单）
function getMoveTargets(): { path: number[]; label: string }[] {
  const targets: { path: number[]; label: string }[] = [{ path: [], label: '顶级菜单' }]
  function collect(nodes: MenuNode[], parentPath: number[], prefix: string) {
    for (let i = 0; i < nodes.length; i++) {
      const nodePath = [...parentPath, i]
      // 排除自身及其子孙节点
      if (isDescendantOf(nodePath, moveNodePath.value) || arraysEqual(nodePath, moveNodePath.value)) continue
      // 外链菜单不能作为父级（无法容纳子菜单）
      if (nodes[i].external) continue
      targets.push({ path: nodePath, label: prefix + nodes[i].title })
      if (nodes[i].children?.length) collect(nodes[i].children!, nodePath, prefix + nodes[i].title + ' / ')
    }
  }
  collect(rawMenus.value, [], '')
  return targets
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

  const node = { ...info.node }
  // 从原位置删除
  const parentPath = moveNodePath.value.slice(0, -1)
  const parentArr = parentPath.length === 0 ? rawMenus.value : getNodeByPath(parentPath)!.node.children!
  parentArr.splice(moveNodePath.value[moveNodePath.value.length - 1], 1)

  // 插入到新位置
  const targetArr = moveTargetPath.value.length === 0 ? rawMenus.value : getNodeByPath(moveTargetPath.value)!.node.children!
  targetArr.push(node)

  moveDialogVisible.value = false
  dirty.value = true
  await handleSave('已移动')
}

function formatComponent(path?: string | null): string {
  if (!path) return ''
  return path.replace(/^\/src\//, '').replace(/^\.\.\//, 'src/')
}

function isEditable(node: MenuNode): boolean {
  return node.editable !== false
}

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
  dirty.value = true
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
  // 从交互控件（按钮/开关/输入）上发起的拖拽不触发排序
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

  dirty.value = true
  clearDragState()
  await handleSave('已移动')
}

function onDragEnd() {
  clearDragState()
}

// 向递归子组件注入操作集合
provide('menuActions', {
  hasChildren,
  formatComponent,
  isEditable,
  toggleEnabled,
  addChildMenu,
  editMenu,
  deleteMenu,
  openMoveDialog,
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
    <!-- 页头 -->
    <div class="page-header">
      <div class="header-title">
        <span class="header-icon">
          <el-icon :size="20"><Menu /></el-icon>
        </span>
        <h2>菜单管理</h2>
      </div>
      <div class="header-actions">
        <el-button :icon="Refresh" @click="loadMenus">刷新</el-button>
        <el-button type="primary" :icon="Plus" @click="addRootMenu">新增顶级菜单</el-button>
      </div>
    </div>

    <!-- 树容器 -->
    <div class="tree-card" v-loading="loading">
      <div v-if="displayMenus.length === 0 && !loading" class="empty-hint">
        <el-empty description="暂无菜单，点击「新增顶级菜单」添加" />
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

    <!-- 编辑弹窗 -->
    <CommonDialog
      v-model="dialogVisible"
      :title="dialogTitle"
      width="460px"
      :close-on-click-modal="false"
      :close-on-press-escape="false"
      class="stretch-dialog"
      @closed="resetEditForm"
    >
      <el-form :model="editForm" label-width="90px">
        <el-form-item label="菜单标题">
          <el-input v-model="editForm.title" placeholder="请输入菜单标题" />
        </el-form-item>
        <el-form-item label="外部链接">
          <el-checkbox v-model="editForm.external">是外部链接</el-checkbox>
          <div class="form-hint">勾选后 page 会作为完整 URL 在独立窗口外链容器中打开</div>
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
        <el-form-item label="悬浮菜单">
          <el-checkbox v-model="editForm.float">在悬浮按钮菜单中显示</el-checkbox>
          <div class="form-hint">勾选后此菜单会出现在屏幕右上角的悬浮按钮菜单中</div>
        </el-form-item>
        <el-form-item label="主界面">
          <el-checkbox v-model="editForm.visible">在侧边栏和首页显示</el-checkbox>
          <div class="form-hint">取消勾选后此菜单仅在悬浮按钮菜单中可见</div>
        </el-form-item>
        <el-form-item label="允许编辑">
          <el-checkbox v-model="editForm.editable">允许在菜单管理中编辑</el-checkbox>
          <div class="form-hint">取消勾选后此菜单在菜单管理列表中将隐藏编辑和删除按钮</div>
        </el-form-item>
        <el-form-item label="启用状态">
          <el-switch v-model="editForm.enabled" inline-prompt active-text="启用" inactive-text="停用" />
          <div class="form-hint">停用后此菜单不在侧栏/首页显示，也不可在权限管理中分配</div>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button v-if="$has('menu-manage:save')" type="primary" @click="confirmEdit">确定</el-button>
      </template>
    </CommonDialog>

    <!-- 移动弹窗 -->
    <CommonDialog
      v-model="moveDialogVisible"
      title="移动菜单"
      width="400px"
    >
      <el-form label-width="90px">
        <el-form-item label="移动到">
          <el-select v-model="moveTargetPath" placeholder="选择目标父菜单" style="width: 100%">
            <el-option
              v-for="target in getMoveTargets()"
              :key="target.path.join('-')"
              :label="target.label"
              :value="target.path"
            />
          </el-select>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="moveDialogVisible = false">取消</el-button>
        <el-button type="primary" @click="confirmMove">确定</el-button>
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
  margin-bottom: 16px;
  flex-shrink: 0;
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

.page-header h2 {
  margin: 0;
  font-size: 20px;
  font-weight: 700;
  color: var(--text-main, #0f172a);
}

.header-actions {
  display: flex;
  gap: 8px;
}

/* 树容器 */
.tree-card {
  flex: 1;
  overflow: auto;
  background: var(--surface, #fff);
  border: 1px solid var(--border, #e2e8f0);
  border-radius: var(--radius, 12px);
  padding: 8px 8px;
  box-shadow: var(--shadow-sm, 0 1px 2px rgba(0, 0, 0, 0.04));
}

.empty-hint {
  flex: 1;
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
</style>

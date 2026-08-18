<script setup lang="ts">
import { onActivated, onMounted, ref, computed } from 'vue'
import { ElMessage } from 'element-plus'
import { Search } from '@element-plus/icons-vue'
import {
  listRolesWithUsers,
  listPermissionMenusFlat,
  saveRolePermissions,
  getUserPermissions,
  saveUserPermissions,
  type RoleWithUsersDto,
  type MenuPermFlatDto,
  type ViewPermNodeDto,
} from '@/common/api/permission'

// ========== 菜单树结构（纯菜单，不含动作点） ==========
interface MenuTreeNode {
  id: number
  title: string
  children?: MenuTreeNode[]
}
const menuTree = ref<MenuTreeNode[]>([])
/** 菜单 ID → 动作点列表 */
const actionPointsMap = ref<Map<number, ViewPermNodeDto[]>>(new Map())
/** 当前选中的菜单 ID（中间面板高亮行） */
const selectedMenuId = ref<number | null>(null)

/** 扁平列表转树（不含动作点） */
function buildMenuTree(flat: MenuPermFlatDto[]): MenuTreeNode[] {
  const map = new Map<number, MenuTreeNode>()
  flat.forEach((m) => map.set(m.id, { id: m.id, title: m.title, children: [] }))
  const roots: MenuTreeNode[] = []
  flat.forEach((m) => {
    const node = map.get(m.id)!
    if (m.parentId != null && map.has(m.parentId)) map.get(m.parentId)!.children!.push(node)
    else roots.push(node)
  })
  const prune = (nodes: MenuTreeNode[]) => {
    nodes.forEach((n) => {
      if (n.children && n.children.length === 0) delete n.children
      else if (n.children) prune(n.children)
    })
  }
  prune(roots)
  return roots
}

/** 收集所有节点 ID（含分组 + 叶子） */
function getAllNodeIds(nodes: MenuTreeNode[]): number[] {
  const ids: number[] = []
  const walk = (list: MenuTreeNode[]) => {
    list.forEach((n) => { ids.push(n.id); if (n.children) walk(n.children) })
  }
  walk(nodes)
  return ids
}

/** 收集所有叶子 ID */
function getAllLeafIds(nodes: MenuTreeNode[]): number[] {
  const ids: number[] = []
  const walk = (list: MenuTreeNode[]) => {
    list.forEach((n) => {
      if (!n.children || n.children.length === 0) ids.push(n.id)
      else walk(n.children)
    })
  }
  walk(nodes)
  return ids
}

/** 节点 ID → 父节点 ID 映射 */
function buildParentMap(nodes: MenuTreeNode[]): Map<number, number> {
  const map = new Map<number, number>()
  const walk = (list: MenuTreeNode[], parentId?: number) => {
    list.forEach((n) => {
      if (parentId !== undefined) map.set(n.id, parentId)
      if (n.children) walk(n.children, n.id)
    })
  }
  walk(nodes)
  return map
}

/** 收集指定节点下所有叶子 ID */
function getLeafIdsOf(node: MenuTreeNode): number[] {
  if (!node.children || node.children.length === 0) return [node.id]
  const ids: number[] = []
  const walk = (n: MenuTreeNode) => {
    if (!n.children || n.children.length === 0) ids.push(n.id)
    else n.children.forEach(walk)
  }
  walk(node)
  return ids
}

// ========== 选中目标 ==========
type SelectionTarget =
  | { type: 'role'; roleId: number }
  | { type: 'user'; userId: string; roleId: number }

const loading = ref(false)
const rolesWithUsers = ref<RoleWithUsersDto[]>([])
const selectedTarget = ref<SelectionTarget | null>(null)
const expandedRoles = ref<Set<number>>(new Set())
const saving = ref(false)

/** 继承的 ID（用户模式下来自角色） */
const inheritedMenuIds = ref<Set<number>>(new Set())
/** 直接选中的 ID（正数=菜单，负数=动作点） */
const directMenuIds = ref<Set<number>>(new Set())

const parentMap = computed(() => buildParentMap(menuTree.value))
const allNodeIds = computed(() => getAllNodeIds(menuTree.value))
const allLeafIds = computed(() => getAllLeafIds(menuTree.value))

/** 有效 = 继承 ∪ 直接 */
const effectiveMenuIds = computed(() => {
  if (selectedTarget.value?.type === 'user')
    return new Set([...inheritedMenuIds.value, ...directMenuIds.value])
  return new Set(directMenuIds.value)
})

const selectedRole = computed(() => {
  const t = selectedTarget.value
  return t ? rolesWithUsers.value.find((r) => r.id === t.roleId) ?? null : null
})

const selectedUser = computed(() => {
  const t = selectedTarget.value
  if (!t || t.type !== 'user') return null
  const role = rolesWithUsers.value.find((r) => r.id === t.roleId)
  return role?.users.find((u) => u.id === t.userId) ?? null
})

const inheritedRoleNames = computed(() => {
  const t = selectedTarget.value
  if (!t || t.type !== 'user') return []
  const uid = t.userId
  return rolesWithUsers.value.filter((r) => r.users.some((u) => u.id === uid)).map((r) => r.name)
})

const checkedCount = computed(() => allLeafIds.value.filter((id) => effectiveMenuIds.value.has(id)).length)
const totalLeafCount = computed(() => allLeafIds.value.length)

// ========== 当前选中菜单的动作点 ==========
const selectedMenuActions = computed<ViewPermNodeDto[]>(() => {
  if (selectedMenuId.value === null) return []
  return actionPointsMap.value.get(selectedMenuId.value) ?? []
})

const selectedMenuTitle = computed(() => {
  if (selectedMenuId.value === null) return ''
  const find = (nodes: MenuTreeNode[]): string => {
    for (const n of nodes) {
      if (n.id === selectedMenuId.value) return n.title
      if (n.children) { const f = find(n.children); if (f) return f }
    }
    return ''
  }
  return find(menuTree.value)
})

const actionCheckedCount = computed(() =>
  selectedMenuActions.value.filter((a) => effectiveMenuIds.value.has(-a.id)).length
)

// ========== 搜索 ==========
const searchQuery = ref('')
const expandedNodes = ref<Set<number>>(new Set())

function checkHasMatch(node: MenuTreeNode, q: string): boolean {
  if (node.title.toLowerCase().includes(q)) return true
  if (node.children) return node.children.some((c) => checkHasMatch(c, q))
  return false
}

function escapeHtml(text: string): string {
  return text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
}

function highlightText(text: string): string {
  const q = searchQuery.value.trim()
  if (!q) return escapeHtml(text)
  const eText = escapeHtml(text)
  const eQuery = escapeHtml(q).replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
  return eText.replace(new RegExp(`(${eQuery})`, 'gi'), '<mark class="search-highlight">$1</mark>')
}

// ========== 菜单勾选状态 ==========
function menuCheckState(node: MenuTreeNode): 'all' | 'half' | 'none' {
  // 节点自身被勾选 → 全选
  if (effectiveMenuIds.value.has(node.id)) return 'all'
  // 叶子：看动作点
  if (!node.children || node.children.length === 0) {
    const actions = actionPointsMap.value.get(node.id) ?? []
    return actions.some((a) => effectiveMenuIds.value.has(-a.id)) ? 'half' : 'none'
  }
  // 分组：看后代叶子 + 动作点
  const leaves = getLeafIdsOf(node)
  for (const leafId of leaves) {
    if (effectiveMenuIds.value.has(leafId)) return 'half'
    const actions = actionPointsMap.value.get(leafId) ?? []
    if (actions.some((a) => effectiveMenuIds.value.has(-a.id))) return 'half'
  }
  return 'none'
}

// ========== 显示列表（扁平化树） ==========
interface DisplayItem {
  node: MenuTreeNode
  depth: number
  isLeaf: boolean
  isExpanded: boolean
  checkState: 'all' | 'half' | 'none'
}

const displayList = computed<DisplayItem[]>(() => {
  const result: DisplayItem[] = []
  const q = searchQuery.value.trim().toLowerCase()
  function walk(nodes: MenuTreeNode[], depth: number) {
    nodes.forEach((node) => {
      const isLeaf = !node.children || node.children.length === 0
      const state = menuCheckState(node)
      if (!q) {
        const expanded = expandedNodes.value.has(node.id)
        result.push({ node, depth, isLeaf, isExpanded: expanded, checkState: state })
        if (!isLeaf && expanded) walk(node.children!, depth + 1)
      } else {
        const matched = node.title.toLowerCase().includes(q)
        const has = matched || (node.children ? checkHasMatch(node, q) : false)
        if (matched || has) {
          result.push({ node, depth, isLeaf, isExpanded: true, checkState: state })
          if (!isLeaf && has) walk(node.children!, depth + 1)
        }
      }
    })
  }
  walk(menuTree.value, 0)
  return result
})

// ========== 计数 ==========
function checkedCountOf(node: MenuTreeNode): number {
  return getLeafIdsOf(node).filter((id) => effectiveMenuIds.value.has(id)).length
}
function leafCountOf(node: MenuTreeNode): number {
  return getLeafIdsOf(node).length
}

// ========== 继承判断 ==========
function isInherited(id: number): boolean {
  return selectedTarget.value?.type === 'user' && inheritedMenuIds.value.has(id)
}

// ========== 左侧角色树 ==========
function toggleRoleExpand(id: number) {
  const s = new Set(expandedRoles.value)
  if (s.has(id)) s.delete(id); else s.add(id)
  expandedRoles.value = s
}

/** 收集所有动作点负数 ID */
function collectAllActionIds(): number[] {
  const ids: number[] = []
  actionPointsMap.value.forEach((actions) => actions.forEach((a) => ids.push(-a.id)))
  return ids
}

function selectRole(roleId: number) {
  const role = rolesWithUsers.value.find((r) => r.id === roleId)
  if (!role) return
  selectedTarget.value = { type: 'role', roleId }
  const nodeSet = new Set(allNodeIds.value)
  if (role.isAdmin) {
    // admin 全通：所有菜单节点 + 所有动作点
    const all = new Set<number>(allNodeIds.value)
    collectAllActionIds().forEach((id) => all.add(id))
    directMenuIds.value = all
  } else {
    const ids = new Set<number>()
    ;(role.menuIds ?? []).forEach((id) => { if (nodeSet.has(id)) ids.add(id) })
    ;(role.viewPermIds ?? []).forEach((vpId) => ids.add(-vpId))
    directMenuIds.value = ids
  }
  inheritedMenuIds.value = new Set()
  expandedNodes.value = new Set(menuTree.value.map((n) => n.id))
  selectFirstActionMenu()
}

async function selectUser(userId: string, roleId: number) {
  selectedTarget.value = { type: 'user', userId, roleId }
  const nodeSet = new Set(allNodeIds.value)

  // 继承 = 用户所有角色的并集
  const inheritedSet = new Set<number>()
  const userRoles = rolesWithUsers.value.filter((r) => r.users.some((u) => u.id === userId))
  userRoles.forEach((role) => {
    if (role.isAdmin) {
      const walk = (nodes: MenuTreeNode[]) => {
        nodes.forEach((n) => { inheritedSet.add(n.id); if (n.children) walk(n.children) })
      }
      walk(menuTree.value)
      collectAllActionIds().forEach((id) => inheritedSet.add(id))
    } else {
      ;(role.menuIds ?? []).forEach((id) => inheritedSet.add(id))
      ;(role.viewPermIds ?? []).forEach((vpId) => inheritedSet.add(-vpId))
    }
  })
  inheritedMenuIds.value = inheritedSet

  // 用户直接授权
  try {
    const detail = await getUserPermissions(userId)
    const ids = new Set<number>()
    detail.menuIds.filter((id) => nodeSet.has(id)).forEach((id) => ids.add(id))
    ;(detail.viewPermIds ?? []).forEach((vpId) => ids.add(-vpId))
    directMenuIds.value = ids
  } catch {
    directMenuIds.value = new Set()
  }

  expandedNodes.value = new Set(menuTree.value.map((n) => n.id))
  selectFirstActionMenu()
}

/** 选中第一个有动作点的叶子菜单 */
function selectFirstActionMenu() {
  for (const leafId of allLeafIds.value) {
    if (actionPointsMap.value.has(leafId)) { selectedMenuId.value = leafId; return }
  }
  selectedMenuId.value = allLeafIds.value.length > 0 ? allLeafIds.value[0] : null
}

// ========== 中间面板：菜单操作 ==========
function toggleExpand(id: number) {
  const s = new Set(expandedNodes.value)
  if (s.has(id)) s.delete(id); else s.add(id)
  expandedNodes.value = s
}

function selectMenu(id: number) {
  selectedMenuId.value = id
}

/**
 * 勾选菜单节点（叶子或分组）：
 * - 叶子菜单：切自身 ID + 关联动作点
 * - 分组菜单：切自身 ID + 所有后代叶子 ID + 所有后代动作点
 * 勾选动作点不联动菜单（toggleActionCheck 独立处理）
 */
function toggleMenuCheck(node: MenuTreeNode) {
  const ownInSet = effectiveMenuIds.value.has(node.id)
  const s = new Set(directMenuIds.value)

  // 收集所有要切换的 ID
  const toggleIds = new Set<number>()
  if (!isInherited(node.id)) toggleIds.add(node.id)
  // 后代叶子
  getLeafIdsOf(node).filter((id) => !isInherited(id)).forEach((id) => toggleIds.add(id))
  // 动作点（自身 + 后代叶子的）
  const menuIds = [node.id, ...getLeafIdsOf(node)]
  menuIds.forEach((mid) => {
    (actionPointsMap.value.get(mid) ?? []).forEach((a) => {
      if (!isInherited(-a.id)) toggleIds.add(-a.id)
    })
  })

  if (ownInSet) toggleIds.forEach((id) => s.delete(id))
  else toggleIds.forEach((id) => s.add(id))
  directMenuIds.value = s
}

// ========== 右侧面板：动作点操作 ==========
function toggleActionCheck(actionId: number) {
  const negId = -actionId
  if (isInherited(negId)) return
  const s = new Set(directMenuIds.value)
  if (s.has(negId)) s.delete(negId); else s.add(negId)
  directMenuIds.value = s
}

// ========== 数据加载 ==========
async function loadData() {
  loading.value = true
  try {
    const [roleList, menus] = await Promise.all([listRolesWithUsers(), listPermissionMenusFlat()])
    rolesWithUsers.value = roleList
    menuTree.value = buildMenuTree(menus)
    // 提取动作点映射
    const apMap = new Map<number, ViewPermNodeDto[]>()
    menus.forEach((m) => { if (m.viewPerms && m.viewPerms.length > 0) apMap.set(m.id, m.viewPerms) })
    actionPointsMap.value = apMap

    if (roleList.length > 0 && selectedTarget.value === null) {
      selectRole(roleList[0].id)
      const s = new Set(expandedRoles.value); s.add(roleList[0].id); expandedRoles.value = s
    }
  } catch { /* */ } finally {
    loading.value = false
  }
}

// ========== 保存 ==========
async function savePermissions() {
  const target = selectedTarget.value
  if (!target) return

  const menuIdSet = new Set<number>()
  const viewPermIdSet = new Set<number>()
  directMenuIds.value.forEach((id) => {
    if (id < 0) viewPermIdSet.add(-id)
    else menuIdSet.add(id)
  })

  // 补齐父级分组 ID
  const menuIds = new Set(menuIdSet)
  menuIdSet.forEach((leafId) => {
    let pid = parentMap.value.get(leafId)
    while (pid !== undefined) {
      if (!inheritedMenuIds.value.has(pid)) menuIds.add(pid)
      pid = parentMap.value.get(pid)
    }
  })

  saving.value = true
  try {
    if (target.type === 'role') {
      await saveRolePermissions({ roleId: target.roleId, menuIds: [...menuIds], viewPermIds: [...viewPermIdSet] })
      const r = rolesWithUsers.value.find((x) => x.id === target.roleId)
      if (r) { r.menuIds = [...menuIds]; r.viewPermIds = [...viewPermIdSet] }
    } else {
      await saveUserPermissions({ userId: target.userId, menuIds: [...menuIds], viewPermIds: [...viewPermIdSet] })
    }
    ElMessage.success('权限已保存')
  } catch { /* */ } finally {
    saving.value = false
  }
}

onMounted(loadData)
onActivated(loadData)
</script>

<template>
  <div class="permission-page">
    <!-- ===== 左侧：角色面板 ===== -->
    <div class="role-panel" v-loading="loading">
      <div class="panel-header">
        <div class="panel-title-row">
          <span class="panel-title">角色</span>
          <span class="panel-count">{{ rolesWithUsers.length }}</span>
        </div>
      </div>
      <div class="role-tree">
        <template v-for="role in rolesWithUsers" :key="role.id">
          <div
            class="role-row"
            :class="{ active: selectedTarget?.type === 'role' && selectedTarget.roleId === role.id }"
            @click="selectRole(role.id)"
          >
            <span
              class="expand-arrow"
              :class="{ expanded: expandedRoles.has(role.id), 'no-children': role.users.length === 0 }"
              @click.stop="toggleRoleExpand(role.id)"
            >
              <svg width="12" height="12" viewBox="0 0 12 12">
                <path d="M4 2l4 4-4 4" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"/>
              </svg>
            </span>
            <div class="role-avatar">{{ role.name.slice(0, 1) }}</div>
            <div class="role-info">
              <span class="role-name">{{ role.name }}</span>
              <span class="role-code">{{ role.code }}</span>
            </div>
            <span v-if="role.code === 'admin'" class="shield shield-admin">超管</span>
            <span v-else-if="role.code === 'user'" class="shield shield-user">用户</span>
            <span class="role-user-count">{{ role.users.length }}</span>
          </div>
          <div v-if="expandedRoles.has(role.id)" class="user-list">
            <div
              v-for="user in role.users"
              :key="user.id"
              class="user-row"
              :class="{ active: selectedTarget?.type === 'user' && selectedTarget.userId === user.id }"
              @click="selectUser(user.id, role.id)"
            >
              <div class="user-avatar">{{ (user.displayName || user.account).slice(0, 1) }}</div>
              <div class="user-info">
                <span class="user-name">{{ user.displayName || user.account }}</span>
                <span class="user-account">{{ user.account }}</span>
              </div>
            </div>
            <div v-if="role.users.length === 0" class="user-empty">无用户</div>
          </div>
        </template>
        <el-empty v-if="rolesWithUsers.length === 0 && !loading" description="暂无角色" :image-size="60" />
      </div>
    </div>

    <!-- ===== 中间：导航菜单面板 ===== -->
    <div class="menu-panel">
      <template v-if="selectedTarget">
        <div class="perm-header">
          <div class="perm-title">
            <template v-if="selectedTarget.type === 'role'">
              <span class="perm-role-name">{{ selectedRole?.name }}</span>
              <span class="perm-role-code">{{ selectedRole?.code }}</span>
            </template>
            <template v-else>
              <span class="perm-user-name">{{ selectedUser?.displayName || selectedUser?.account }}</span>
              <span v-if="inheritedRoleNames.length" class="perm-inherit">
                继承 {{ inheritedRoleNames.join('、') }}
              </span>
            </template>
          </div>
          <el-button type="primary" :loading="saving" @click="savePermissions">保存权限</el-button>
        </div>

        <div class="perm-search-bar">
          <el-input v-model="searchQuery" placeholder="搜索菜单..." :prefix-icon="Search" clearable size="default" />
          <span class="perm-count">
            <span class="perm-count-num">{{ checkedCount }}</span>
            <span class="perm-count-sep">/</span>
            <span class="perm-count-total">{{ totalLeafCount }}</span>
            <span class="perm-count-label">项</span>
          </span>
        </div>

        <div class="menu-list">
          <div
            v-for="item in displayList"
            :key="item.node.id"
            class="tree-row"
            :class="{
              'is-leaf': item.isLeaf,
              'is-group': !item.isLeaf,
              'no-guide': item.depth === 0,
              'is-selected': selectedMenuId === item.node.id,
            }"
            :style="{ '--depth': item.depth }"
            @click="item.isLeaf ? selectMenu(item.node.id) : toggleExpand(item.node.id)"
          >
            <span v-if="!item.isLeaf" class="tree-arrow" :class="{ expanded: item.isExpanded }">
              <svg width="12" height="12" viewBox="0 0 12 12">
                <path d="M4 2l4 4-4 4" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"/>
              </svg>
            </span>
            <span v-else class="tree-arrow-placeholder"></span>

            <span
              class="tree-checkbox"
              :class="{
                checked: effectiveMenuIds.has(item.node.id),
                indeterminate: !item.isLeaf && item.checkState === 'half',
                disabled: isInherited(item.node.id),
              }"
              @click.stop="toggleMenuCheck(item.node)"
            >
              <svg v-if="effectiveMenuIds.has(item.node.id)" class="check-icon" width="12" height="12" viewBox="0 0 12 12">
                <path d="M2 6l3 3 5-5" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/>
              </svg>
              <svg v-else-if="!item.isLeaf && item.checkState === 'half'" class="check-icon" width="12" height="12" viewBox="0 0 12 12">
                <path d="M3 6h6" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"/>
              </svg>
            </span>

            <span class="tree-label" v-html="highlightText(item.node.title)"></span>

            <span v-if="!item.isLeaf" class="tree-badge">{{ checkedCountOf(item.node) }}/{{ leafCountOf(item.node) }}</span>
            <span v-else-if="(actionPointsMap.get(item.node.id) ?? []).length > 0" class="action-count-badge">
              {{ (actionPointsMap.get(item.node.id) ?? []).length }} 个动作
            </span>
          </div>

          <el-empty v-if="displayList.length === 0" description="未找到匹配的菜单" :image-size="80" />
        </div>
      </template>
      <div v-else class="empty-hint">
        <el-empty description="请在左侧选择一个角色或用户" :image-size="100" />
      </div>
    </div>

    <!-- ===== 右侧：菜单接口（动作点）面板 ===== -->
    <div class="action-panel">
      <template v-if="selectedTarget && selectedMenuId !== null">
        <div class="action-header">
          <div class="action-title-row">
            <span class="action-title">动作点</span>
            <span class="action-menu-name">{{ selectedMenuTitle }}</span>
          </div>
          <span class="action-count" v-if="selectedMenuActions.length > 0">
            <span class="action-count-num">{{ actionCheckedCount }}</span>
            <span class="action-count-sep">/</span>
            <span>{{ selectedMenuActions.length }}</span>
          </span>
        </div>

        <div class="action-list">
          <div
            v-for="action in selectedMenuActions"
            :key="action.id"
            class="action-row"
            :class="{ disabled: isInherited(-action.id) }"
            @click="toggleActionCheck(action.id)"
          >
            <span
              class="tree-checkbox"
              :class="{
                checked: effectiveMenuIds.has(-action.id),
                disabled: isInherited(-action.id),
              }"
            >
              <svg v-if="effectiveMenuIds.has(-action.id)" class="check-icon" width="12" height="12" viewBox="0 0 12 12">
                <path d="M2 6l3 3 5-5" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/>
              </svg>
            </span>
            <span class="action-label">{{ action.title }}</span>
            <span class="action-code">{{ action.name }}</span>
          </div>

          <el-empty v-if="selectedMenuActions.length === 0" description="该菜单无动作点" :image-size="60" />
        </div>
      </template>
      <div v-else class="empty-hint">
        <el-empty description="请在中间选择一个菜单" :image-size="80" />
      </div>
    </div>
  </div>
</template>

<style scoped>
.permission-page { display: flex; height: 100%; overflow: hidden; }

/* ===== 左侧角色面板 ===== */
.role-panel {
  width: 260px; flex-shrink: 0;
  border-right: 1px solid var(--border);
  display: flex; flex-direction: column; overflow: hidden;
  background: var(--surface);
}
.panel-header { padding: 16px 18px 12px; border-bottom: 1px solid var(--border); flex-shrink: 0; }
.panel-title-row { display: flex; align-items: center; gap: 8px; }
.panel-title { font-size: 15px; font-weight: 700; color: var(--text-main); }
.panel-count {
  font-size: 12px; color: #fff; background: var(--brand);
  border-radius: 999px; padding: 1px 9px; line-height: 18px;
}
.role-tree { flex: 1; overflow-y: auto; padding: 6px 10px; }

.role-row {
  display: flex; align-items: center; gap: 8px;
  padding: 10px 12px; border-radius: var(--radius-sm); cursor: pointer;
  transition: background 0.15s, border-color 0.15s; margin-bottom: 2px;
  background: transparent; border: 1px solid transparent;
}
.role-row:hover { background: var(--page-bg); }
.role-row.active { background: var(--brand-50); border-color: var(--brand-200); }
.expand-arrow {
  display: inline-flex; align-items: center; justify-content: center;
  width: 18px; height: 18px; color: var(--text-sub);
  transition: transform 0.2s ease; flex-shrink: 0; cursor: pointer; border-radius: 4px;
}
.expand-arrow:hover { background: var(--border); }
.expand-arrow.expanded { transform: rotate(90deg); }
.expand-arrow.no-children { visibility: hidden; }
.role-avatar {
  width: 30px; height: 30px; border-radius: var(--radius-sm);
  background: var(--brand-gradient); color: #fff;
  font-size: 13px; font-weight: 700;
  display: flex; align-items: center; justify-content: center; flex-shrink: 0;
}
.role-info { flex: 1; min-width: 0; display: flex; flex-direction: column; gap: 1px; }
.role-name {
  font-size: 14px; font-weight: 600; color: var(--text-main);
  overflow: hidden; text-overflow: ellipsis; white-space: nowrap;
}
.role-row.active .role-name { color: var(--brand-deep); }
.role-code { font-size: 11px; color: var(--text-sub); font-family: monospace; }
.role-user-count {
  font-size: 12px; color: var(--text-sub); background: var(--page-bg);
  padding: 1px 8px; border-radius: 999px; flex-shrink: 0;
  font-variant-numeric: tabular-nums;
}
.shield { font-size: 10px; font-weight: 700; padding: 1px 8px; border-radius: 999px; letter-spacing: 0.3px; flex-shrink: 0; }
.shield-admin { background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: #fff; }
.shield-user { background: var(--brand-gradient); color: #fff; }

.user-list { padding: 2px 0 6px 28px; }
.user-row {
  display: flex; align-items: center; gap: 10px;
  padding: 7px 10px; border-radius: var(--radius-sm); cursor: pointer;
  transition: background 0.15s; margin-bottom: 1px;
}
.user-row:hover { background: var(--page-bg); }
.user-row.active { background: var(--brand-50); }
.user-avatar {
  width: 24px; height: 24px; border-radius: 50%;
  background: var(--brand-100); color: var(--brand-deep);
  font-size: 11px; font-weight: 600;
  display: flex; align-items: center; justify-content: center; flex-shrink: 0;
}
.user-info { flex: 1; min-width: 0; display: flex; flex-direction: column; gap: 1px; }
.user-name {
  font-size: 13px; font-weight: 500; color: var(--text-main);
  overflow: hidden; text-overflow: ellipsis; white-space: nowrap;
}
.user-row.active .user-name { color: var(--brand-deep); }
.user-account { font-size: 11px; color: var(--text-sub); font-family: monospace; }
.user-empty { font-size: 12px; color: var(--text-sub); padding: 8px 10px; }

/* ===== 中间菜单面板 ===== */
.menu-panel {
  width: 360px; flex-shrink: 0;
  border-right: 1px solid var(--border);
  display: flex; flex-direction: column; overflow: hidden;
  background: var(--surface);
}
.perm-header {
  display: flex; align-items: center; justify-content: space-between;
  padding: 16px 20px; border-bottom: 1px solid var(--border); flex-shrink: 0;
}
.perm-title { display: flex; align-items: center; gap: 10px; flex-wrap: wrap; }
.perm-role-name { font-size: 17px; font-weight: 700; color: var(--text-main); }
.perm-role-code {
  font-size: 12px; color: var(--text-sub); font-family: monospace;
  background: var(--page-bg); padding: 2px 10px; border-radius: var(--radius-sm);
}
.perm-user-name { font-size: 17px; font-weight: 700; color: var(--text-main); }
.perm-inherit {
  font-size: 12px; color: var(--brand-deep);
  background: var(--brand-50); padding: 2px 10px; border-radius: var(--radius-sm);
}

.perm-search-bar {
  display: flex; align-items: center; gap: 16px;
  padding: 12px 20px; border-bottom: 1px solid var(--border); flex-shrink: 0;
}
.perm-search-bar .el-input { flex: 1; }
.perm-count { display: flex; align-items: baseline; gap: 1px; font-size: 13px; color: var(--text-sub); white-space: nowrap; }
.perm-count-num { font-size: 18px; font-weight: 700; color: var(--brand); }
.perm-count-sep { color: var(--border); margin: 0 1px; }
.perm-count-total { font-size: 14px; color: var(--text-sub); }
.perm-count-label { font-size: 12px; color: var(--text-sub); margin-left: 2px; }

/* 菜单列表 */
.menu-list { flex: 1; overflow-y: auto; padding: 8px 0; }

.tree-row {
  display: flex; align-items: center; gap: 8px;
  height: 40px; cursor: pointer; user-select: none;
  transition: background 0.12s ease; position: relative;
  padding-left: calc(var(--depth, 0) * 24px + 12px);
  padding-right: 16px;
}
.tree-row:hover { background: var(--brand-50); }
.tree-row.is-selected { background: var(--brand-50); border-left: 3px solid var(--brand); padding-left: calc(var(--depth, 0) * 24px + 9px); }

.tree-row::before {
  content: ''; position: absolute;
  left: calc((var(--depth, 0) - 1) * 24px + 18px);
  top: 0; bottom: 0; width: 1px; background: var(--border);
}
.tree-row.no-guide::before { display: none; }

.tree-arrow {
  display: inline-flex; align-items: center; justify-content: center;
  width: 20px; height: 20px; color: var(--text-sub);
  transition: transform 0.2s ease; flex-shrink: 0;
}
.tree-arrow.expanded { transform: rotate(90deg); }
.tree-arrow-placeholder { width: 20px; flex-shrink: 0; }

.tree-checkbox {
  width: 18px; height: 18px; border-radius: 4px;
  border: 1.5px solid var(--border);
  display: inline-flex; align-items: center; justify-content: center;
  cursor: pointer; flex-shrink: 0; transition: all 0.15s;
  background: #fff; color: #fff;
}
.tree-checkbox.checked { background: var(--brand); border-color: var(--brand); }
.tree-checkbox.indeterminate { background: var(--brand); border-color: var(--brand); }
.tree-checkbox.disabled { background: var(--brand-100); border-color: var(--brand-200); cursor: not-allowed; color: var(--brand); }
.tree-checkbox.disabled.checked { background: var(--brand-100); border-color: var(--brand-200); }
.check-icon { pointer-events: none; }

.tree-label {
  flex: 1; min-width: 0; font-size: 14px; color: var(--text-main);
  overflow: hidden; text-overflow: ellipsis; white-space: nowrap;
}
.tree-row.is-group .tree-label { font-weight: 600; }
.tree-row.is-leaf .tree-label { font-weight: 400; color: var(--text-sub); }
.tree-row.is-selected .tree-label { color: var(--brand-deep); font-weight: 600; }

.tree-badge {
  font-size: 11px; color: var(--text-sub); background: var(--page-bg);
  padding: 1px 8px; border-radius: 999px; flex-shrink: 0;
  font-variant-numeric: tabular-nums;
}
.action-count-badge {
  font-size: 10px; color: var(--el-color-warning); background: var(--el-color-warning-light-9);
  padding: 1px 6px; border-radius: 4px; flex-shrink: 0;
  border: 1px solid var(--el-color-warning-light-5);
}

:deep(.search-highlight) { color: var(--brand); background: var(--brand-50); border-radius: 2px; padding: 0 2px; }

/* ===== 右侧动作点面板 ===== */
.action-panel {
  flex: 1; min-width: 0; display: flex; flex-direction: column; overflow: hidden;
  background: var(--surface);
}
.action-header {
  display: flex; align-items: center; justify-content: space-between;
  padding: 16px 24px; border-bottom: 1px solid var(--border); flex-shrink: 0;
}
.action-title-row { display: flex; align-items: center; gap: 12px; }
.action-title { font-size: 15px; font-weight: 700; color: var(--text-main); }
.action-menu-name {
  font-size: 14px; color: var(--brand-deep);
  background: var(--brand-50); padding: 2px 12px; border-radius: var(--radius-sm);
}
.action-count { display: flex; align-items: baseline; gap: 1px; font-size: 13px; color: var(--text-sub); }
.action-count-num { font-size: 18px; font-weight: 700; color: var(--brand); }
.action-count-sep { color: var(--border); margin: 0 1px; }

.action-list { flex: 1; overflow-y: auto; padding: 8px 0; }

.action-row {
  display: flex; align-items: center; gap: 10px;
  height: 42px; cursor: pointer; user-select: none;
  transition: background 0.12s ease;
  padding: 0 24px;
}
.action-row:hover { background: var(--brand-50); }
.action-row.disabled { opacity: 0.55; }

.action-label {
  flex: 1; min-width: 0; font-size: 14px; color: var(--text-main);
  overflow: hidden; text-overflow: ellipsis; white-space: nowrap;
}
.action-code {
  font-size: 11px; color: var(--text-sub); font-family: monospace;
  flex-shrink: 0; background: var(--page-bg); padding: 1px 8px; border-radius: 4px;
}

.empty-hint { flex: 1; display: flex; align-items: center; justify-content: center; }
</style>

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
  type MenuFlatDto,
} from '@/common/api/permission'

// ========== 菜单树结构 ==========
interface MenuTreeNode {
  id: number
  title: string
  children?: MenuTreeNode[]
}
const menuTree = ref<MenuTreeNode[]>([])

/** 扁平列表转树 */
function buildTree(flat: MenuFlatDto[]): MenuTreeNode[] {
  const map = new Map<number, MenuTreeNode>()
  flat.forEach((m) => map.set(m.id, { id: m.id, title: m.title, children: [] }))
  const roots: MenuTreeNode[] = []
  flat.forEach((m) => {
    const node = map.get(m.id)!
    if (m.parentId != null && map.has(m.parentId)) {
      map.get(m.parentId)!.children!.push(node)
    } else {
      roots.push(node)
    }
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

/** 建立 节点ID → 父节点ID 映射 */
function buildLeafParentMap(nodes: MenuTreeNode[]): Map<number, number> {
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

/** 收集指定节点下的所有叶子 ID */
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

// 继承的角色菜单 ID（用户模式下，来自所有角色，含父级分组）
const inheritedMenuIds = ref<Set<number>>(new Set())
// 直接选中的菜单 ID（角色模式 = 角色权限；用户模式 = 用户级授权，均含叶子+父级）
const directMenuIds = ref<Set<number>>(new Set())

const leafParentMap = computed(() => buildLeafParentMap(menuTree.value))
const allLeafIds = computed(() => getAllLeafIds(menuTree.value))

/** 有效菜单 = 继承 ∪ 直接（角色模式时 inherited 为空） */
const effectiveMenuIds = computed(() => {
  if (selectedTarget.value?.type === 'user') {
    return new Set([...inheritedMenuIds.value, ...directMenuIds.value])
  }
  return new Set(directMenuIds.value)
})

const selectedRole = computed(() => {
  const target = selectedTarget.value
  if (!target) return null
  return rolesWithUsers.value.find((r) => r.id === target.roleId) ?? null
})

const selectedUser = computed(() => {
  const target = selectedTarget.value
  if (!target || target.type !== 'user') return null
  const role = rolesWithUsers.value.find((r) => r.id === target.roleId)
  return role?.users.find((u) => u.id === target.userId) ?? null
})

/** 用户继承的所有角色名 */
const inheritedRoleNames = computed(() => {
  const target = selectedTarget.value
  if (!target || target.type !== 'user') return []
  const userId = target.userId
  return rolesWithUsers.value
    .filter((r) => r.users.some((u) => u.id === userId))
    .map((r) => r.name)
})

const checkedCount = computed(() => allLeafIds.value.filter((id) => effectiveMenuIds.value.has(id)).length)
const totalLeafCount = computed(() => allLeafIds.value.length)

// ========== 搜索 ==========
const searchQuery = ref('')
const expandedNodes = ref<Set<number>>(new Set())

function checkHasMatch(node: MenuTreeNode, query: string): boolean {
  if (node.title.toLowerCase().includes(query)) return true
  if (node.children) return node.children.some((c) => checkHasMatch(c, query))
  return false
}

function escapeHtml(text: string): string {
  return text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
}

function highlightText(text: string): string {
  const query = searchQuery.value.trim()
  if (!query) return escapeHtml(text)
  const escapedText = escapeHtml(text)
  const escapedQueryHtml = escapeHtml(query)
  const escapedQueryRegex = escapedQueryHtml.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
  const regex = new RegExp(`(${escapedQueryRegex})`, 'gi')
  return escapedText.replace(regex, '<mark class="search-highlight">$1</mark>')
}

// ========== 分组勾选状态 ==========
function groupCheckState(group: MenuTreeNode): 'all' | 'half' | 'none' {
  if (!group.children) return effectiveMenuIds.value.has(group.id) ? 'all' : 'none'
  const leaves = getLeafIdsOf(group)
  let count = 0
  leaves.forEach((id) => { if (effectiveMenuIds.value.has(id)) count++ })
  if (count === 0) return 'none'
  if (count === leaves.length) return 'all'
  return 'half'
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
  const query = searchQuery.value.trim().toLowerCase()

  function walk(nodes: MenuTreeNode[], depth: number) {
    nodes.forEach((node) => {
      const isLeaf = !node.children || node.children.length === 0
      const state = groupCheckState(node)

      if (!query) {
        const expanded = expandedNodes.value.has(node.id)
        result.push({ node, depth, isLeaf, isExpanded: expanded, checkState: state })
        if (!isLeaf && expanded) {
          walk(node.children!, depth + 1)
        }
      } else {
        const matched = node.title.toLowerCase().includes(query)
        const hasMatch = matched || (node.children ? checkHasMatch(node, query) : false)
        if (matched || hasMatch) {
          result.push({ node, depth, isLeaf, isExpanded: true, checkState: state })
          if (!isLeaf && hasMatch) {
            walk(node.children!, depth + 1)
          }
        }
      }
    })
  }

  walk(menuTree.value, 0)
  return result
})

// ========== 计数辅助 ==========
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

// ========== 左侧树操作 ==========
function toggleRoleExpand(id: number) {
  const s = new Set(expandedRoles.value)
  if (s.has(id)) s.delete(id)
  else s.add(id)
  expandedRoles.value = s
}

function selectRole(roleId: number) {
  const role = rolesWithUsers.value.find((r) => r.id === roleId)
  if (!role) return
  selectedTarget.value = { type: 'role', roleId }
  const leafSet = new Set(allLeafIds.value)
  if (role.isAdmin) {
    directMenuIds.value = new Set(allLeafIds.value)
  } else {
    directMenuIds.value = new Set((role.menuIds ?? []).filter((id) => leafSet.has(id)))
  }
  inheritedMenuIds.value = new Set()
  // 角色模式下也展开所有分组，方便查看
  expandedNodes.value = new Set(menuTree.value.map((n) => n.id))
}

async function selectUser(userId: string, roleId: number) {
  selectedTarget.value = { type: 'user', userId, roleId }
  const leafSet = new Set(allLeafIds.value)

  // 继承 = 用户所有角色的菜单并集（含父级分组 ID）
  const inheritedSet = new Set<number>()
  const userRoles = rolesWithUsers.value.filter((r) => r.users.some((u) => u.id === userId))
  userRoles.forEach((role) => {
    if (role.isAdmin) {
      // admin 全通：收集所有节点 ID
      const walk = (nodes: MenuTreeNode[]) => {
        nodes.forEach((n) => {
          inheritedSet.add(n.id)
          if (n.children) walk(n.children)
        })
      }
      walk(menuTree.value)
    } else {
      ;(role.menuIds ?? []).forEach((id) => inheritedSet.add(id))
    }
  })
  inheritedMenuIds.value = inheritedSet

  // 用户直接授权（只保留叶子 ID）
  try {
    const userMenuIds = await getUserPermissions(userId)
    directMenuIds.value = new Set(userMenuIds.filter((id) => leafSet.has(id)))
  } catch {
    directMenuIds.value = new Set()
  }

  expandedNodes.value = new Set(menuTree.value.map((n) => n.id))
}

// ========== 右侧树操作 ==========
function toggleExpand(id: number) {
  const s = new Set(expandedNodes.value)
  if (s.has(id)) s.delete(id)
  else s.add(id)
  expandedNodes.value = s
}

function toggleLeaf(id: number) {
  if (isInherited(id)) return
  const s = new Set(directMenuIds.value)
  if (s.has(id)) s.delete(id)
  else s.add(id)
  directMenuIds.value = s
}

function toggleGroupCheck(group: MenuTreeNode) {
  const leaves = getLeafIdsOf(group).filter((id) => !isInherited(id))
  if (leaves.length === 0) return
  const allChecked = leaves.every((id) => effectiveMenuIds.value.has(id))
  const s = new Set(directMenuIds.value)
  if (allChecked) {
    leaves.forEach((id) => s.delete(id))
  } else {
    leaves.forEach((id) => s.add(id))
  }
  directMenuIds.value = s
}

// ========== 数据加载 ==========
async function loadData() {
  loading.value = true
  try {
    const [roleList, menus] = await Promise.all([listRolesWithUsers(), listPermissionMenusFlat()])
    rolesWithUsers.value = roleList
    menuTree.value = buildTree(menus)
    if (roleList.length > 0 && selectedTarget.value === null) {
      selectRole(roleList[0].id)
      // 默认展开第一个角色
      const s = new Set(expandedRoles.value)
      s.add(roleList[0].id)
      expandedRoles.value = s
    }
  } catch { /* 错误已由 request.ts 弹出提示 */ } finally {
    loading.value = false
  }
}

// ========== 保存 ==========
async function savePermissions() {
  const target = selectedTarget.value
  if (!target) return
  const menuIds = new Set<number>(directMenuIds.value)
  // 补齐父级分组 ID（只补不在继承中的）
  directMenuIds.value.forEach((leafId) => {
    let pid = leafParentMap.value.get(leafId)
    while (pid !== undefined) {
      if (!inheritedMenuIds.value.has(pid)) menuIds.add(pid)
      pid = leafParentMap.value.get(pid)
    }
  })

  saving.value = true
  try {
    if (target.type === 'role') {
      await saveRolePermissions({ roleId: target.roleId, menuIds: [...menuIds] })
      // 更新本地角色数据
      const r = rolesWithUsers.value.find((x) => x.id === target.roleId)
      if (r) r.menuIds = [...menuIds]
    } else {
      await saveUserPermissions({ userId: target.userId, menuIds: [...menuIds] })
    }
    ElMessage.success('权限已保存')
  } catch { /* 错误已由 request.ts 弹出提示 */ } finally {
    saving.value = false
  }
}

onMounted(loadData)
onActivated(loadData)
</script>

<template>
  <div class="permission-page">
    <!-- 左侧：角色 → 用户树 -->
    <div class="role-panel" v-loading="loading">
      <div class="panel-header">
        <div class="panel-title-row">
          <span class="panel-title">角色</span>
          <span class="panel-count">{{ rolesWithUsers.length }}</span>
        </div>
      </div>
      <div class="role-tree">
        <template v-for="role in rolesWithUsers" :key="role.id">
          <!-- 角色行 -->
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
          <!-- 用户行（展开时显示） -->
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

    <!-- 右侧：权限树面板 -->
    <div class="permission-panel">
      <template v-if="selectedTarget">
        <!-- 头部 -->
        <div class="perm-header">
          <div class="perm-title">
            <template v-if="selectedTarget.type === 'role'">
              <span class="perm-role-name">{{ selectedRole?.name }}</span>
              <span class="perm-role-code">{{ selectedRole?.code }}</span>
            </template>
            <template v-else>
              <span class="perm-user-name">{{ selectedUser?.displayName || selectedUser?.account }}</span>
              <span v-if="inheritedRoleNames.length" class="perm-inherit">
                继承自 {{ inheritedRoleNames.join('、') }}
              </span>
            </template>
          </div>
          <el-button type="primary" :loading="saving" @click="savePermissions">
            保存权限
          </el-button>
        </div>

        <!-- 搜索栏 -->
        <div class="perm-search-bar">
          <el-input
            v-model="searchQuery"
            placeholder="搜索权限..."
            :prefix-icon="Search"
            clearable
            size="default"
          />
          <span class="perm-count">
            <span class="perm-count-num">{{ checkedCount }}</span>
            <span class="perm-count-sep">/</span>
            <span class="perm-count-total">{{ totalLeafCount }}</span>
            <span class="perm-count-label">项</span>
          </span>
        </div>

        <!-- 复选框树列表 -->
        <div class="perm-tree">
          <div
            v-for="item in displayList"
            :key="item.node.id"
            class="tree-row"
            :class="{ 'is-leaf': item.isLeaf, 'is-group': !item.isLeaf, 'no-guide': item.depth === 0 }"
            :style="{ '--depth': item.depth }"
            @click="item.isLeaf ? toggleLeaf(item.node.id) : toggleExpand(item.node.id)"
          >
            <!-- 箭头 -->
            <span v-if="!item.isLeaf" class="tree-arrow" :class="{ expanded: item.isExpanded }">
              <svg width="12" height="12" viewBox="0 0 12 12">
                <path d="M4 2l4 4-4 4" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"/>
              </svg>
            </span>
            <span v-else class="tree-arrow-placeholder"></span>

            <!-- 复选框 -->
            <span
              class="tree-checkbox"
              :class="{
                checked: effectiveMenuIds.has(item.node.id),
                indeterminate: !item.isLeaf && item.checkState === 'half',
                disabled: item.isLeaf && isInherited(item.node.id),
              }"
              @click.stop="item.isLeaf ? toggleLeaf(item.node.id) : toggleGroupCheck(item.node)"
            >
              <!-- 对勾：已选中 -->
              <svg v-if="effectiveMenuIds.has(item.node.id)" class="check-icon" width="12" height="12" viewBox="0 0 12 12">
                <path d="M2 6l3 3 5-5" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/>
              </svg>
              <!-- 半选横线：分组半选 -->
              <svg v-else-if="!item.isLeaf && item.checkState === 'half'" class="check-icon" width="12" height="12" viewBox="0 0 12 12">
                <path d="M3 6h6" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"/>
              </svg>
            </span>

            <!-- 名称（搜索高亮） -->
            <span class="tree-label" v-html="highlightText(item.node.title)"></span>

            <!-- 继承标签 -->
            <span v-if="item.isLeaf && isInherited(item.node.id)" class="inherit-tag">继承</span>

            <!-- 计数徽章：仅分组节点显示 -->
            <span v-if="!item.isLeaf" class="tree-badge">{{ checkedCountOf(item.node) }}/{{ leafCountOf(item.node) }}</span>
          </div>

          <el-empty v-if="displayList.length === 0" description="未找到匹配的权限" :image-size="80" />
        </div>
      </template>

      <div v-else class="empty-hint">
        <el-empty description="请在左侧选择一个角色或用户" :image-size="100" />
      </div>
    </div>
  </div>
</template>

<style scoped>
.permission-page {
  display: flex;
  height: 100%;
  overflow: hidden;
}

/* ===== 左侧角色面板 ===== */
.role-panel {
  width: 300px;
  flex-shrink: 0;
  border-right: 1px solid var(--border);
  display: flex;
  flex-direction: column;
  overflow: hidden;
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

/* 角色行 */
.role-row {
  display: flex; align-items: center; gap: 8px;
  padding: 10px 12px; border-radius: var(--radius-sm); cursor: pointer;
  transition: background 0.15s, border-color 0.15s; margin-bottom: 2px;
  background: transparent; border: 1px solid transparent;
}
.role-row:hover { background: var(--page-bg); }
.role-row.active {
  background: var(--brand-50);
  border-color: var(--brand-200);
}
.expand-arrow {
  display: inline-flex; align-items: center; justify-content: center;
  width: 18px; height: 18px; color: var(--text-sub);
  transition: transform 0.2s ease; flex-shrink: 0; cursor: pointer;
  border-radius: 4px;
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

/* 用户列表 */
.user-list { padding: 2px 0 6px 28px; }
.user-row {
  display: flex; align-items: center; gap: 10px;
  padding: 7px 10px; border-radius: var(--radius-sm); cursor: pointer;
  transition: background 0.15s; margin-bottom: 1px;
}
.user-row:hover { background: var(--page-bg); }
.user-row.active {
  background: var(--brand-50);
}
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

/* ===== 右侧权限面板 ===== */
.permission-panel { flex: 1; min-width: 0; display: flex; flex-direction: column; overflow: hidden; background: var(--surface); }
.perm-header {
  display: flex; align-items: center; justify-content: space-between;
  padding: 16px 24px; border-bottom: 1px solid var(--border); flex-shrink: 0;
}
.perm-title { display: flex; align-items: center; gap: 10px; flex-wrap: wrap; }
.perm-role-name { font-size: 18px; font-weight: 700; color: var(--text-main); }
.perm-role-code {
  font-size: 12px; color: var(--text-sub); font-family: monospace;
  background: var(--page-bg); padding: 2px 10px; border-radius: var(--radius-sm);
}
.perm-user-name { font-size: 18px; font-weight: 700; color: var(--text-main); }
.perm-inherit {
  font-size: 12px; color: var(--brand-deep);
  background: var(--brand-50); padding: 2px 10px; border-radius: var(--radius-sm);
}

/* 搜索栏 */
.perm-search-bar {
  display: flex; align-items: center; gap: 16px;
  padding: 12px 24px; border-bottom: 1px solid var(--border); flex-shrink: 0;
}
.perm-search-bar .el-input { flex: 1; }
.perm-count { display: flex; align-items: baseline; gap: 1px; font-size: 13px; color: var(--text-sub); white-space: nowrap; }
.perm-count-num { font-size: 18px; font-weight: 700; color: var(--brand); }
.perm-count-sep { color: var(--border); margin: 0 1px; }
.perm-count-total { font-size: 14px; color: var(--text-sub); }
.perm-count-label { font-size: 12px; color: var(--text-sub); margin-left: 2px; }

/* ===== 复选框树列表 ===== */
.perm-tree { flex: 1; overflow-y: auto; padding: 8px 0; }

.tree-row {
  display: flex; align-items: center; gap: 8px;
  height: 40px;
  cursor: pointer; user-select: none;
  transition: background 0.12s ease;
  position: relative;
  padding-left: calc(var(--depth, 0) * 24px + 12px);
  padding-right: 24px;
}
.tree-row:hover { background: var(--brand-50); }

/* 左侧引导线 */
.tree-row::before {
  content: '';
  position: absolute;
  left: calc((var(--depth, 0) - 1) * 24px + 18px);
  top: 0; bottom: 0;
  width: 1px;
  background: var(--border);
}
.tree-row.no-guide::before { display: none; }

/* 箭头 */
.tree-arrow {
  display: inline-flex; align-items: center; justify-content: center;
  width: 20px; height: 20px; color: var(--text-sub);
  transition: transform 0.2s ease; flex-shrink: 0;
}
.tree-arrow.expanded { transform: rotate(90deg); }
.tree-arrow-placeholder { width: 20px; flex-shrink: 0; }

/* 复选框 */
.tree-checkbox {
  width: 18px; height: 18px; border-radius: 4px;
  border: 1.5px solid var(--border);
  display: inline-flex; align-items: center; justify-content: center;
  cursor: pointer; flex-shrink: 0; transition: all 0.15s;
  background: #fff; color: #fff;
}
.tree-checkbox.checked {
  background: var(--brand); border-color: var(--brand);
}
.tree-checkbox.indeterminate {
  background: var(--brand); border-color: var(--brand);
}
.tree-checkbox.disabled {
  background: var(--brand-100); border-color: var(--brand-200);
  cursor: not-allowed; color: var(--brand);
}
.tree-checkbox.disabled.checked {
  background: var(--brand-100); border-color: var(--brand-200);
}
.check-icon { pointer-events: none; }

/* 名称 */
.tree-label {
  flex: 1; min-width: 0;
  font-size: 14px; color: var(--text-main);
  overflow: hidden; text-overflow: ellipsis; white-space: nowrap;
}
.tree-row.is-group .tree-label { font-weight: 600; }
.tree-row.is-leaf .tree-label { font-weight: 400; color: var(--text-sub); }

/* 继承标签 */
.inherit-tag {
  font-size: 10px; color: var(--brand-deep); background: var(--brand-50);
  padding: 1px 6px; border-radius: 4px; flex-shrink: 0;
  border: 1px solid var(--brand-200);
}

/* 计数徽章 */
.tree-badge {
  font-size: 11px; color: var(--text-sub); background: var(--page-bg);
  padding: 1px 8px; border-radius: 999px; flex-shrink: 0;
  font-variant-numeric: tabular-nums;
}

/* 搜索高亮 */
:deep(.search-highlight) {
  color: var(--brand);
  background: var(--brand-50);
  border-radius: 2px;
  padding: 0 2px;
}

.empty-hint { flex: 1; display: flex; align-items: center; justify-content: center; }
</style>

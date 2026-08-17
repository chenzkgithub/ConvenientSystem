<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { ElMessage, ElTree } from 'element-plus'
import {
  listRoles,
  saveRole,
  deleteRole,
  toggleRoleEnabled,
  listMenusFlat,
  type RoleDto,
  type RoleSaveDto,
  type MenuFlatDto,
} from '@/common/api/roleManage'
import { confirmDelete } from '@/common/utils/confirm'
import CommonDataTable, { type DataTableColumn } from '@/common/components/CommonDataTable.vue'
import CommonDialog from '@/common/components/CommonDialog.vue'

const loading = ref(false)
const list = ref<RoleDto[]>([])

const columns: DataTableColumn<RoleDto>[] = [
  { prop: 'name', label: '角色名称', minWidth: 140 },
  { prop: 'code', label: '编码', width: 140 },
  { prop: 'description', label: '描述', minWidth: 180 },
  { prop: 'enabled', label: '启用', width: 100, custom: true },
  { prop: 'isAdmin', label: '管理员', width: 90, custom: true },
  { prop: 'dataScope', label: '数据范围', width: 100, custom: true },
  { prop: 'createTime', label: '创建时间', width: 170, type: 'date' },
]

// 菜单树（供分配可见菜单）
interface MenuTreeNode {
  id: number
  title: string
  children?: MenuTreeNode[]
}
const menuTree = ref<MenuTreeNode[]>([])
const treeRef = ref<InstanceType<typeof ElTree>>()

/** 扁平菜单列表转树 */
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
  // 清理空 children，避免渲染多余展开箭头
  const prune = (nodes: MenuTreeNode[]) => {
    nodes.forEach((n) => {
      if (n.children && n.children.length === 0) delete n.children
      else if (n.children) prune(n.children)
    })
  }
  prune(roots)
  return roots
}

/** 从树中提取叶子节点 ID（用于 setCheckedKeys 只传叶子，避免父节点被重复勾选异常） */
function getLeafIds(nodes: MenuTreeNode[], ids: number[]): number[] {
  const leaves = new Set<number>()
  const walk = (list: MenuTreeNode[]) => {
    list.forEach((n) => {
      if (!n.children || n.children.length === 0) leaves.add(n.id)
      else walk(n.children)
    })
  }
  walk(nodes)
  return ids.filter((id) => leaves.has(id))
}

async function loadData() {
  loading.value = true
  try {
    const [roles, menus] = await Promise.all([listRoles(), listMenusFlat()])
    list.value = roles
    menuTree.value = buildTree(menus)
  } catch { /* 错误已由 request.ts 弹出提示 */ } finally {
    loading.value = false
  }
}

// ========== 编辑弹窗 ==========
const dialogVisible = ref(false)
const isEdit = ref(false)
const form = reactive<RoleSaveDto>({
  id: 0,
  name: '',
  code: '',
  description: '',
  enabled: true,
  isAdmin: false,
  dataScope: 0,
  menuIds: [],
})

function openCreate() {
  isEdit.value = false
  Object.assign(form, { id: 0, name: '', code: '', description: '', enabled: true, isAdmin: false, dataScope: 0, menuIds: [] })
  dialogVisible.value = true
  // 等弹窗与树渲染后清空勾选
  setTimeout(() => treeRef.value?.setCheckedKeys([], false), 0)
}

function openEdit(row: any) {
  const r = row as RoleDto
  isEdit.value = true
  Object.assign(form, {
    id: r.id,
    name: r.name,
    code: r.code,
    description: r.description ?? '',
    enabled: r.enabled,
    isAdmin: r.isAdmin ?? false,
    dataScope: r.dataScope ?? 0,
    menuIds: [...r.menuIds],
  })
  dialogVisible.value = true
  setTimeout(() => treeRef.value?.setCheckedKeys(getLeafIds(menuTree.value, r.menuIds ?? []), false), 0)
}

async function submitForm() {
  if (!form.name.trim()) return ElMessage.warning('请填写角色名称')
  if (!form.code.trim()) return ElMessage.warning('请填写角色编码')
  // 收集完全选中 + 半选中（父分组）作为可见菜单
  const checked = (treeRef.value?.getCheckedKeys(false) ?? []) as number[]
  const half = (treeRef.value?.getHalfCheckedKeys() ?? []) as number[]
  form.menuIds = [...checked, ...half]
  try {
    await saveRole({ ...form })
    ElMessage.success('保存成功')
    dialogVisible.value = false
    await loadData()
  } catch { /* 错误已由 request.ts 弹出提示 */ }
}

async function onDelete(row: any) {
  const r = row as RoleDto
  const ok = await confirmDelete(r.name, () => deleteRole(r.id))
  if (ok) await loadData()
}

async function onToggleEnabled(row: any, val: boolean) {
  const r = row as RoleDto
  try {
    await toggleRoleEnabled(r.id, val)
    ElMessage.success(val ? '已启用' : '已停用')
    await loadData()
  } catch {
    // 错误已由 request.ts 弹出提示
    await loadData()
  }
}

onMounted(loadData)
</script>

<template>
  <div class="role-page">
    <!-- 角色列表（提示文字与新增按钮封装进列表组件插槽，与表格统一对齐） -->
    <CommonDataTable
      :columns="columns"
      :data="list"
      :loading="loading"
      :total="list.length"
      :show-pagination="false"
      :actions-width="150"
      empty-text="暂无角色"
      @load="loadData"
    >
      <template #filters>
        <span class="hint">角色决定用户可见的菜单与可访问的接口；管理员标记仅影响数据范围，菜单权限统一按配置。</span>
      </template>
      <template #toolbar>
        <el-button type="primary" @click="openCreate">新增角色</el-button>
      </template>
      <template #cell-enabled="{ row }">
        <el-switch
          :model-value="row.enabled"
          @update:model-value="(val: string | number | boolean) => onToggleEnabled(row, val as boolean)"
          inline-prompt
          active-text="启"
          inactive-text="停"
        />
      </template>
      <template #cell-isAdmin="{ row }">
        <el-tag v-if="(row as RoleDto).isAdmin" type="success" size="small">是</el-tag>
        <el-tag v-else type="info" size="small">否</el-tag>
      </template>
      <template #cell-dataScope="{ row }">
        <el-tag v-if="(row as RoleDto).dataScope === 1" type="success" size="small">全部</el-tag>
        <el-tag v-else type="info" size="small">本人</el-tag>
      </template>
      <template #actions="{ row }">
        <el-button link type="primary" size="small" @click="openEdit(row)">编辑</el-button>
        <el-button link type="danger" size="small" @click="onDelete(row)">删除</el-button>
      </template>
    </CommonDataTable>

    <!-- 编辑弹窗 -->
    <CommonDialog v-model="dialogVisible" :title="isEdit ? '编辑角色' : '新增角色'" width="620px">
      <el-form :model="form" label-width="90px">
        <el-form-item label="角色名称" required>
          <el-input v-model="form.name" placeholder="如：短信操作员" maxlength="50" />
        </el-form-item>
        <el-form-item label="角色编码" required>
          <el-input v-model="form.code" placeholder="唯一编码，如 sms-operator" maxlength="50" />
        </el-form-item>
        <el-form-item label="描述">
          <el-input v-model="form.description" placeholder="可选" maxlength="200" />
        </el-form-item>
        <el-form-item label="启用">
          <el-switch v-model="form.enabled" />
        </el-form-item>
        <el-form-item label="管理员">
          <el-checkbox v-model="form.isAdmin">
            管理员（仅影响数据范围，菜单权限按下方配置）
          </el-checkbox>
        </el-form-item>
        <el-form-item label="数据范围">
          <el-select v-model="form.dataScope" style="width: 200px">
            <el-option :value="0" label="本人" />
            <el-option :value="1" label="全部" />
          </el-select>
        </el-form-item>
        <el-form-item label="可见菜单">
          <div class="menu-tree-wrap">
            <el-tree
              ref="treeRef"
              :data="menuTree"
              show-checkbox
              node-key="id"
              :props="{ label: 'title', children: 'children' }"
              default-expand-all
            />
          </div>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" @click="submitForm">保存</el-button>
      </template>
    </CommonDialog>
  </div>
</template>

<style scoped>
.role-page {
  display: flex;
  flex-direction: column;
  height: 100%;
  overflow: hidden;
}
.hint {
  font-size: 13px;
  color: #6b7280;
}
.menu-tree-wrap {
  width: 100%;
  max-height: 280px;
  overflow: auto;
  border: 1px solid var(--el-border-color);
  border-radius: 4px;
  padding: 6px 10px;
}
</style>

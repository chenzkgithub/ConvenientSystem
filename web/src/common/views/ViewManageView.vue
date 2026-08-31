<script setup lang="ts">
/**
 * 视图管理：维护视图注册表及其权限点。
 * 左侧为视图列表，右侧为选中视图的权限点列表。
 */
import { onMounted, ref, computed } from 'vue'
import { ElMessage } from 'element-plus'
import { Plus, Refresh, Edit, Delete, Search } from '@element-plus/icons-vue'
import {
  getViews,
  saveView,
  deleteView,
  savePermission,
  deletePermission,
  type ViewDto,
  type ViewSaveDto,
  type ViewPermissionDto,
  type ViewPermissionSaveDto,
} from '@/common/api/view'
import CommonDialog from '@/common/components/CommonDialog.vue'

const loading = ref(false)
const views = ref<ViewDto[]>([])
const selectedViewId = ref<number | null>(null)

const selectedView = computed(() => views.value.find((v) => v.id === selectedViewId.value) ?? null)

/** 搜索关键词 */
const searchKey = ref('')

/** 按标题/名称筛选后的视图列表 */
const filteredViews = computed(() => {
  const key = searchKey.value.trim().toLowerCase()
  if (!key) return views.value
  return views.value.filter(v =>
    v.title.toLowerCase().includes(key) || v.name.toLowerCase().includes(key)
  )
})

async function load() {
  loading.value = true
  try {
    views.value = await getViews()
    // 保持选中
    if (selectedViewId.value && !views.value.find((v) => v.id === selectedViewId.value)) {
      selectedViewId.value = views.value.length > 0 ? views.value[0].id : null
    }
  } catch { /* 错误已由 request.ts 弹出提示 */ } finally {
    loading.value = false
  }
}

onMounted(load)

// ========== 视图编辑弹窗 ==========
const viewDialogVisible = ref(false)
const viewDialogIsEdit = ref(false)
const viewForm = ref<ViewSaveDto>({
  id: 0, name: '', title: '', component: '', routePath: '', description: '', enabled: true,
})

function openAddView() {
  viewDialogIsEdit.value = false
  viewForm.value = { id: 0, name: '', title: '', component: '', routePath: '', description: '', enabled: true }
  viewDialogVisible.value = true
}

function openEditView(v: ViewDto) {
  viewDialogIsEdit.value = true
  viewForm.value = {
    id: v.id, name: v.name, title: v.title,
    component: v.component ?? '', routePath: v.routePath ?? '',
    description: v.description ?? '', enabled: v.enabled,
  }
  viewDialogVisible.value = true
}

async function confirmSaveView() {
  const res = await saveView(viewForm.value)
  if (res.ok) {
    ElMessage.success(viewDialogIsEdit.value ? '视图已更新' : '视图已创建')
    viewDialogVisible.value = false
    if (!viewDialogIsEdit.value && selectedViewId.value === null) {
      // 新建后自动选中（reload 后取最后一个）
    }
    await load()
  } else {
    ElMessage.error(res.msg || '保存失败')
  }
}

async function handleDeleteView(v: ViewDto) {
  try {
    const { ElMessageBox } = await import('element-plus')
    await ElMessageBox.confirm(
      `确定删除视图「${v.title}」及其 ${v.permissions.length} 个权限点吗？\n已授权的角色/用户将丢失对应按钮权限。`,
      '确认删除',
      { confirmButtonText: '删除', cancelButtonText: '取消', type: 'warning', confirmButtonClass: 'el-button--danger' },
    )
  } catch { return }
  await deleteView(v.id)
  ElMessage.success('已删除')
  if (selectedViewId.value === v.id) selectedViewId.value = null
  await load()
}

// ========== 权限点编辑弹窗 ==========
const permDialogVisible = ref(false)
const permDialogIsEdit = ref(false)
const permForm = ref<ViewPermissionSaveDto>({ id: 0, viewId: 0, name: '', title: '' })

function openAddPerm() {
  if (!selectedView.value) return
  permDialogIsEdit.value = false
  // 自动生成权限码建议
  const suggestedName = selectedView.value.name ? `${selectedView.value.name}:` : ''
  permForm.value = { id: 0, viewId: selectedView.value.id, name: suggestedName, title: '' }
  permDialogVisible.value = true
}

function openEditPerm(p: ViewPermissionDto) {
  if (!selectedView.value) return
  permDialogIsEdit.value = true
  permForm.value = { id: p.id, viewId: selectedView.value.id, name: p.name, title: p.title }
  permDialogVisible.value = true
}

async function confirmSavePerm() {
  const res = await savePermission(permForm.value)
  if (res.ok) {
    ElMessage.success(permDialogIsEdit.value ? '权限点已更新' : '权限点已添加')
    permDialogVisible.value = false
    await load()
  } else {
    ElMessage.error(res.msg || '保存失败')
  }
}

async function handleDeletePerm(p: ViewPermissionDto) {
  try {
    const { ElMessageBox } = await import('element-plus')
    await ElMessageBox.confirm(`确定删除权限点「${p.title}」(${p.name}) 吗？`, '确认删除', {
      confirmButtonText: '删除', cancelButtonText: '取消', type: 'warning', confirmButtonClass: 'el-button--danger',
    })
  } catch { return }
  await deletePermission(p.id)
  ElMessage.success('已删除')
  await load()
}
</script>

<template>
  <div class="view-manage" v-loading="loading">
    <!-- 页头 -->
    <div class="page-header">
      <div class="header-title">
        <h2>视图管理</h2>
        <span class="header-desc">维护页面视图定义与按钮级权限点，菜单仅引用视图而不存储权限</span>
      </div>
      <div class="header-actions">
        <el-button :icon="Refresh" @click="load">刷新</el-button>
        <el-button v-if="$has('view-manage')" type="primary" :icon="Plus" @click="openAddView">新增视图</el-button>
      </div>
    </div>

    <div class="main-content">
      <!-- 左侧：视图列表 -->
      <div class="view-list">
        <div class="view-search">
          <el-input v-model="searchKey" :prefix-icon="Search" placeholder="搜索视图" clearable size="small" />
        </div>
        <div v-if="filteredViews.length === 0" class="empty-hint">
          <el-empty :description="searchKey ? '未找到匹配的视图' : '暂无视图，点击上方按钮添加'" :image-size="60" />
        </div>
        <div
          v-for="v in filteredViews"
          :key="v.id"
          class="view-item"
          :class="{ active: selectedViewId === v.id }"
          @click="selectedViewId = v.id"
        >
          <div class="view-item-header">
            <span class="view-title">{{ v.title }}</span>
            <span class="view-badge">{{ v.permissions.length }}</span>
          </div>
          <div class="view-name">{{ v.name }}</div>
          <div class="view-meta">
            <span v-if="v.component" class="view-component">{{ v.component }}</span>
            <span v-if="!v.enabled" class="view-disabled">已停用</span>
          </div>
          <div class="view-actions">
            <el-button v-if="$has('view-manage')" size="small" type="primary" link :icon="Edit" @click.stop="openEditView(v)">编辑</el-button>
            <el-button v-if="$has('view-manage')" size="small" type="danger" link :icon="Delete" @click.stop="handleDeleteView(v)">删除</el-button>
          </div>
        </div>
      </div>

      <!-- 右侧：权限点列表 -->
      <div class="perm-panel">
        <template v-if="selectedView">
          <div class="perm-header">
            <div>
              <span class="perm-view-title">{{ selectedView.title }}</span>
              <span class="perm-view-name">{{ selectedView.name }}</span>
            </div>
            <el-button v-if="$has('view-manage')" type="primary" size="small" :icon="Plus" @click="openAddPerm">新增权限点</el-button>
          </div>

          <div v-if="selectedView.permissions.length === 0" class="perm-empty">
            <el-empty description="暂无权限点，点击上方按钮添加" :image-size="50" />
          </div>
          <div v-else class="perm-list">
            <div v-for="p in selectedView.permissions" :key="p.id" class="perm-item">
              <div class="perm-info">
                <span class="perm-title">{{ p.title }}</span>
                <span class="perm-code">{{ p.name }}</span>
              </div>
              <div class="perm-actions">
                <el-button v-if="$has('view-manage')" size="small" type="primary" link :icon="Edit" @click="openEditPerm(p)">编辑</el-button>
                <el-button v-if="$has('view-manage')" size="small" type="danger" link :icon="Delete" @click="handleDeletePerm(p)">删除</el-button>
              </div>
            </div>
          </div>
        </template>
        <div v-else class="empty-hint">
          <el-empty description="请在左侧选择一个视图" :image-size="80" />
        </div>
      </div>
    </div>

    <!-- 视图编辑弹窗 -->
    <CommonDialog v-model="viewDialogVisible" :title="viewDialogIsEdit ? '编辑视图' : '新增视图'" width="480px" :close-on-click-modal="false">
      <el-form :model="viewForm" label-width="90px">
        <el-form-item label="权限码">
          <el-input v-model="viewForm.name" placeholder="如 user-manage（唯一标识）" />
          <div class="form-hint">用于菜单引用和权限检查，创建后不建议修改</div>
        </el-form-item>
        <el-form-item label="显示名称">
          <el-input v-model="viewForm.title" placeholder="如 用户管理" />
        </el-form-item>
        <el-form-item label="路由地址">
          <el-input v-model="viewForm.routePath" placeholder="如 /user-manage" />
        </el-form-item>
        <el-form-item label="组件路径">
          <el-input v-model="viewForm.component" placeholder="如 /src/common/views/UserManageView.vue" />
          <div class="form-hint">菜单编辑中选择此视图后会自动填入</div>
        </el-form-item>
        <el-form-item label="说明">
          <el-input v-model="viewForm.description" type="textarea" :rows="2" placeholder="可选" />
        </el-form-item>
        <el-form-item label="启用状态">
          <el-switch v-model="viewForm.enabled" inline-prompt active-text="启用" inactive-text="停用" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="viewDialogVisible = false">取消</el-button>
        <el-button type="primary" @click="confirmSaveView">确定</el-button>
      </template>
    </CommonDialog>

    <!-- 权限点编辑弹窗 -->
    <CommonDialog v-model="permDialogVisible" :title="permDialogIsEdit ? '编辑权限点' : '新增权限点'" width="440px" :close-on-click-modal="false">
      <el-form :model="permForm" label-width="90px">
        <el-form-item label="权限码">
          <el-input v-model="permForm.name" placeholder="如 user-manage:add" />
          <div class="form-hint">建议格式：视图Name:动作（如 add/edit/delete）</div>
        </el-form-item>
        <el-form-item label="显示名称">
          <el-input v-model="permForm.title" placeholder="如 新增用户" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="permDialogVisible = false">取消</el-button>
        <el-button type="primary" @click="confirmSavePerm">确定</el-button>
      </template>
    </CommonDialog>
  </div>
</template>

<style scoped>
.view-manage {
  padding: 20px;
  height: 100%;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.page-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 16px;
  flex-shrink: 0;
}
.header-title h2 { margin: 0; font-size: 18px; font-weight: 600; }
.header-desc { font-size: 12px; color: var(--el-text-color-secondary); margin-top: 2px; display: block; }

.main-content {
  flex: 1;
  display: flex;
  gap: 16px;
  overflow: hidden;
}

/* ===== 左侧视图列表 ===== */
.view-list {
  width: 320px;
  flex-shrink: 0;
  overflow-y: auto;
  border: 1px solid var(--el-border-color-light);
  border-radius: 8px;
  padding: 8px;
}
.view-search {
  margin-bottom: 8px;
}

.view-item {
  padding: 12px;
  border-radius: 6px;
  cursor: pointer;
  border: 1px solid transparent;
  transition: all 0.15s;
  margin-bottom: 4px;
}
.view-item:hover { background: var(--el-fill-color-light); }
.view-item.active {
  background: var(--el-color-primary-light-9);
  border-color: var(--el-color-primary-light-5);
}

.view-item-header { display: flex; align-items: center; justify-content: space-between; }
.view-title { font-weight: 500; font-size: 14px; }
.view-badge {
  font-size: 11px; background: var(--el-color-info-light-7); color: var(--el-color-info);
  padding: 1px 6px; border-radius: 10px; font-weight: 500;
}
.view-name { font-size: 12px; color: var(--el-text-color-secondary); margin-top: 2px; font-family: monospace; }
.view-meta { display: flex; gap: 8px; margin-top: 4px; font-size: 11px; }
.view-component { color: var(--el-text-color-placeholder); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.view-disabled { color: var(--el-color-danger); font-weight: 500; }
.view-actions { margin-top: 6px; display: flex; gap: 4px; }

/* ===== 右侧权限点面板 ===== */
.perm-panel {
  flex: 1;
  border: 1px solid var(--el-border-color-light);
  border-radius: 8px;
  padding: 16px;
  overflow-y: auto;
  display: flex;
  flex-direction: column;
}

.perm-header {
  display: flex; align-items: center; justify-content: space-between;
  margin-bottom: 16px; flex-shrink: 0;
}
.perm-view-title { font-weight: 600; font-size: 15px; margin-right: 8px; }
.perm-view-name { font-size: 12px; color: var(--el-text-color-secondary); font-family: monospace; }

.perm-empty { flex: 1; display: flex; align-items: center; justify-content: center; }

.perm-list { display: flex; flex-direction: column; gap: 4px; }
.perm-item {
  display: flex; align-items: center; justify-content: space-between;
  padding: 10px 14px; border-radius: 6px;
  border: 1px solid var(--el-border-color-lighter);
  transition: background 0.15s;
}
.perm-item:hover { background: var(--el-fill-color-lighter); }
.perm-info { display: flex; align-items: center; gap: 12px; }
.perm-title { font-size: 14px; font-weight: 500; }
.perm-code { font-size: 12px; color: var(--el-text-color-secondary); font-family: monospace;
  background: var(--el-fill-color); padding: 2px 6px; border-radius: 4px; }
.perm-actions { display: flex; gap: 2px; }

.empty-hint { flex: 1; display: flex; align-items: center; justify-content: center; }
.form-hint { font-size: 12px; color: var(--el-text-color-placeholder); margin-top: 2px; }
</style>

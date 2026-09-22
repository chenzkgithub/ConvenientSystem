<script setup lang="ts">
/**
 * 外部公开页面管理页面
 * - 表格展示所有公开页面配置（免登录 public=1 可访问的页面）
 * - 新增/编辑/删除/启停
 * - 访问链接列可一键复制完整 URL
 */
import { onMounted, reactive, ref, computed, watch } from 'vue'
import { ElMessage } from 'element-plus'
import { httpGet, httpPost, httpPut, httpDelete } from '@/api/request'
import { viewComponentOptions, getViewEnv } from '@/common/viewComponents'
import { confirmAndRun } from '@/common/utils/confirm'
import CommonDataTable, { type DataTableColumn } from '@/common/components/CommonDataTable.vue'
import CommonDialog from '@/common/components/CommonDialog.vue'
import { CopyDocument, Link } from '@element-plus/icons-vue'

interface SysPublicPageItem {
  id: number
  pageKey: string
  title: string
  component: string
  description: string | null
  enabled: boolean
  sortOrder: number
}

const loading = ref(false)
const list = ref<SysPublicPageItem[]>([])

/** 构造访问链接：
 *  只带 public=1（免登录公开上下文，不发 JWT），它本身已隐含无主框架渲染，
 *  无需再拼 standalone=1（那个只给需登录的内部独立窗口用）。 */
function buildAccessUrl(pageKey: string): string {
  const origin = window.location.origin
  const path = pageKey.startsWith('/') ? pageKey.slice(1) : pageKey
  return `${origin}/#/${path}?public=1`
}

/** 在新窗口打开访问链接 */
function openAccessUrl(pageKey: string) {
  window.open(buildAccessUrl(pageKey), '_blank', 'noopener,noreferrer')
}

/** 复制链接到剪贴板 */
async function copyUrl(url: string) {
  try {
    await navigator.clipboard.writeText(url)
    ElMessage.success('链接已复制')
  } catch {
    ElMessage.warning('复制失败，请手动选择复制')
  }
}

const columns: DataTableColumn<SysPublicPageItem>[] = [
  { prop: 'title', label: '名称', minWidth: 120, sortable: true },
  { prop: 'pageKey', label: '路由路径', width: 200, sortable: true },
  { prop: 'component', label: '组件', minWidth: 200, showOverflowTooltip: true, sortable: true },
  { prop: 'enabled', label: '启用', width: 80, custom: true, sortable: true },
  { prop: 'sortOrder', label: '排序', width: 70, align: 'center', sortable: true },
  { prop: 'pageKey', label: '访问链接', minWidth: 280, custom: true, sortable: true },
  { prop: 'actions', label: '操作', width: 140, fixed: 'right', custom: true },
]

async function loadData() {
  loading.value = true
  try {
    list.value = await httpGet<SysPublicPageItem[]>('/api/Common/SysPublicPage/GetAll')
  } catch { /* httpGet 已弹错误提示 */ } finally {
    loading.value = false
  }
}

// ========== 新增/编辑弹窗 ==========
const dialogVisible = ref(false)
const isEdit = ref(false)
const saving = ref(false)
const pageKeyEdited = ref(false) // 用户是否手动编辑过路由路径
const form = reactive({
  id: 0,
  pageKey: '',
  title: '',
  component: '',
  description: '',
  enabled: true,
  sortOrder: 0,
})

/** 根据组件路径自动生成路由路径：
 * /src/common/views/LotteryTrendView.vue → /out-lottery-trend
 * 提取文件名去 View 后缀，PascalCase 转 kebab-case，统一加 out- 前缀 */
function generatePageKey(component: string): string {
  const fileName = component.split('/').pop() || ''
  const name = fileName.replace(/\.vue$/i, '').replace(/View$/i, '')
  if (!name) return ''
  const kebab = name.replace(/([a-z])([A-Z])/g, '$1-$2').toLowerCase()
  return '/out-' + kebab
}

// 组件选择后自动填充路由路径（仅新增且用户未手动编辑时）
watch(() => form.component, (val) => {
  if (!isEdit.value && !pageKeyEdited.value) {
    form.pageKey = generatePageKey(val)
  }
})

/** 组件下拉按运行环境分组（通用/桌面端专属），空组不展示。
 *  公开页只跑 Web 端，桌面端专属组件依赖 /api/local 本地接口，公开访客上下文不可用。 */
const groupedComponentOptions = computed(() => {
  const groups = [
    { label: '通用组件', env: 'both' as const },
    { label: '桌面端专属（公开页不可用）', env: 'desktop' as const },
  ]
  return groups
    .map((g) => ({ ...g, options: viewComponentOptions.filter((o) => o.env === g.env) }))
    .filter((g) => g.options.length > 0)
})

/** 已选组件的运行环境（桌面端专属时提示公开页不可用） */
const selectedComponentEnv = computed(() => getViewEnv(form.component))

function resetForm() {
  form.id = 0
  form.pageKey = ''
  form.title = ''
  form.component = ''
  form.description = ''
  form.enabled = true
  form.sortOrder = 0
  pageKeyEdited.value = false
}

function openCreate() {
  isEdit.value = false
  resetForm()
  dialogVisible.value = true
}

function openEdit(row: SysPublicPageItem) {
  isEdit.value = true
  pageKeyEdited.value = true // 编辑模式不自动覆盖
  form.id = row.id
  form.pageKey = row.pageKey
  form.title = row.title
  form.component = row.component
  form.description = row.description || ''
  form.enabled = row.enabled
  form.sortOrder = row.sortOrder
  dialogVisible.value = true
}

async function saveForm() {
  if (!form.title.trim()) {
    ElMessage.warning('请输入名称')
    return
  }
  if (!form.pageKey.trim()) {
    ElMessage.warning('请输入路由路径')
    return
  }
  if (!form.component.trim()) {
    ElMessage.warning('请选择组件')
    return
  }

  saving.value = true
  try {
    if (isEdit.value) {
      await httpPut('/api/Common/SysPublicPage/Update', { ...form })
      ElMessage.success('更新成功')
    } else {
      await httpPost('/api/Common/SysPublicPage/Create', { ...form })
      ElMessage.success('创建成功')
    }
    dialogVisible.value = false
    loadData()
  } catch { /* httpPost/httpPut 已弹错误提示 */ } finally {
    saving.value = false
  }
}

// ========== 删除 ==========
async function handleDelete(row: SysPublicPageItem) {
  const ok = await confirmAndRun(
    `确认删除「${row.title}」？删除后该页面将无法免登录访问。`,
    () => httpDelete(`/api/Common/SysPublicPage/Delete?id=${row.id}`),
    { title: '删除确认', confirmButtonText: '删除', successText: '删除成功' }
  )
  if (ok) loadData()
}

onMounted(loadData)
</script>

<template>
  <div class="public-page-container">
    <!-- 表格 -->
    <CommonDataTable
      show-refresh
      show-column-toggle
      table-key="sys-public-page"
      :columns="columns"
      :data="list"
      :loading="loading"
      empty-text="暂无公开页面配置"
      @load="loadData"
    >
      <!-- 工具栏按钮 -->
      <template #toolbar>
        <el-button v-if="$has('sys-public-page:create')" type="primary" @click="openCreate">新增公开页面</el-button>
      </template>
      <!-- 启用状态 -->
      <template #cell-enabled="{ row }">
        <el-tag :type="row.enabled ? 'success' : 'info'" size="small" effect="light">
          {{ row.enabled ? '启用' : '停用' }}
        </el-tag>
      </template>

      <!-- 访问链接 -->
      <template #cell-pageKey="{ row }">
        <div class="url-cell">
          <span class="url-text" :title="buildAccessUrl(row.pageKey)">{{ buildAccessUrl(row.pageKey) }}</span>
          <el-button
            link
            type="primary"
            size="small"
            :icon="Link"
            title="打开"
            @click="openAccessUrl(row.pageKey)"
          />
          <el-button
            link
            type="primary"
            size="small"
            :icon="CopyDocument"
            title="复制"
            @click="copyUrl(buildAccessUrl(row.pageKey))"
          />
        </div>
      </template>

      <!-- 操作列 -->
      <template #cell-actions="{ row }">
        <el-button v-if="$has('sys-public-page:edit')" link type="primary" size="small" @click="openEdit(row as SysPublicPageItem)">编辑</el-button>
        <el-button v-if="$has('sys-public-page:delete')" link type="danger" size="small" @click="handleDelete(row as SysPublicPageItem)">删除</el-button>
      </template>
    </CommonDataTable>

    <!-- 新增/编辑弹窗 -->
    <CommonDialog
      v-model="dialogVisible"
      :title="isEdit ? '编辑公开页面' : '新增公开页面'"
      width="520px"
      :close-on-click-modal="false"
    >
      <el-form :model="form" label-width="90px" label-position="right">
        <el-form-item label="名称">
          <el-input v-model="form.title" placeholder="如：走势图" />
        </el-form-item>
        <el-form-item label="路由路径">
          <el-input
            v-model="form.pageKey"
            placeholder="选择组件后自动生成，可手动修改"
            @input="pageKeyEdited = true"
          />
        </el-form-item>
        <el-form-item label="组件">
          <el-select
            v-model="form.component"
            placeholder="选择 Vue 组件"
            filterable
            clearable
            style="width: 100%"
          >
            <el-option-group
              v-for="group in groupedComponentOptions"
              :key="group.label"
              :label="group.label"
            >
              <el-option
                v-for="opt in group.options"
                :key="opt.value"
                :label="opt.label"
                :value="opt.value"
              />
            </el-option-group>
          </el-select>
          <div v-if="selectedComponentEnv === 'desktop'" class="form-hint env-warning">
            该组件依赖桌面端本地接口，公开页仅 Web 端可访问，将导致页面不可用
          </div>
        </el-form-item>
        <el-form-item label="描述">
          <el-input
            v-model="form.description"
            type="textarea"
            :rows="2"
            placeholder="页面用途说明（选填）"
          />
        </el-form-item>
        <el-form-item label="排序">
          <el-input-number v-model="form.sortOrder" :min="0" controls-position="right" />
        </el-form-item>
        <el-form-item label="启用">
          <el-switch v-model="form.enabled" active-text="启用" inactive-text="停用" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="saveForm">保存</el-button>
      </template>
    </CommonDialog>
  </div>
</template>

<style scoped>
.public-page-container {
  display: flex;
  flex-direction: column;
  height: 100%;
  overflow: hidden;
}

.url-cell {
  display: flex;
  align-items: center;
  gap: 4px;
}

.url-text {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

/* 表单提示 */
.form-hint {
  font-size: 12px;
  color: var(--text-sub, #64748b);
  margin-top: 4px;
  line-height: 1.5;
}

/* 桌面端专属组件的警告提示 */
.env-warning {
  color: var(--el-color-warning, #e6a23c);
}
</style>

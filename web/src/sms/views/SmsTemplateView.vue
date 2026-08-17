<script setup lang="ts">
import { reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import {
  listTemplates,
  createTemplate,
  updateTemplate,
  deleteTemplate,
  toggleTemplateEnabled,
  previewTemplate,
  extractVariables,
} from '@/sms/api/sms'
import type { SmsTemplateDto } from '@/sms/types'
import { formatCreator } from '@/common/formatCreator'
import { useDataTable } from '@/common/composables/useDataTable'
import { confirmDelete } from '@/common/utils/confirm'
import CommonDataTable, { type DataTableColumn } from '@/common/components/CommonDataTable.vue'
import CommonDialog from '@/common/components/CommonDialog.vue'

// ========== 列表与筛选 ==========
const filters = reactive({
  category: '',
  keyword: '',
})

const { loading, list, total, load, search, reset } = useDataTable<SmsTemplateDto, typeof filters>(
  listTemplates,
  { filters, paged: false }
)

const columns: DataTableColumn<SmsTemplateDto>[] = [
  { prop: 'name', label: '模板名称', minWidth: 160 },
  { prop: 'category', label: '分类', width: 100 },
  { prop: 'signature', label: '签名', width: 80 },
  { prop: 'enabled', label: '状态', width: 80, custom: true },
  { prop: 'content', label: '模板内容', minWidth: 260 },
  { prop: 'createTime', label: '创建时间', width: 170, type: 'date' },
  { prop: 'creatorName', label: '创建人', width: 150, formatter: (row) => formatCreator(row) },
]

// ========== 新增/编辑弹层 ==========
const dialogVisible = ref(false)
const isEdit = ref(false)
const formRef = ref()
const form = ref<SmsTemplateDto>({
  id: 0,
  name: '',
  content: '',
  signature: 'zk',
  category: '通知',
  enabled: true,
})

const rules = {
  name: [{ required: true, message: '请输入模板名称', trigger: 'blur' }],
  content: [{ required: true, message: '请输入模板内容', trigger: 'blur' }],
}

function openCreate() {
  isEdit.value = false
  form.value = { id: 0, name: '', content: '', signature: 'zk', category: '通知', enabled: true }
  dialogVisible.value = true
}

function openEdit(row: SmsTemplateDto) {
  isEdit.value = true
  form.value = { ...row }
  dialogVisible.value = true
}

async function submitForm() {
  try {
    await formRef.value?.validate()
  } catch {
    return
  }
  try {
    if (isEdit.value) {
      await updateTemplate(form.value)
      ElMessage.success('模板已更新')
    } else {
      await createTemplate(form.value)
      ElMessage.success('模板已创建')
    }
    dialogVisible.value = false
    await load()
  } catch { /* 错误已由 request.ts 弹出提示 */ }
}

async function handleDelete(row: SmsTemplateDto) {
  const ok = await confirmDelete(row.name, () => deleteTemplate(row.id))
  if (ok) await load()
}

async function handleToggle(row: SmsTemplateDto) {
  try {
    const res = await toggleTemplateEnabled(row.id)
    row.enabled = res.enabled
    ElMessage.success(res.enabled ? '已启用' : '已禁用')
  } catch { /* 错误已由 request.ts 弹出提示 */ }
}

// ========== 预览 ==========
const previewVisible = ref(false)
const previewContent = ref('')

async function openPreview(row: SmsTemplateDto) {
  try {
    const vars = await extractVariables(row.content)
    const varMap: Record<string, string> = {}
    vars.forEach((v) => (varMap[v] = `[${v}]`))
    const res = await previewTemplate(row.content, varMap)
    previewContent.value = res.rendered
    previewVisible.value = true
  } catch { /* 错误已由 request.ts 弹出提示 */ }
}
</script>

<template>
  <div class="sms-template-page">
    <CommonDataTable
      :columns="columns"
      :data="list"
      :loading="loading"
      :total="total"
      :show-pagination="false"
      :actions-width="240"
      empty-text="暂无模板数据"
      searchable
      @load="load"
      @search="search"
      @reset="reset"
    >
      <template #filters>
        <el-input v-model="filters.keyword" placeholder="搜索模板名称" clearable style="width: 200px" @clear="search" @keyup.enter="search" />
        <el-select v-model="filters.category" placeholder="全部分类" clearable style="width: 140px" @change="search">
          <el-option label="通知" value="通知" />
          <el-option label="营销" value="营销" />
          <el-option label="验证码" value="验证码" />
        </el-select>
      </template>
      <template #toolbar>
        <el-button type="success" @click="openCreate">+ 新建模板</el-button>
      </template>

      <template #cell-enabled="{ row }">
        <el-tag :type="row.enabled ? 'success' : 'info'" size="small">{{ row.enabled ? '启用' : '禁用' }}</el-tag>
      </template>

      <template #actions="{ row }">
        <el-button link type="primary" @click="openEdit(row as SmsTemplateDto)">编辑</el-button>
        <el-button link type="primary" @click="openPreview(row as SmsTemplateDto)">预览</el-button>
        <el-button link :type="row.enabled ? 'warning' : 'success'" @click="handleToggle(row as SmsTemplateDto)">
          {{ row.enabled ? '禁用' : '启用' }}
        </el-button>
        <el-button link type="danger" @click="handleDelete(row as SmsTemplateDto)">删除</el-button>
      </template>
    </CommonDataTable>

    <!-- 新增/编辑弹层 -->
    <CommonDialog v-model="dialogVisible" :title="isEdit ? '编辑模板' : '新建模板'" width="600px">
      <el-form ref="formRef" :model="form" :rules="rules" label-width="80px">
        <el-form-item label="名称" prop="name">
          <el-input v-model="form.name" placeholder="如：入职欢迎通知" />
        </el-form-item>
        <el-form-item label="分类" prop="category">
          <el-select v-model="form.category" style="width: 100%">
            <el-option label="通知" value="通知" />
            <el-option label="营销" value="营销" />
            <el-option label="验证码" value="验证码" />
          </el-select>
        </el-form-item>
        <el-form-item label="签名" prop="signature">
          <el-input v-model="form.signature" placeholder="短信签名" />
        </el-form-item>
        <el-form-item label="内容" prop="content">
          <el-input v-model="form.content" type="textarea" :rows="5" placeholder="支持变量：{姓名}、{公司} 等" />
        </el-form-item>
        <el-form-item label="启用">
          <el-switch v-model="form.enabled" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" @click="submitForm">保存</el-button>
      </template>
    </CommonDialog>

    <!-- 预览弹层 -->
    <CommonDialog v-model="previewVisible" title="模板预览" width="500px">
      <div class="preview-content">{{ previewContent }}</div>
      <template #footer>
        <el-button type="primary" @click="previewVisible = false">关闭</el-button>
      </template>
    </CommonDialog>
  </div>
</template>

<style scoped>
.sms-template-page {
  display: flex;
  flex-direction: column;
  height: 100%;
  overflow: hidden;
}
.preview-content {
  background: #f5f7fa;
  border-radius: 8px;
  padding: 16px;
  font-size: 14px;
  line-height: 1.8;
  white-space: pre-wrap;
  word-break: break-all;
}
</style>

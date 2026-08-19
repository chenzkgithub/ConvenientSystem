<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import {
  listSmsConfigs,
  getRegisteredProviders,
  saveSmsConfig,
  deleteSmsConfig,
  testSendSms,
  listTemplates
} from '@/sms/api/sms'
import type { SmsProviderConfigDto, SmsTemplateDto } from '@/sms/types'
import { confirmDelete } from '@/common/utils/confirm'
import CommonDataTable, { type DataTableColumn } from '@/common/components/CommonDataTable.vue'
import CommonDialog from '@/common/components/CommonDialog.vue'

const loading = ref(false)
const list = ref<SmsProviderConfigDto[]>([])
const providerTypes = ref<string[]>(['aliyun', 'ihuyi'])
const templates = ref<SmsTemplateDto[]>([])

const columns: DataTableColumn<SmsProviderConfigDto>[] = [
  { prop: 'name', label: '名称', minWidth: 140, sortable: true },
  { prop: 'providerType', label: '类型', width: 100, custom: true, sortable: true },
  { prop: 'defaultSignature', label: '签名', width: 100, sortable: true },
  { prop: 'templateName', label: '关联模板', minWidth: 140, sortable: true },
  { prop: 'isDefault', label: '默认', width: 80, custom: true, sortable: true },
  { prop: 'enabled', label: '启用', width: 80, custom: true, sortable: true },
  { prop: 'createTime', label: '创建时间', width: 170, type: 'date', sortable: true },
]

async function loadData() {
  loading.value = true
  try {
    const [rows, types, tpls] = await Promise.all([
      listSmsConfigs(),
      getRegisteredProviders(),
      listTemplates()
    ])
    list.value = rows
    if (types && types.length) providerTypes.value = types
    templates.value = tpls || []
  } catch { /* 错误已由 request.ts 弹出提示 */ } finally {
    loading.value = false
  }
}

// ========== 编辑弹窗 ==========
const dialogVisible = ref(false)
const isEdit = ref(false)
const form = reactive<SmsProviderConfigDto>({
  id: 0,
  name: '',
  providerType: 'aliyun',
  accessKeyId: '',
  accessKeySecret: '',
  defaultSignature: 'zk',
  templateCode: '',
  templateId: null,
  isDefault: false,
  enabled: true
})

function openCreate() {
  isEdit.value = false
  Object.assign(form, {
    id: 0,
    name: '',
    providerType: 'aliyun',
    accessKeyId: '',
    accessKeySecret: '',
    defaultSignature: 'zk',
    templateCode: '',
    templateId: null,
    isDefault: false,
    enabled: true
  })
  dialogVisible.value = true
}

function openEdit(row: any) {
  isEdit.value = true
  const cfg = row as SmsProviderConfigDto
  Object.assign(form, {
    ...cfg,
    accessKeySecret: cfg.accessKeySecret ?? ''
  })
  dialogVisible.value = true
}

function validateForm(): boolean {
  if (!form.name.trim()) {
    ElMessage.warning('请填写配置名称')
    return false
  }
  if (!form.providerType) {
    ElMessage.warning('请选择服务商类型')
    return false
  }
  return true
}

async function submitForm() {
  if (!validateForm()) return
  try {
    const payload = { ...form, createTime: undefined, updateTime: undefined }
    await saveSmsConfig(payload)
    ElMessage.success('保存成功')
    dialogVisible.value = false
    await loadData()
  } catch { /* 错误已由 request.ts 弹出提示 */ }
}

async function onDelete(row: any) {
  const ok = await confirmDelete(row.name, () => deleteSmsConfig(row.id))
  if (ok) await loadData()
}

// ========== 测试发送弹窗 ==========
const testVisible = ref(false)
const testForm = reactive({ phone: '', content: '这是一条测试短信', signature: 'zk' })
const testing = ref(false)

function openTest() {
  testForm.phone = ''
  testForm.content = '这是一条测试短信'
  // 用默认配置的签名
  const def = list.value.find(c => c.isDefault)
  testForm.signature = def?.defaultSignature || 'zk'
  testVisible.value = true
}

async function doTest() {
  if (!testForm.phone.trim()) {
    ElMessage.warning('请填写手机号')
    return
  }
  testing.value = true
  try {
    const res = await testSendSms(testForm)
    if (res.success) ElMessage.success(`发送成功（${res.costMs}ms）`)
    else ElMessage.error('发送失败：' + (res.errorMessage || '未知错误'))
  } catch { /* 错误已由 request.ts 弹出提示 */ } finally {
    testing.value = false
  }
}

onMounted(loadData)
</script>

<template>
  <div class="sms-config-page">
    <CommonDataTable
      show-refresh
      show-column-toggle
      table-key="sms-config"
      @load="loadData"
      :columns="columns"
      :data="list"
      :loading="loading"
      :total="list.length"
      :show-pagination="false"
      :actions-width="160"
      empty-text="暂无短信配置"
    >
      <template #filters>
        <span class="hint">配置短信服务商凭证，标记为默认的配置用于系统通知联动推送。</span>
      </template>
      <template #toolbar>
        <el-button v-if="$has('sms-config:test-send')" @click="openTest">测试发送</el-button>
        <el-button v-if="$has('sms-config:create')" type="primary" @click="openCreate">新增配置</el-button>
      </template>

      <template #cell-providerType="{ row }">
        <el-tag size="small">{{ row.providerType === 'aliyun' ? '阿里云' : '互亿无线' }}</el-tag>
      </template>
      <template #cell-isDefault="{ row }">
        <el-tag v-if="row.isDefault" type="success" size="small">默认</el-tag>
        <span v-else class="muted">-</span>
      </template>
      <template #cell-enabled="{ row }">
        <el-tag :type="row.enabled ? 'success' : 'info'" size="small">
          {{ row.enabled ? '启用' : '停用' }}
        </el-tag>
      </template>

      <template #actions="{ row }">
        <el-button v-if="$has('sms-config:edit')" link type="primary" size="small" @click="openEdit(row)">编辑</el-button>
        <el-button v-if="$has('sms-config:delete')" link type="danger" size="small" @click="onDelete(row)">删除</el-button>
      </template>
    </CommonDataTable>

    <!-- 编辑弹窗 -->
    <CommonDialog v-model="dialogVisible" :title="isEdit ? '编辑短信配置' : '新增短信配置'" width="600px">
      <el-form :model="form" label-width="120px">
        <el-form-item label="配置名称" required>
          <el-input v-model="form.name" placeholder="如：阿里云通知" maxlength="100" />
        </el-form-item>

        <el-form-item label="服务商类型" required>
          <el-select v-model="form.providerType" style="width: 100%">
            <el-option v-for="t in providerTypes" :key="t" :label="t === 'aliyun' ? '阿里云' : '互亿无线'" :value="t" />
          </el-select>
        </el-form-item>

        <el-form-item label="AccessKeyId" required>
          <el-input v-model="form.accessKeyId" placeholder="服务商密钥 ID" />
        </el-form-item>

        <el-form-item label="AccessKeySecret" required>
          <el-input
            v-model="form.accessKeySecret"
            type="password"
            show-password
            placeholder="服务商密钥 Secret"
          />
        </el-form-item>

        <el-form-item label="默认签名">
          <el-input v-model="form.defaultSignature" placeholder="如 zk" maxlength="50" />
        </el-form-item>

        <el-form-item label="模板 Code">
          <el-input v-model="form.templateCode" placeholder="阿里云模板编号（互亿无线不需要）" />
        </el-form-item>

        <el-form-item label="关联模板">
          <el-select
            v-model="form.templateId"
            clearable
            placeholder="选择本地短信模板（用于测试发送）"
            style="width: 100%"
          >
            <el-option
              v-for="t in templates"
              :key="t.id"
              :label="t.name"
              :value="t.id"
            />
          </el-select>
        </el-form-item>

        <el-form-item label="默认配置">
          <el-switch v-model="form.isDefault" />
          <div style="font-size: 12px; color: #9ca3af; margin-top: 8px;">
            标记为默认后，系统通知联动短信推送使用此配置
          </div>
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

    <!-- 测试发送弹窗 -->
    <CommonDialog v-model="testVisible" title="测试发送短信" width="480px">
      <el-form :model="testForm" label-width="80px">
        <el-form-item label="手机号" required>
          <el-input v-model="testForm.phone" placeholder="接收测试短信的手机号" />
        </el-form-item>
        <el-form-item label="签名">
          <el-input v-model="testForm.signature" placeholder="短信签名" />
        </el-form-item>
        <el-form-item label="内容" required>
          <el-input
            v-model="testForm.content"
            type="textarea"
            :rows="3"
            placeholder="短信内容"
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="testVisible = false">关闭</el-button>
        <el-button type="primary" :loading="testing" @click="doTest">发送</el-button>
      </template>
    </CommonDialog>
  </div>
</template>

<style scoped>
.sms-config-page {
  display: flex;
  flex-direction: column;
  height: 100%;
  overflow: hidden;
}

.hint {
  font-size: 13px;
  color: #6b7280;
}

.muted {
  color: #9ca3af;
  font-size: 13px;
}
</style>

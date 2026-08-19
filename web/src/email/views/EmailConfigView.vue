<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import {
  listEmailConfigs,
  saveEmailConfig,
  deleteEmailConfig,
  testSendEmail
} from '@/email/api/email'
import type { EmailConfigDto } from '@/email/types'
import { confirmDelete } from '@/common/utils/confirm'
import CommonDataTable, { type DataTableColumn } from '@/common/components/CommonDataTable.vue'
import CommonDialog from '@/common/components/CommonDialog.vue'

const loading = ref(false)
const list = ref<EmailConfigDto[]>([])

const columns: DataTableColumn<EmailConfigDto>[] = [
  { prop: 'name', label: '名称', minWidth: 140, sortable: true },
  { prop: 'smtpServer', label: 'SMTP 服务器', minWidth: 160, sortable: true },
  { prop: 'account', label: '发件邮箱', minWidth: 160, sortable: true },
  { prop: 'fromName', label: '显示名', width: 120, sortable: true },
  { prop: 'enableSsl', label: 'SSL', width: 80, custom: true, sortable: true },
  { prop: 'isDefault', label: '默认', width: 80, custom: true, sortable: true },
  { prop: 'enabled', label: '启用', width: 80, custom: true, sortable: true },
  { prop: 'createTime', label: '创建时间', width: 170, type: 'date', sortable: true },
]

async function loadData() {
  loading.value = true
  try {
    list.value = await listEmailConfigs()
  } catch { /* 错误已由 request.ts 弹出提示 */ } finally {
    loading.value = false
  }
}

// ========== 编辑弹窗 ==========
const dialogVisible = ref(false)
const isEdit = ref(false)
const form = reactive<EmailConfigDto>({
  id: 0,
  name: '',
  smtpServer: 'smtp.qq.com',
  smtpPort: 587,
  account: '',
  password: '',
  fromName: '系统通知',
  enableSsl: true,
  isDefault: false,
  enabled: true,
})

function openCreate() {
  isEdit.value = false
  Object.assign(form, {
    id: 0,
    name: '',
    smtpServer: 'smtp.qq.com',
    smtpPort: 587,
    account: '',
    password: '',
    fromName: '系统通知',
    enableSsl: true,
    isDefault: false,
    enabled: true,
  })
  dialogVisible.value = true
}

function openEdit(row: any) {
  isEdit.value = true
  const cfg = row as EmailConfigDto
  Object.assign(form, { ...cfg })
  dialogVisible.value = true
}

// 常用邮箱快捷选择
function quickFill(provider: string) {
  const presets: Record<string, { smtpServer: string; smtpPort: number }> = {
    qq: { smtpServer: 'smtp.qq.com', smtpPort: 587 },
    '163': { smtpServer: 'smtp.163.com', smtpPort: 587 },
    outlook: { smtpServer: 'smtp.office365.com', smtpPort: 587 },
    gmail: { smtpServer: 'smtp.gmail.com', smtpPort: 587 },
    wecom: { smtpServer: 'smtp.exmail.qq.com', smtpPort: 587 },
    icloud: { smtpServer: 'smtp.mail.me.com', smtpPort: 587 },
  }
  const p = presets[provider]
  if (p) {
    form.smtpServer = p.smtpServer
    form.smtpPort = p.smtpPort
  }
}

function validateForm(): boolean {
  if (!form.name.trim()) {
    ElMessage.warning('请填写配置名称')
    return false
  }
  if (!form.smtpServer.trim()) {
    ElMessage.warning('请填写 SMTP 服务器')
    return false
  }
  if (!form.account.trim()) {
    ElMessage.warning('请填写发件人邮箱')
    return false
  }
  if (form.smtpPort <= 0 || form.smtpPort > 65535) {
    ElMessage.warning('端口号无效')
    return false
  }
  return true
}

async function submitForm() {
  if (!validateForm()) return
  try {
    const payload = { ...form, createTime: undefined, updateTime: undefined }
    await saveEmailConfig(payload)
    ElMessage.success('保存成功')
    dialogVisible.value = false
    await loadData()
  } catch { /* 错误已由 request.ts 弹出提示 */ }
}

async function onDelete(row: any) {
  const ok = await confirmDelete(row.name, () => deleteEmailConfig(row.id))
  if (ok) await loadData()
}

// ========== 测试发送弹窗 ==========
const testVisible = ref(false)
const testForm = reactive({ recipients: '', subject: '测试邮件', content: '这是一封测试邮件，来自 ConvenientSystem 邮件通知模块。' })
const testing = ref(false)
const testResult = ref<{ success: boolean; errorMessage?: string; costMs: number } | null>(null)

function openTest() {
  testForm.recipients = ''
  testForm.subject = '测试邮件'
  testForm.content = '这是一封测试邮件，来自 ConvenientSystem 邮件通知模块。'
  testResult.value = null
  testVisible.value = true
}

async function doTestSend() {
  if (!testForm.recipients.trim()) {
    ElMessage.warning('请填写收件人')
    return
  }
  testing.value = true
  testResult.value = null
  try {
    const res = await testSendEmail(testForm)
    testResult.value = res
    if (res.success) {
      ElMessage.success(`测试邮件发送成功（${res.costMs}ms）`)
    } else {
      ElMessage.error('测试发送失败：' + (res.errorMessage ?? '未知错误'))
    }
  } catch {
    /* 错误已由 request.ts 弹出提示 */
  } finally {
    testing.value = false
  }
}

onMounted(loadData)
</script>

<template>
  <div class="email-config-page">
    <CommonDataTable
      show-refresh
      show-column-toggle
      table-key="email-config"
      @load="loadData"
      :columns="columns"
      :data="list"
      :loading="loading"
      :total="list.length"
      :show-pagination="false"
      :actions-width="160"
      empty-text="暂无邮件配置"
    >
      <template #filters>
        <span class="hint">配置邮件 SMTP 凭证，标记为默认的配置用于系统通知联动推送。</span>
      </template>
      <template #toolbar>
        <el-button v-if="$has('email-config:test-send')" @click="openTest">测试发送</el-button>
        <el-button v-if="$has('email-config:create')" type="primary" @click="openCreate">新增配置</el-button>
      </template>

      <template #cell-enableSsl="{ row }">
        <el-tag :type="row.enableSsl ? 'success' : 'info'" size="small">
          {{ row.enableSsl ? '是' : '否' }}
        </el-tag>
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
        <el-button v-if="$has('email-config:edit')" link type="primary" size="small" @click="openEdit(row)">编辑</el-button>
        <el-button v-if="$has('email-config:delete')" link type="danger" size="small" @click="onDelete(row)">删除</el-button>
      </template>
    </CommonDataTable>

    <!-- 编辑弹窗 -->
    <CommonDialog v-model="dialogVisible" :title="isEdit ? '编辑邮件配置' : '新增邮件配置'" width="600px">
      <el-form :model="form" label-width="120px">
        <el-form-item label="配置名称" required>
          <el-input v-model="form.name" placeholder="如：QQ 邮箱通知" maxlength="100" />
        </el-form-item>

        <el-form-item label="常用邮箱">
          <div class="quick-fill">
            <el-button size="small" @click="quickFill('qq')">QQ 邮箱</el-button>
            <el-button size="small" @click="quickFill('163')">163 邮箱</el-button>
            <el-button size="small" @click="quickFill('outlook')">Outlook</el-button>
            <el-button size="small" @click="quickFill('gmail')">Gmail</el-button>
            <el-button size="small" @click="quickFill('wecom')">企业微信</el-button>
            <el-button size="small" @click="quickFill('icloud')">iCloud</el-button>
          </div>
        </el-form-item>

        <el-form-item label="SMTP 服务器" required>
          <el-input v-model="form.smtpServer" placeholder="如 smtp.qq.com" />
        </el-form-item>

        <el-form-item label="端口" required>
          <el-input-number v-model="form.smtpPort" :min="1" :max="65535" style="width: 100%" />
        </el-form-item>

        <el-form-item label="发件人邮箱" required>
          <el-input v-model="form.account" placeholder="如 123456@qq.com" />
        </el-form-item>

        <el-form-item label="授权码 / 密码" required>
          <el-input
            v-model="form.password"
            type="password"
            show-password
            placeholder="邮箱授权码（非登录密码）"
          />
          <div style="font-size: 12px; color: #9ca3af; margin-top: 4px;">
            QQ 邮箱：mail.qq.com → 设置 → 账户 → POP3/SMTP → 开启 → 获取授权码
          </div>
        </el-form-item>

        <el-form-item label="发件人名称">
          <el-input v-model="form.fromName" placeholder="收件人看到的发件人名称" />
        </el-form-item>

        <el-form-item label="SSL">
          <el-switch v-model="form.enableSsl" />
        </el-form-item>

        <el-form-item label="默认配置">
          <el-switch v-model="form.isDefault" />
          <div style="font-size: 12px; color: #9ca3af; margin-top: 8px;">
            标记为默认后，系统通知联动邮件推送使用此配置
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
    <CommonDialog v-model="testVisible" title="测试发送邮件" width="500px">
      <el-form :model="testForm" label-width="80px">
        <el-form-item label="收件人" required>
          <el-input v-model="testForm.recipients" placeholder="填写你的邮箱地址，多个用分号分隔" />
        </el-form-item>
        <el-form-item label="主题" required>
          <el-input v-model="testForm.subject" placeholder="测试邮件主题" />
        </el-form-item>
        <el-form-item label="内容" required>
          <el-input
            v-model="testForm.content"
            type="textarea"
            :rows="4"
            placeholder="测试邮件内容"
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="testVisible = false">关闭</el-button>
        <el-button type="primary" :loading="testing" @click="doTestSend">发送</el-button>
      </template>
    </CommonDialog>
  </div>
</template>

<style scoped>
.email-config-page {
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

.quick-fill {
  display: flex;
  gap: 6px;
  flex-wrap: wrap;
}
</style>

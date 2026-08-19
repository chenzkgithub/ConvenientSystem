<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import {
  listWebhooks,
  getProviderTypes,
  saveWebhook,
  deleteWebhook,
  testWebhook
} from '@/notify/api/notify'
import { PROVIDER_LABELS, type WebhookConfigDto } from '@/notify/types'
import { confirmDelete } from '@/common/utils/confirm'
import CommonDataTable, { type DataTableColumn } from '@/common/components/CommonDataTable.vue'
import CommonDialog from '@/common/components/CommonDialog.vue'

const loading = ref(false)
const list = ref<WebhookConfigDto[]>([])
const providerTypes = ref<string[]>(['dingtalk', 'wecom', 'feishu'])

const columns: DataTableColumn<WebhookConfigDto>[] = [
  { prop: 'name', label: '名称', minWidth: 140, sortable: true },
  { prop: 'providerType', label: '类型', width: 110, custom: true, sortable: true },
  { prop: 'mode', label: '模式', width: 130, custom: true, sortable: true },
  { prop: 'recipientIds', label: '接收者', minWidth: 180, custom: true, sortable: true },
  { prop: 'isDefault', label: '默认', width: 80, custom: true, sortable: true },
  { prop: 'enabled', label: '启用', width: 90, custom: true, sortable: true },
  { prop: 'createTime', label: '创建时间', width: 170, type: 'date', sortable: true },
  { prop: 'updateTime', label: '更新时间', width: 170, type: 'date', sortable: true },
]

function providerLabel(t: string) {
  return PROVIDER_LABELS[t] || t
}

async function loadData() {
  loading.value = true
  try {
    const [rows, types] = await Promise.all([listWebhooks(), getProviderTypes()])
    list.value = rows
    if (types && types.length) providerTypes.value = types
  } catch { /* 错误已由 request.ts 弹出提示 */ } finally {
    loading.value = false
  }
}

// ========== 编辑弹窗 ==========
const dialogVisible = ref(false)
const isEdit = ref(false)
const form = reactive<WebhookConfigDto>({
  id: 0,
  name: '',
  providerType: 'dingtalk',
  webhookUrl: '',
  secret: '',
  appKey: '',
  appSecret: '',
  recipientIds: '',
  enableGroup: true,
  enablePrivate: false,
  useCard: false,
  isDefault: false,
  enabled: true,
  createTime: '',
  updateTime: ''
})

function openCreate() {
  isEdit.value = false
  Object.assign(form, {
    id: 0,
    name: '',
    providerType: 'dingtalk',
    webhookUrl: '',
    secret: '',
    appKey: '',
    appSecret: '',
    recipientIds: '',
    enableGroup: true,
    enablePrivate: false,
    useCard: false,
    isDefault: false,
    enabled: true,
    createTime: '',
    updateTime: ''
  })
  dialogVisible.value = true
}

function openEdit(row: any) {
  isEdit.value = true
  const cfg = row as WebhookConfigDto
  Object.assign(form, {
    ...cfg,
    secret: cfg.secret ?? '',
    appSecret: cfg.appSecret ?? '',
    recipientIds: cfg.recipientIds ?? ''
  })
  dialogVisible.value = true
}

// 校验：至少启用一种模式
function validateForm(): boolean {
  if (!form.name.trim()) {
    ElMessage.warning('请填写名称')
    return false
  }
  if (!form.enableGroup && !form.enablePrivate) {
    ElMessage.warning('至少启用一种发送模式（群或私聊）')
    return false
  }
  if (form.enableGroup && !form.webhookUrl?.trim()) {
    ElMessage.warning('群模式：请填写 Webhook 地址')
    return false
  }
  if (form.enablePrivate) {
    if (!form.appKey?.trim()) {
      ElMessage.warning('私聊模式：请填写 AppKey')
      return false
    }
    if (!form.appSecret?.trim()) {
      ElMessage.warning('私聊模式：请填写 AppSecret')
      return false
    }
    if (!form.recipientIds?.trim()) {
      ElMessage.warning('私聊模式：请填写接收者列表')
      return false
    }
  }
  return true
}

async function submitForm() {
  if (!validateForm()) return
  try {
    // createTime/updateTime 是只读返回字段，保存时不传（空字符串会导致后端 DateTime 反序列化失败 → 400）
    const payload = { ...form, createTime: undefined, updateTime: undefined }
    await saveWebhook(payload)
    ElMessage.success('保存成功')
    dialogVisible.value = false
    await loadData()
  } catch { /* 错误已由 request.ts 弹出提示 */ }
}

async function onDelete(row: any) {
  const ok = await confirmDelete(row.name, () => deleteWebhook(row.id))
  if (ok) await loadData()
}

const testingId = ref(0)
async function onTest(row: any) {
  testingId.value = row.id
  try {
    const res = await testWebhook(row.id)
    if (res.success) ElMessage.success(`发送成功（${res.costMs}ms）`)
    else ElMessage.error('发送失败：' + (res.errorMessage || '未知错误'))
  } catch { /* 错误已由 request.ts 弹出提示 */ } finally {
    testingId.value = 0
  }
}

onMounted(loadData)
</script>

<template>
  <div class="webhook-page">
    <CommonDataTable
      show-refresh
      show-column-toggle
      table-key="webhook-config"
      :columns="columns"
      :data="list"
      :loading="loading"
      :total="list.length"
      :show-pagination="false"
      :actions-width="200"
      @load="loadData"
      empty-text="暂无机器人配置"
    >
      <template #filters>
        <span class="hint">配置群机器人或私聊机器人，任务执行失败时自动推送通知。</span>
      </template>
      <template #toolbar>
        <el-button v-if="$has('webhook-config:create')" type="primary" @click="openCreate">新增机器人</el-button>
      </template>

      <template #cell-providerType="{ row }">
        <el-tag size="small">{{ providerLabel(row.providerType) }}</el-tag>
      </template>
      <template #cell-mode="{ row }">
        <div style="font-size: 12px; line-height: 1.6;">
          <el-tag v-if="row.enableGroup" type="success" size="small">群机器人</el-tag>
          <el-tag v-if="row.enablePrivate" type="warning" size="small">私聊机器人</el-tag>
        </div>
      </template>
      <template #cell-recipientIds="{ row }">
        <span v-if="row.enablePrivate && row.recipientIds" class="recipient-hint">{{
          row.recipientIds
        }}</span>
        <span v-else class="muted">-</span>
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
        <el-button
          v-if="$has('webhook-config:test-send')"
          link
          type="primary"
          size="small"
          :loading="testingId === row.id"
          @click="onTest(row)"
        >
          测试
        </el-button>
        <el-button v-if="$has('webhook-config:edit')" link type="primary" size="small" @click="openEdit(row)">编辑</el-button>
        <el-button v-if="$has('webhook-config:delete')" link type="danger" size="small" @click="onDelete(row)">删除</el-button>
      </template>
    </CommonDataTable>

    <!-- 编辑弹窗 -->
    <CommonDialog v-model="dialogVisible" :title="isEdit ? '编辑机器人' : '新增机器人'" width="640px">
      <el-form :model="form" label-width="120px">
        <el-form-item label="名称" required>
          <el-input v-model="form.name" placeholder="如：运维告警群" maxlength="100" />
        </el-form-item>

        <el-form-item label="类型" required>
          <el-select v-model="form.providerType" style="width: 100%">
            <el-option v-for="t in providerTypes" :key="t" :label="providerLabel(t)" :value="t" />
          </el-select>
          <div style="font-size: 12px; color: #9ca3af; margin-top: 8px;">
            <div v-if="form.providerType === 'dingtalk'">
              📌 钉钉群机器人官方文档：https://open.dingtalk.com/document/robots/custom-robot-access
            </div>
            <div v-else-if="form.providerType === 'wecom'">
              📌 企业微信应用文档：https://developer.work.weixin.qq.com/document/path/90236
            </div>
            <div v-else-if="form.providerType === 'feishu'">
              📌 飞书应用文档：https://open.feishu.cn/document/server-docs/im-v1/message/create
            </div>
          </div>
        </el-form-item>

        <!-- 群机器人配置 -->
        <el-card class="config-card" title="群机器人配置">
          <template #header>
            <div class="card-header">
              <span>群机器人配置</span>
              <el-checkbox v-model="form.enableGroup" @change="() => {}" />
            </div>
          </template>
          <div v-if="form.enableGroup" style="display: flex; flex-direction: column; gap: 12px;">
            <el-form-item label="Webhook 地址">
              <el-input
                v-model="form.webhookUrl"
                type="textarea"
                :rows="2"
                placeholder="完整 Webhook URL，如 https://oapi.dingtalk.com/robot/send?access_token=..."
              />
            </el-form-item>
            <el-form-item label="加签密钥">
              <el-input
                v-model="form.secret"
                type="password"
                show-password
                placeholder="可选。钉钉加签密钥"
              />
            </el-form-item>
          </div>
          <div v-else style="color: #9ca3af; font-size: 13px;">已禁用</div>
        </el-card>

        <!-- 私聊机器人配置 -->
        <el-card class="config-card" title="私聊机器人配置">
          <template #header>
            <div class="card-header">
              <span>私聊机器人配置</span>
              <el-checkbox v-model="form.enablePrivate" @change="() => {}" />
            </div>
          </template>
          <div v-if="form.enablePrivate" style="display: flex; flex-direction: column; gap: 12px;">
            <el-form-item label="AppKey">
              <el-input
                v-model="form.appKey"
                placeholder="钉钉开发平台应用 AppKey"
                maxlength="100"
              />
            </el-form-item>
            <el-form-item label="AppSecret">
              <el-input
                v-model="form.appSecret"
                type="password"
                show-password
                placeholder="钉钉开发平台应用 AppSecret"
              />
            </el-form-item>
            <el-form-item label="接收者列表">
              <el-input
                v-model="form.recipientIds"
                type="textarea"
                :rows="3"
                placeholder='支持两种格式：&#10;1. JSON: ["uid1","uid2","uid3"]&#10;2. 逗号分隔: uid1,uid2,uid3'
              />
              <div style="font-size: 12px; color: #9ca3af; margin-top: 8px;">
                提示：填写钉钉用户 UID 或邮箱地址，多个用逗号/分号分隔或 JSON 数组格式
              </div>
            </el-form-item>
          </div>
          <div v-else style="color: #9ca3af; font-size: 13px;">已禁用</div>
        </el-card>

        <!-- 消息类型 -->
        <el-form-item label="消息类型">
          <el-radio-group v-model="form.useCard">
            <el-radio :label="false">纯文本</el-radio>
            <el-radio :label="true">富文本卡片</el-radio>
          </el-radio-group>
          <div style="font-size: 12px; color: #9ca3af; margin-top: 8px;">
            💡 纯文本：快速简单 | 富文本卡片：Markdown 格式（标题、列表等），仅群机器人生效
          </div>
        </el-form-item>

        <el-form-item label="默认机器人">
          <el-switch v-model="form.isDefault" />
          <div style="font-size: 12px; color: #9ca3af; margin-top: 8px;">
            标记为默认后，通知联动推送、邮件任务推送、任务失败告警均发给此机器人
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
  </div>
</template>

<style scoped>
.webhook-page {
  display: flex;
  flex-direction: column;
  height: 100%;
  overflow: hidden;
}

.hint {
  font-size: 13px;
  color: #6b7280;
}

.config-card {
  margin-bottom: 12px;
}

.card-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  width: 100%;
}

.muted {
  color: #9ca3af;
  font-size: 13px;
}

.recipient-hint {
  word-break: break-all;
}
</style>

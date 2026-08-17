import { httpGet, httpPost } from '@/api/request'
import type { WebhookConfigDto, WebhookSendResultDto, WebhookLogDto } from '@/notify/types'

/** 获取全部机器人配置 */
export function listWebhooks() {
  return httpGet<WebhookConfigDto[]>('/api/Common/WebhookConfig/List')
}

/** 获取已注册的服务商类型 */
export function getProviderTypes() {
  return httpGet<string[]>('/api/Common/WebhookConfig/GetProviderTypes')
}

/** 新增或更新配置 */
export function saveWebhook(dto: WebhookConfigDto) {
  return httpPost<void>('/api/Common/WebhookConfig/Save', dto)
}

/** 删除配置 */
export function deleteWebhook(id: number) {
  return httpPost<void>('/api/Common/WebhookConfig/Delete', id)
}

/** 测试发送 */
export function testWebhook(id: number) {
  return httpPost<WebhookSendResultDto>('/api/Common/WebhookConfig/Test', { id })
}

// ========== 发送日志 ==========
export function listWebhookLogs(params?: {
  configName?: string
  success?: boolean
  page?: number
  size?: number
}) {
  return httpGet<{ total: number; list: WebhookLogDto[] }>('/api/Common/WebhookLog/List', params)
}

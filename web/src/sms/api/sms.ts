import { httpGet, httpPost } from '@/api/request'
import type {
  SmsTemplateDto,
  SmsLogDto,
  SmsQuotaDto,
  SmsStatisticsDto,
  SmsProviderConfigDto,
  SmsTestSendRequest
} from '@/sms/types'

// ========== 模板 ==========
export function listTemplates(params?: { category?: string; keyword?: string }) {
  return httpGet<SmsTemplateDto[]>('/api/Sms/SmsTemplate/List', params)
}

export function getTemplate(id: number) {
  return httpGet<SmsTemplateDto>('/api/Sms/SmsTemplate/Get', { id })
}

export function createTemplate(dto: SmsTemplateDto) {
  return httpPost<SmsTemplateDto>('/api/Sms/SmsTemplate/Create', dto)
}

export function updateTemplate(dto: SmsTemplateDto) {
  return httpPost<void>('/api/Sms/SmsTemplate/Update', dto)
}

export function deleteTemplate(id: number) {
  return httpPost<void>(`/api/Sms/SmsTemplate/Delete?id=${id}`)
}

export function toggleTemplateEnabled(id: number) {
  return httpPost<{ enabled: boolean }>(`/api/Sms/SmsTemplate/ToggleEnabled?id=${id}`)
}

export function previewTemplate(content: string, variables: Record<string, string>) {
  return httpPost<{ rendered: string }>('/api/Sms/SmsTemplate/Preview', { content, variables })
}

export function extractVariables(content: string) {
  return httpPost<string[]>(`/api/Sms/SmsTemplate/ExtractVariables?content=${encodeURIComponent(content)}`)
}

// ========== 日志 ==========
export function listLogs(params?: {
  taskId?: number
  phone?: string
  status?: number
  startTime?: string
  endTime?: string
  page?: number
  size?: number
}) {
  return httpGet<{ total: number; list: SmsLogDto[] }>('/api/Sms/SmsLog/List', params)
}

export function getStatistics() {
  return httpGet<SmsStatisticsDto>('/api/Sms/SmsLog/Statistics')
}

/** 按日发送趋势（days：往前天数，含今天） */
export function getSmsTrend(days: number) {
  return httpGet<import('@/common/types').SendTrend>('/api/Sms/SmsLog/Trend', { days })
}

export function getQuota() {
  return httpGet<SmsQuotaDto>('/api/Sms/SmsLog/Quota')
}

// ========== 配置 ==========
export function listSmsConfigs() {
  return httpGet<SmsProviderConfigDto[]>('/api/Sms/SmsConfig/List')
}

export function getRegisteredProviders() {
  return httpGet<string[]>('/api/Sms/SmsConfig/GetProviders')
}

export function saveSmsConfig(dto: SmsProviderConfigDto) {
  return httpPost<void>('/api/Sms/SmsConfig/Save', dto)
}

export function deleteSmsConfig(id: number) {
  return httpPost<void>(`/api/Sms/SmsConfig/Delete?id=${id}`)
}

export function getQuotaConfig() {
  return httpGet<SmsQuotaDto>('/api/Sms/SmsConfig/GetQuota')
}

export function saveQuotaConfig(dto: SmsQuotaDto) {
  return httpPost<void>('/api/Sms/SmsConfig/SaveQuota', dto)
}

export function testSendSms(req: SmsTestSendRequest) {
  return httpPost<{
    success: boolean
    errorMessage?: string
    providerMsgId?: string
    costMs: number
    provider: string
  }>('/api/Sms/SmsConfig/TestSend', req)
}

import { httpGet, httpPost } from '@/api/request'
import type { EmailConfigDto, EmailTaskDto, EmailLogDto, EmailTestSendRequest } from '@/email/types'

// ========== 配置 ==========
export function listEmailConfigs() {
  return httpGet<EmailConfigDto[]>('/api/Email/EmailConfig/List')
}

export function saveEmailConfig(dto: EmailConfigDto) {
  return httpPost<void>('/api/Email/EmailConfig/Save', dto)
}

export function deleteEmailConfig(id: number) {
  return httpPost<void>('/api/Email/EmailConfig/Delete', id)
}

export function testSendEmail(req: EmailTestSendRequest) {
  return httpPost<{ success: boolean; errorMessage?: string; costMs: number }>(
    '/api/Email/EmailConfig/TestSend',
    req,
  )
}

// ========== 日志（EmailLogController：任务管理已移除，历史日志仍可查询） ==========
export function listEmailTasks() {
  return httpGet<EmailTaskDto[]>('/api/Email/EmailLog/Tasks')
}

export function listEmailLogs(params?: { taskId?: number; page?: number; size?: number }) {
  return httpGet<{ total: number; list: EmailLogDto[] }>('/api/Email/EmailLog/Logs', params)
}

/** 按日发送趋势（days：往前天数，含今天） */
export function getEmailTrend(days: number) {
  return httpGet<import('@/common/types').SendTrend>('/api/Email/EmailLog/Trend', { days })
}

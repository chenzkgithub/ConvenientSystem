import { httpGet } from '@/api/request'
import type { SendTrend } from '@/common/types'

/** 审计日志记录 */
export interface AuditLogDto {
  id: number
  userId?: string | null
  account: string
  action: string
  module: string
  path: string
  method: string
  ip: string
  paramSummary?: string | null
  success: boolean
  statusCode: number
  costMs: number
  createTime: string
}

/** 分页查询审计日志（对应后端 AuditLogController） */
export function listAuditLogs(params?: {
  account?: string
  module?: string
  success?: boolean
  startTime?: string
  endTime?: string
  page?: number
  size?: number
}) {
  return httpGet<{ total: number; list: AuditLogDto[] }>('/api/Common/AuditLog/List', params)
}

/** 审计操作按日趋势（对应后端 AuditLogController Trend） */
export function getAuditTrend(days: number) {
  return httpGet<SendTrend>('/api/Common/AuditLog/Trend', { days })
}

/** 登录活跃按日趋势（对应后端 AuditLogController LoginTrend） */
export function getAuditLoginTrend(days: number) {
  return httpGet<SendTrend>('/api/Common/AuditLog/LoginTrend', { days })
}

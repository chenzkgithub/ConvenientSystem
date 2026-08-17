import { httpGet, httpDelete } from '@/api/request'

/** 错误日志记录 */
export interface ErrorLogDto {
  id: number
  account: string
  path: string
  method: string
  statusCode: number
  exceptionType: string
  errorMessage: string
  stackTrace?: string | null
  ip: string
  createTime: string
}

/** 分页查询错误日志（对应后端 ErrorLogController） */
export function listErrorLogs(params?: {
  keyword?: string
  startTime?: string
  endTime?: string
  page?: number
  size?: number
}) {
  return httpGet<{ total: number; list: ErrorLogDto[] }>('/api/Common/ErrorLog/List', params)
}

/** 清空全部错误日志 */
export function clearErrorLogs() {
  return httpDelete<number>('/api/Common/ErrorLog/Clear')
}

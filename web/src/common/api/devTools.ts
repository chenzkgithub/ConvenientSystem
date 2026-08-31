import { httpGet } from '@/api/request'

/** 雪花ID生成结果（ids 以字符串返回，避免 JS Number 精度丢失） */
export interface SnowflakeIdResult {
  ids: string[]
  count: number
}

/**
 * 生成雪花ID；count 为生成数量（1～1000），默认 1。
 * epoch 为可选的起始纪元日期（如 '2020-01-01'），传入后以该日期为基准生成ID，
 * ID 位数由所选日期决定，不做强制限制。
 */
export function generateSnowflakeIds(count = 1, epoch?: string) {
  const params: Record<string, unknown> = { count }
  if (epoch) params.epoch = epoch
  return httpGet<SnowflakeIdResult>('/api/Common/DevTools/SnowflakeId', params)
}

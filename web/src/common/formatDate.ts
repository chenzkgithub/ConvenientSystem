/**
 * 将日期字符串格式化为 yyyy-MM-dd HH:mm:ss
 * 支持 ISO 格式、带 T 分隔符等常见格式
 */
export function formatDate(value?: string | null): string {
  if (!value) return '-'
  // 处理 ISO 格式（含 T）和空格分隔格式
  const str = value.replace('T', ' ')
  // 取前 19 位：yyyy-MM-dd HH:mm:ss
  if (str.length >= 19) return str.substring(0, 19)
  // 不足 19 位则补零
  const parts = str.split(/[\s\-:]+/)
  const y = parts[0] || ''
  const M = parts[1] || '01'
  const d = parts[2] || '01'
  const h = parts[3] || '00'
  const m = parts[4] || '00'
  const s = parts[5] || '00'
  return `${y}-${M}-${d} ${h}:${m}:${s}`
}

/** 今天日期 yyyy-MM-dd（本地时区；用于筛选日期默认值等） */
export function todayYmd(): string {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

/**
 * 全局错误工具：仅保留网络异常判断；
 * 错误页跳转已移除，所有错误统一由 ElMessage 弹提示。
 */

/** 判断一个 fetch 异常是否属于网络/连接类错误 */
export function isNetworkError(err: unknown): boolean {
  if (!(err instanceof Error)) return false
  const msg = err.message.toLowerCase()
  return (
    msg.includes('fetch') ||
    msg.includes('network') ||
    msg.includes('failed to fetch') ||
    msg.includes('net::') ||
    msg.includes('err_connection') ||
    msg.includes('abort') ||
    err.name === 'AbortError' ||
    err.name === 'TypeError'
  )
}

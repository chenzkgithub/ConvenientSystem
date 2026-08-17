import { ElMessage, ElMessageBox } from 'element-plus'

export interface ConfirmRunOptions {
  /** 确认框标题 */
  title?: string
  /** 确认按钮文案 */
  confirmButtonText?: string
  /** 取消按钮文案 */
  cancelButtonText?: string
  /** 图标类型 */
  type?: 'warning' | 'info' | 'error' | 'success'
  /** 执行成功后的提示文案；传空串表示不提示 */
  successText?: string
}

/**
 * 「先确认、再执行、后提示」的统一封装，收敛列表页里反复手写的
 * ElMessageBox.confirm + try/catch + ElMessage.success 三段式。
 *
 * 返回值语义：true = 已确认且执行成功（调用方据此刷新列表）；
 * false = 用户取消，或执行失败（失败提示由 request.ts 全局处理，此处不重复弹）。
 */
export async function confirmAndRun(
  message: string,
  run: () => Promise<unknown>,
  options: ConfirmRunOptions = {}
): Promise<boolean> {
  const { title = '提示', type = 'warning', successText = '操作成功' } = options

  try {
    await ElMessageBox.confirm(message, title, {
      type,
      // 未指定时交给 Element Plus 用默认文案，避免显式传 undefined 覆盖默认值
      ...(options.confirmButtonText ? { confirmButtonText: options.confirmButtonText } : {}),
      ...(options.cancelButtonText ? { cancelButtonText: options.cancelButtonText } : {}),
    })
  } catch {
    return false // 用户点了取消或关闭确认框
  }

  try {
    await run()
  } catch {
    return false // 错误已由 request.ts 弹出提示
  }

  if (successText) ElMessage.success(successText)
  return true
}

/**
 * 删除确认的语义糖：统一「确定删除「xxx」？」文案与「已删除」成功提示，
 * 避免各列表页各写一套措辞。
 */
export function confirmDelete(
  target: string,
  run: () => Promise<unknown>,
  options: ConfirmRunOptions = {}
): Promise<boolean> {
  return confirmAndRun(`确定删除「${target}」？`, run, { successText: '已删除', ...options })
}

import { localPost } from '@/api/request'

/** 接口归属：local=全部走桌面端本地通道（审计脚本依据，勿删） */
export const API_SIDE = 'local' as const

/**
 * UI 状态持久化：走桌面端本地 API，落盘 exe 目录 ui-state.json
 * （清理 WebView2 缓存、卸载重装均不丢数据）。
 * 请求全部 silent：不弹 loading 遮罩、不弹全局错误提示。
 * 非桌面端环境 localPost 直接抛错（调用方捕获后回退 localStorage）。
 */

/** UI 状态读取结果（key 不存在时 value 为 null） */
export interface UiStateValueResult {
  value: string | null
}

/** 读取指定键的原始字符串值 */
function uiStateGet(key: string) {
  return localPost<UiStateValueResult>(
    `/api/local/ui-state/Get?key=${encodeURIComponent(key)}`,
    {},
    undefined,
    undefined,
    { silent: true },
  )
}

/** 保存（覆盖）指定键的原始字符串值 */
function uiStateSet(key: string, value: string) {
  return localPost('/api/local/ui-state/Set', { key, value }, undefined, undefined, { silent: true })
}

/**
 * 读取 UI 状态：本地 API 优先；API 可达但无数据时把 localStorage 旧数据一次性
 * 迁移到 API 并清除（老用户无感升级，避免两处数据长期不一致）；
 * API 不可达（纯浏览器 web 版）时回退 localStorage 读取。
 */
export async function loadUiStateString(key: string): Promise<string | null> {
  let apiReachable = true
  let apiValue: string | null = null
  try {
    const r = await uiStateGet(key)
    apiValue = r.value ?? null
  } catch {
    apiReachable = false
  }
  if (apiReachable) {
    if (apiValue != null) return apiValue
    // API 无数据：尝试把 localStorage 旧数据迁移上去（老用户首次升级）
    const raw = localStorage.getItem(key)
    if (raw == null) return null
    try {
      await uiStateSet(key, raw)
    } catch {
      return raw // API 突然失败：保留 localStorage，下次打开再迁移
    }
    localStorage.removeItem(key) // 迁移成功，清除旧数据
    return raw
  }
  return localStorage.getItem(key)
}

/**
 * 保存 UI 状态到本地 API；API 不可用时回退写 localStorage
 * （纯浏览器 web 版兜底，回到桌面端后读取时自动迁移回 API）。
 */
export function saveUiStateString(key: string, value: string) {
  uiStateSet(key, value).catch(() => {
    try {
      localStorage.setItem(key, value)
    } catch {
      console.warn(`[uiState] 保存 ${key} 失败（本地 API 与 localStorage 均不可用）`)
    }
  })
}

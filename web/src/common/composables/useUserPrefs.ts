import { useAuthStore } from '@/common/stores/auth'
import { updateMyConfig } from '@/common/api/userConfig'

/** 防抖保存：收集短时间内的多次修改，合并为一次 API 请求 */
let saveTimer: ReturnType<typeof setTimeout> | null = null
const pendingSaves = new Map<string, string>()

function flushSaves() {
  if (pendingSaves.size === 0) return
  const items = Array.from(pendingSaves.entries()).map(([configKey, configValue]) => ({ configKey, configValue }))
  pendingSaves.clear()
  saveTimer = null
  updateMyConfig(items).catch(() => { /* 保存失败静默，下次变更时会重试 */ })
}

/**
 * 用户 UI 偏好读写。
 * - getPref：优先读 auth.uiPrefs（数据库），回退 localStorage，最后用 fallback
 * - setPref：同时写 localStorage（即时生效）+ 防抖 1 秒保存到数据库
 */
export function useUserPrefs() {
  function getPref(key: string, fallback: string): string {
    const auth = useAuthStore()
    // 优先读数据库（登录且已加载 uiPrefs 时生效）
    if (auth.uiPrefs[key] !== undefined) return auth.uiPrefs[key]
    // 回退 localStorage（未登录或 uiPrefs 尚未加载时）
    return localStorage.getItem(key) ?? fallback
  }

  function setPref(key: string, value: string) {
    const auth = useAuthStore()
    // 同步更新内存和 localStorage
    auth.uiPrefs[key] = value
    try { localStorage.setItem(key, value) } catch { /* ignore */ }
    // 防抖保存到数据库
    pendingSaves.set(key, value)
    if (saveTimer) clearTimeout(saveTimer)
    saveTimer = setTimeout(flushSaves, 1000)
  }

  return { getPref, setPref }
}

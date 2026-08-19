import { defineStore } from 'pinia'
import { ref, watch } from 'vue'
import { useUserPrefs } from '@/common/composables/useUserPrefs'

/** 单个标签页：path 为路由 fullPath（含 query），首页固定不可关闭 */
export interface TabItem {
  path: string
  title: string
  closable: boolean
  pinned?: boolean
}

const HOME: TabItem = { path: '/', title: '首页', closable: false }

// 标签栏持久化键：与登录态（auth_state_v1）同用 localStorage，刷新/重启后都恢复上次打开的标签与选中项。
// 固定端口使 origin 一致，localStorage 才能跨刷新/重启保留（见 Desktop/Program.cs 端口注释）。
const PERSIST_KEY = 'tabs_state_v1'
const REMEMBER_KEY = 'UI.RememberTabs'

/**
 * 从 localStorage 恢复标签与选中项。
 * - 首页恒为第一项且不可关闭，无论存储里有没有；
 * - 其余标签一律置为可关闭；
 * - active 必须落在恢复出的标签内，否则回退首页。
 * - rememberTabs 关闭时仅返回首页。
 */
function loadPersisted(): { tabs: TabItem[]; active: string; remember: boolean } {
  const { getPref } = useUserPrefs()
  const remember = getPref(REMEMBER_KEY, 'true') !== 'false'
  if (!remember) return { tabs: [{ ...HOME }], active: '/', remember }
  try {
    const raw = localStorage.getItem(PERSIST_KEY)
    if (raw) {
      const o = JSON.parse(raw)
      if (Array.isArray(o.tabs)) {
        const rest = o.tabs
          .filter((t: unknown): t is TabItem =>
            !!t && typeof (t as TabItem).path === 'string' && (t as TabItem).path !== '/',
          )
          .map((t: TabItem) => ({ path: t.path, title: String(t.title || t.path), closable: true, pinned: !!t.pinned }))
        const tabs = [{ ...HOME }, ...rest]
        const active = typeof o.active === 'string' && tabs.some((t) => t.path === o.active) ? o.active : '/'
        return { tabs, active, remember }
      }
    }
  } catch {
    /* 读取失败时按仅首页处理 */
  }
  return { tabs: [{ ...HOME }], active: '/', remember }
}

/** 标签关闭守卫：返回 false 可阻止关闭（如编辑器有未保存内容时弹窗确认） */
export type TabCloseGuard = () => Promise<boolean> | boolean

/** 顶部多标签页：记录已打开页面，支持打开/激活/关闭，与路由联动 */
export const useTabsStore = defineStore('tabs', () => {
  const persisted = loadPersisted()
  const tabs = ref<TabItem[]>(persisted.tabs)
  const active = ref(persisted.active)
  const rememberTabs = ref(persisted.remember)

  // 标签或选中项变化即写回 localStorage，使刷新/重启后能恢复（仅在开启记忆时）
  function persist() {
    if (!rememberTabs.value) return
    try {
      localStorage.setItem(PERSIST_KEY, JSON.stringify({ tabs: tabs.value, active: active.value }))
    } catch {
      /* 忽略写入失败 */
    }
  }
  watch([tabs, active], persist, { deep: true })

  /** 切换标签记忆开关 */
  function toggleRememberTabs() {
    rememberTabs.value = !rememberTabs.value
    const { setPref } = useUserPrefs()
    setPref(REMEMBER_KEY, String(rememberTabs.value))
    if (!rememberTabs.value) {
      try { localStorage.removeItem(PERSIST_KEY) } catch { /* ignore */ }
    }
  }

  // 记忆关闭时立即清除已保存的标签（防止 watcher 在状态切换间隙重新写入）
  watch(rememberTabs, (val) => {
    if (!val) {
      try { localStorage.removeItem(PERSIST_KEY) } catch { /* ignore */ }
    }
  })

  // 窗口关闭/刷新时保留持久化标签页，下次进入恢复上次打开的标签与选中项；
  // 退出登录时由 reset() 负责清除，避免下一位用户继承上一位的页签

  // 按标签 path 注册的关闭守卫（非响应式，组件卸载时需自行注销）
  const closeGuards = new Map<string, TabCloseGuard>()

  // 标签刷新计数器（递增时触发 keep-alive 组件重新挂载，无需改变路由）
  const refreshCounters = ref<Record<string, number>>({})

  /** 注册标签关闭守卫（同 path 覆盖） */
  function registerCloseGuard(path: string, guard: TabCloseGuard) {
    closeGuards.set(path, guard)
  }

  /** 注销标签关闭守卫 */
  function unregisterCloseGuard(path: string) {
    closeGuards.delete(path)
  }

  /** 关闭前询问守卫：无守卫或守卫放行时返回 true */
  async function canCloseTab(path: string): Promise<boolean> {
    const guard = closeGuards.get(path)
    if (!guard) return true
    try {
      return await guard()
    } catch {
      return true
    }
  }

  /** 打开或激活一个标签（已存在则仅激活，并按最新标题更新） */
  function openTab(path: string, title: string) {
    const exist = tabs.value.find((t) => t.path === path)
    if (exist) {
      if (title) exist.title = title
    } else {
      tabs.value.push({ path, title: title || path, closable: path !== '/', pinned: false })
    }
    active.value = path
  }

  /** 刷新标签：递增计数器使 keep-alive 组件重新挂载（不改变路由） */
  function refreshTab(path: string) {
    refreshCounters.value[path] = (refreshCounters.value[path] || 0) + 1
  }

  /** 切换标签固定状态（固定的标签不可关闭） */
  function togglePin(path: string) {
    const tab = tabs.value.find((t) => t.path === path)
    if (tab && tab.closable) tab.pinned = !tab.pinned
  }

  /** 关闭除指定标签和首页/固定标签外的所有标签 */
  function closeOtherTabs(keepPath: string) {
    tabs.value = tabs.value.filter((t) => !t.closable || t.pinned || t.path === keepPath)
    if (!tabs.value.some((t) => t.path === active.value)) {
      active.value = tabs.value[tabs.value.length - 1]?.path || '/'
    }
  }

  /** 关闭所有可关闭标签（保留首页和固定标签） */
  function closeAllTabs() {
    tabs.value = tabs.value.filter((t) => !t.closable || t.pinned)
    active.value = tabs.value[tabs.value.length - 1]?.path || '/'
  }

  /** 仅更新标签标题，不改变激活状态（供编辑器等根据文件名动态改名） */
  function renameTab(path: string, title: string) {
    const tab = tabs.value.find((t) => t.path === path)
    if (tab && title) tab.title = title
  }

  /**
   * 关闭标签。若关闭的是当前激活标签，返回应跳转到的相邻标签 path；
   * 否则返回 null（无需跳转）。首页和固定标签不可关闭。
   */
  function closeTab(path: string): string | null {
    if (path === '/') return null
    const tab = tabs.value.find((t) => t.path === path)
    if (tab?.pinned) return null
    const idx = tabs.value.findIndex((t) => t.path === path)
    if (idx === -1) return null
    tabs.value.splice(idx, 1)
    if (active.value === path) {
      const next = tabs.value[idx] || tabs.value[idx - 1] || tabs.value[0]
      active.value = next.path
      return next.path
    }
    return null
  }

  /** 重置为仅首页（用于登出等场景），并清除持久化 */
  function reset() {
    tabs.value = [{ ...HOME }]
    active.value = '/'
    try {
      localStorage.removeItem(PERSIST_KEY)
    } catch {
      /* 忽略清除失败 */
    }
  }

  /** 服务器偏好加载后同步：从 auth.uiPrefs 读取标签记忆开关 */
  function applyServerPrefs() {
    const { getPref } = useUserPrefs()
    const serverRemember = getPref(REMEMBER_KEY, 'true') !== 'false'
    if (rememberTabs.value !== serverRemember) {
      rememberTabs.value = serverRemember
      if (!serverRemember) {
        tabs.value = [{ ...HOME }]
        active.value = '/'
        try { localStorage.removeItem(PERSIST_KEY) } catch { /* ignore */ }
      }
    }
  }

  return { tabs, active, rememberTabs, refreshCounters, openTab, renameTab, closeTab, closeOtherTabs, closeAllTabs, togglePin, refreshTab, reset, registerCloseGuard, unregisterCloseGuard, canCloseTab, toggleRememberTabs, applyServerPrefs }
})

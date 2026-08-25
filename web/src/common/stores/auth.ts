import { defineStore } from 'pinia'
import { ref } from 'vue'
import { getLoginDefault, verifyLogin, logoutApi } from '@/common/api/login'
import { resetUnauthorizedHandled } from '@/api/request'
import { getUIPrefs } from '@/common/api/userConfig'
import { useTabsStore } from '@/common/stores/tabs'
import { useMenuStore } from '@/common/stores/menu'
import router from '@/router'

/** 登录状态与账号信息 */
export const useAuthStore = defineStore('auth', () => {
  // 登录态持久化键：使用 localStorage 持久保存，登录态永不过期，
  // 仅点击“退出登录”才会失效需重新登录。
  const PERSIST_KEY = 'auth_state_v1'

  function loadPersisted(): { loggedIn: boolean; currentAccount: string; displayName: string; avatar: string; token: string; menuCodes: string[]; sessionTimeoutMinutes: number } {
    try {
      const raw = localStorage.getItem(PERSIST_KEY)
      if (raw) {
        const o = JSON.parse(raw)
        // 已登录且持有非空 JWT 才视为有效（不再校验过期时间）。
        // 缺少 token 的旧会话（升级到 JWT 前遗留）会被清除并强制重新登录，
        // 否则请求不带 Authorization 头，后端按未登录处理导致菜单为空。
        const tk = typeof o.token === 'string' ? o.token : ''
        if (o.loggedIn && tk) {
          return {
            loggedIn: true,
            currentAccount: typeof o.currentAccount === 'string' ? o.currentAccount : '',
            displayName: typeof o.displayName === 'string' ? o.displayName : '',
            avatar: typeof o.avatar === 'string' ? o.avatar : '',
            token: tk,
            menuCodes: Array.isArray(o.menuCodes) ? o.menuCodes : [],
            sessionTimeoutMinutes: typeof o.sessionTimeoutMinutes === 'number' ? o.sessionTimeoutMinutes : 0,
          }
        }
        localStorage.removeItem(PERSIST_KEY) // 无效状态（未登录或无 token）：清除
      }
    } catch {
      /* 读取失败时按未登录处理 */
    }
    return { loggedIn: false, currentAccount: '', displayName: '', avatar: '', token: '', menuCodes: [], sessionTimeoutMinutes: 0 }
  }

  const persisted = loadPersisted()
  const loggedIn = ref(persisted.loggedIn)
  const defaultAccount = ref('')
  const defaultPassword = ref('')
  const currentAccount = ref(persisted.currentAccount) // 当前登录名（登录成功后记录，用于顶部显示）
  const displayName = ref(persisted.displayName) // 用户显示名称
  const avatar = ref(persisted.avatar) // 头像 data URL（空串表示未设置，顶栏回退显示首字母）
  const token = ref(persisted.token) // JWT 令牌
  const menuCodes = ref<string[]>(persisted.menuCodes) // 可见菜单权限码（仅前端参考）
  const sessionTimeoutMinutes = ref<number>(persisted.sessionTimeoutMinutes) // 会话超时时间（分钟），0 表示不自动退出
  const disabledReason = ref<'account_disabled' | 'api_401' | null>(null) // 登出原因：账号停用 / API 返回 401 / 正常登出(null)
  const uiPrefs = ref<Record<string, string>>({}) // UI 偏好设置（登录后从数据库加载）

  /** 将当前登录态写入 localStorage（永不过期，仅退出登录时清除） */
  function persist() {
    try {
      localStorage.setItem(
        PERSIST_KEY,
        JSON.stringify({
          loggedIn: loggedIn.value,
          currentAccount: currentAccount.value,
          displayName: displayName.value,
          avatar: avatar.value,
          token: token.value,
          menuCodes: menuCodes.value,
          sessionTimeoutMinutes: sessionTimeoutMinutes.value,
        }),
      )
    } catch {
      /* 忽略写入失败 */
    }
  }

  /** 从数据库加载 UI 偏好设置（登录成功后调用） */
  async function loadUIPrefs() {
    try {
      uiPrefs.value = await getUIPrefs()
    } catch {
      /* 加载失败时保持空字典，各组件回退 localStorage */
    }
  }

  /** 清除所有业务缓存，仅保留“记住账号”的登录名 */
  function clearUserCaches() {
    try {
      const rememberAccount = localStorage.getItem('login_remember_account')
      localStorage.clear()
      if (rememberAccount) {
        localStorage.setItem('login_remember_account', rememberAccount)
      }
    } catch {
      /* 忽略清除失败 */
    }
  }

  /** 拉取后端配置的默认账号密码用于回填 */
  async function loadDefault() {
    try {
      const data = await getLoginDefault()
      defaultAccount.value = data.account || ''
      defaultPassword.value = data.password || ''
    } catch (e) {
      console.warn('获取默认账号失败，需手动输入', e)
    }
  }

  /** 校验并登录，返回 { ok, reason } */
  async function login(account: string, password: string): Promise<{ ok: boolean; reason?: string }> {
    const data = await verifyLogin(account, password)
    const previousAccount = currentAccount.value
    loggedIn.value = !!data.ok
    if (loggedIn.value) {
      // 重置 401 去重标志：退出后重新登录时，确保新会话的 401 能正常触发退出流程
      resetUnauthorizedHandled()
      currentAccount.value = account
      displayName.value = data.displayName || ''
      avatar.value = data.avatar || ''
      token.value = data.token || ''
      menuCodes.value = data.menuCodes || []
      sessionTimeoutMinutes.value = data.sessionTimeoutMinutes ?? 30
      // 切换用户时同步清除旧缓存并重置标签页，避免继承上一位用户的页签
      if (previousAccount && previousAccount !== account) {
        clearUserCaches()
      }
      // 先持久化到 localStorage，确保后续 API 请求（loadUIPrefs 等）能读到新 token
      persist()
      useTabsStore().reset()
      // 登录成功后加载服务器 UI 偏好
      await loadUIPrefs()
      // 登录成功后重置路由到首页，避免仍停留在前账号打开的无权限页面
      if (router.currentRoute.value.path !== '/') {
        void router.replace('/')
      }
    }
    persist()
    return { ok: loggedIn.value, reason: data.reason }
  }

  /** 更新本地显示名称（个人资料修改成功后同步顶栏，JWT 内的旧值下次登录才刷新） */
  function setDisplayName(name: string) {
    displayName.value = name
    persist()
  }

  /** 更新本地头像（个人资料修改成功后同步顶栏；空串表示已清除头像） */
  function setAvatar(dataUrl: string) {
    avatar.value = dataUrl
    persist()
  }

  async function logout(reason?: 'account_disabled' | 'api_401') {
    // 通知后端从在线追踪器中移除（不阻塞退出流程，失败静默）
    if (token.value) {
      try { await logoutApi() } catch { /* 忽略网络错误 */ }
    }
    loggedIn.value = false
    currentAccount.value = ''
    displayName.value = ''
    avatar.value = ''
    token.value = ''
    menuCodes.value = []
    sessionTimeoutMinutes.value = 0
    uiPrefs.value = {} // 清除 UI 偏好
    disabledReason.value = reason ?? null
    clearUserCaches()
    // 重置菜单和标签页的加载状态，下次登录时会重新加载
    useMenuStore().reset()
    useTabsStore().reset()
    // 退出登录后回到首页，避免重新登录时仍停留在前账号的页面路径
    if (router.currentRoute.value.path !== '/') {
      void router.replace('/')
    }
  }

  return { loggedIn, defaultAccount, defaultPassword, currentAccount, displayName, avatar, token, menuCodes, sessionTimeoutMinutes, disabledReason, uiPrefs, loadDefault, login, logout, setDisplayName, setAvatar, loadUIPrefs }
})

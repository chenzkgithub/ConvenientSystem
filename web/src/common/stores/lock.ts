import { defineStore } from 'pinia'
import { ref } from 'vue'
import { getAppConfig } from '@/common/api/lock'

const DEFAULT_LOCK_TIMEOUT_MS = 2 * 60 * 1000 // 缺省 2 分钟
const IDLE_EVENTS = ['mousemove', 'keydown', 'click', 'scroll', 'mousedown', 'wheel']
const LOCK_STORAGE_KEY = 'app_locked' // 锁屏状态持久化：刷新页面后恢复

/** 锁屏与空闲计时 */
export const useLockStore = defineStore('lock', () => {
  const isLocked = ref(localStorage.getItem(LOCK_STORAGE_KEY) === 'true')
  const enabled = ref(false) // 登录后才启用空闲计时
  const featureEnabled = ref(false) // 锁屏功能总开关（由后端 AppSettings:EnableLock 控制；初始 false 避免配置加载前闪现按钮）
  let idleTimer: number | undefined
  let lockTimeoutMs = DEFAULT_LOCK_TIMEOUT_MS // 从后端配置读取，缺省 2 分钟
  let hostBound = false // 桥接消息只能绑一次，开关反复切换时不重复绑定

  /**
   * 读取当前登录用户的锁屏配置。必须在登录之后调用：
   * /Lock/AppConfig 需要认证，未登录时请求会 401，拿不到用户的真实开关。
   * 读取失败时沿用上一次已知值，从未读到过则保持关闭，
   * 避免把用户明确关掉的锁屏功能反而打开。
   */
  async function loadConfig() {
    try {
      const cfg = await getAppConfig()
      featureEnabled.value = !!cfg.enableLock
      if (cfg.lockTimeout && cfg.lockTimeout > 0) {
        lockTimeoutMs = cfg.lockTimeout * 1000
      }
      // 开关被关掉时立刻停掉空闲计时；仍开启则按新超时重排。
      // 不做这步的话，在个人配置里改完要等下次登录才生效。
      if (!featureEnabled.value) {
        stopIdle()
        // 功能已关闭：清掉可能从 localStorage 还原出来的历史锁屏状态，
        // 否则会出现「账号没开锁屏功能却一进来就被锁」的情况，
        // 并通知桌面壳一并解除，避免弹出窗口停留在锁屏上。
        if (isLocked.value) {
          isLocked.value = false
          localStorage.removeItem(LOCK_STORAGE_KEY)
          postToHost('host:unlock')
        }
      } else if (enabled.value) {
        resetIdleTimer()
      }
    } catch {
      /* 沿用上一次已知值：首次就失败时 featureEnabled 保持初始的关闭状态 */
    }
  }

  /** 停掉空闲计时并解绑监听（锁屏功能被关闭或退出登录时） */
  function stopIdle() {
    enabled.value = false
    if (idleTimer) { window.clearTimeout(idleTimer); idleTimer = undefined }
    IDLE_EVENTS.forEach((evt) => document.removeEventListener(evt, resetIdleTimer))
  }

  function resetIdleTimer() {
    if (!enabled.value || isLocked.value) return
    if (idleTimer) window.clearTimeout(idleTimer)
    idleTimer = window.setTimeout(lock, lockTimeoutMs)
  }

  /**
   * 把还原出来的锁屏状态补发给桌面壳。
   * isLocked 是从 localStorage 读出来初始化的，这条路径不经过 lock()，
   * 桌面壳的 LockCoordinator 无从得知自己已经处于锁屏，
   * 之后从悬浮菜单/托盘新开的窗口就不会跟着上锁。
   * 必须在 loadConfig 之后调用：功能关闭时 loadConfig 已清掉锁屏状态，
   * 这里再叠一层 featureEnabled 判断，绝不把陈旧锁屏广播给壳。
   */
  function syncHost() {
    if (featureEnabled.value && isLocked.value) postToHost('host:lock')
  }

  /** 向桌面壳发送消息（普通浏览器环境下无桌面壳，静默忽略） */
  function postToHost(type: string) {
    const wv = (
      window as unknown as { chrome?: { webview?: { postMessage?: (m: unknown) => void } } }
    ).chrome?.webview
    if (typeof wv?.postMessage === 'function') wv.postMessage({ type })
  }

  function lock() {
    if (!featureEnabled.value) return // 功能关闭时不锁屏
    if (isLocked.value) return
    isLocked.value = true
    localStorage.setItem(LOCK_STORAGE_KEY, 'true')
    // 通知桌面壳：锁定所有弹出浏览器窗口。
    postToHost('host:lock')
  }

  function unlock() {
    isLocked.value = false
    localStorage.removeItem(LOCK_STORAGE_KEY)
    // 通知桌面壳：同步解锁所有弹出浏览器窗口。
    postToHost('host:unlock')
    resetIdleTimer()
  }

  /** 来自其它窗口（桌面壳）的解锁：仅同步本页状态，不再回发，避免循环 */
  function unlockFromHost() {
    if (!isLocked.value) return
    isLocked.value = false
    localStorage.removeItem(LOCK_STORAGE_KEY)
    resetIdleTimer()
  }

  /** 退出登录时调用：清除锁屏状态与空闲计时 */
  function reset() {
    isLocked.value = false
    stopIdle()
    // 开关与超时一并归位：换账号登录时不能残留上一个用户的配置，
    // 否则新用户的 loadConfig 返回前会短暂套用旧值。
    featureEnabled.value = false
    lockTimeoutMs = DEFAULT_LOCK_TIMEOUT_MS
    localStorage.removeItem(LOCK_STORAGE_KEY)
  }

  /** 登录后调用：启用空闲计时并绑定监听（可重复调用，已启用则忽略） */
  function start() {
    if (!featureEnabled.value) return // 功能关闭时不启用自动锁屏
    if (enabled.value) return // 幂等：避免重复绑定事件监听
    enabled.value = true
    IDLE_EVENTS.forEach((evt) => document.addEventListener(evt, resetIdleTimer))
    // 用户在内嵌第三方页面（原生 WebView2 控件）内的操作不会触发本页面事件，
    // 桌面壳会将其转发为 host:activity 消息，这里据此重置空闲计时，避免使用中被自动锁屏。
    // 这个监听与锁屏开关无关，且无法解绑，因此只绑一次。
    const wv = (window as unknown as { chrome?: { webview?: { addEventListener?: Function } } })
      .chrome?.webview
    if (!hostBound && typeof wv?.addEventListener === 'function') {
      hostBound = true
      wv.addEventListener('message', (ev: { data?: { type?: string } }) => {
        const type = ev?.data?.type
        if (type === 'host:activity') resetIdleTimer()
        // 某个弹出浏览器窗口内完成解锁，桌面壳回发本消息，同步解除本页锁屏。
        else if (type === 'host:unlock') unlockFromHost()
      })
    }
    resetIdleTimer()
  }

  return { isLocked, featureEnabled, lock, unlock, unlockFromHost, reset, start, resetIdleTimer, loadConfig, syncHost }
})

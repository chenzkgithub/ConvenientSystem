// 基于 axios 的统一请求封装：拦截器处理鉴权、loading、错误提示。
// 所有接口同源：WebView2 内为 Kestrel 根（本地模式或反向代理），
// 独立浏览器部署时通过 VITE_API_BASE 指定远程接口基址。
// 所有请求自动带全局 loading：引用计数，并发请求只显示一个遮罩；延迟展示避免快请求闪烁。
// loading 就近遮罩：存在打开的弹窗/抽屉时始终遮罩最上层窗口，否则遮罩触发位置所在页面区域，不覆盖整个程序窗口。
import axios, { AxiosError } from 'axios'
import { ElLoading, ElMessage, useZIndex } from 'element-plus'
import 'element-plus/es/components/loading/style/css'
import 'element-plus/es/components/message/style/css'
import { isNetworkError } from '@/common/utils/error'
import { fullscreenElement, ensurePositioned } from '@/common/utils/fullscreen'
import { IS_PUBLIC_CONTEXT } from '@/common/publicContext'

// 远程接口基址：为空时走相对路径（exe 内嵌 Kestrel 或同源部署），
// 非空时所有 API 请求直接发往指定远程服务器（独立浏览器部署场景）。
export const API_BASE = (import.meta.env.VITE_API_BASE as string | undefined)?.trim() ?? ''

// 延迟多久才显示 loading（ms）：快于此值完成的请求不弹遮罩，避免闪烁
const LOADING_DELAY = 200

// 请求超时（ms）：后端未启动时默认 TCP 超时可能长达 30s，
// axios 内置 timeout 主动中断，快速展示错误提示。
const REQUEST_TIMEOUT = 10_000

// 登录态持久化键：与 common/stores/auth.ts 保持一致，token 从中读取。
const AUTH_PERSIST_KEY = 'auth_state_v1'

/** API 请求错误：非 2xx 响应时抛出，responseBody 保留完整响应体供调用方读取额外字段 */
export class ApiError extends Error {
  responseBody: Record<string, unknown>
  constructor(message: string, responseBody: Record<string, unknown>) {
    super(message)
    this.name = 'ApiError'
    this.responseBody = responseBody
  }
}

/** 从持久化登录态读取 JWT 令牌（无则返回空串）。
 *  公开上下文（public=1）始终返回空串：外部页面以匿名身份请求，
 *  后端不会进入任何认证/挤号/停用校验分支。纯净窗口（standalone=1）照常携带令牌。 */
function readToken(): string {
  if (IS_PUBLIC_CONTEXT) return ''
  try {
    const raw = localStorage.getItem(AUTH_PERSIST_KEY)
    if (!raw) return ''
    const o = JSON.parse(raw)
    return typeof o?.token === 'string' ? o.token : ''
  } catch {
    return ''
  }
}

/** 处理 401：读取后端返回的原因（如挤号、账号停用），展示提示后清除登录态并重新加载。
 *  幂等保护：多个并发请求同时返回 401 时只展示一次提示、只刷新一次。
 *  公开上下文（public=1）静默忽略：外部页面与主窗口共享 localStorage，
 *  这里若清登录态会连带把主窗口挤下线。 */
let unauthorizedHandled = false
function handleUnauthorized(message: string) {
  if (IS_PUBLIC_CONTEXT) return
  if (unauthorizedHandled) return
  unauthorizedHandled = true
  try {
    // 标记登出原因为 API 返回 401（可能是账户被停用或挤号）
    const authState = localStorage.getItem(AUTH_PERSIST_KEY)
    if (authState) {
      const auth = JSON.parse(authState)
      auth.loggedIn = false
      localStorage.setItem(AUTH_PERSIST_KEY, JSON.stringify(auth))
    }
    localStorage.removeItem(AUTH_PERSIST_KEY)
  } catch {
    /* 忽略清除失败 */
  }
  // 先展示提示，让用户知道被挤号/停用的原因，延迟后刷新回到登录页
  ElMessage.error({ message, duration: 3000, appendTo: fullscreenElement() })
  setTimeout(() => {
    if (typeof window !== 'undefined') window.location.reload()
  }, 2500)
}

// ==================== 全局 Loading 遮罩 ====================

let pendingCount = 0
let loadingInstance: ReturnType<typeof ElLoading.service> | null = null
let showTimer: number | null = null
// loading 遮罩当前使用的 z-index 序号（小于 0 表示未分配）
let loadingZ = -1
// 全局 loading 遮罩元素引用（创建时缓存，用于后续提升层级）
let loadingMaskEl: HTMLElement | null = null
const { nextZIndex } = useZIndex()

// 最近一次用户点击的元素：用于判定请求触发位置，实现"在哪里触发就遮罩哪个窗口"
let lastInteractEl: HTMLElement | null = null
document.addEventListener('pointerdown', (e) => {
  if (e.target instanceof HTMLElement) lastInteractEl = e.target
}, true)

/**
 * 当前打开且处于最上层的弹窗/抽屉：只要存在打开的窗口，loading 就应遮罩该窗口，
 * 绝不落到外部页面（点击卡片后才弹出的窗口，点击位置在窗口外，不能依赖点击元素判定）。
 * 按遮罩层 z-index 取最高，相同则取 DOM 靠后者（后打开的窗口）。
 */
function topmostOpenWindow(): HTMLElement | null {
  let best: HTMLElement | null = null
  let bestZ = -1
  for (const el of document.querySelectorAll<HTMLElement>('.el-dialog, .el-drawer')) {
    const overlay = el.closest<HTMLElement>('.el-overlay')
    // 关闭未销毁的窗口其遮罩层 display:none，跳过
    if (!overlay || overlay.style.display === 'none' || overlay.hidden) continue
    const z = Number(overlay.style.zIndex) || 0
    if (z >= bestZ) {
      bestZ = z
      best = el
    }
  }
  return best
}

function getLoadingTarget(): HTMLElement {
  // 处于浏览器全屏状态时，遮罩必须挂到全屏元素内（body 层内容会被全屏层遮挡）
  const fs = fullscreenElement()
  if (fs) {
    ensurePositioned(fs)
    return fs
  }
  // 全局规则：存在打开的弹窗/抽屉时，loading 始终遮罩最上层窗口，不加载到外部页面
  const top = topmostOpenWindow()
  if (top) {
    ensurePositioned(top)
    return top
  }
  // 兜底：最近点击位置处于某弹窗内（且弹窗仍在页面上）时只遮罩该弹窗
  const dialog = lastInteractEl?.closest?.('.el-dialog') as HTMLElement | null
  if (dialog?.isConnected) {
    ensurePositioned(dialog)
    return dialog
  }
  // 其余场景遮罩页面主区域（非整个窗口）
  const main = (
    document.querySelector<HTMLElement>('.layout-main') ||
    document.querySelector<HTMLElement>('.standalone-page')
  )
  if (main) {
    ensurePositioned(main)
    return main
  }
  return document.body
}

/**
 * 将 loading 遮罩提升到 EP z-index 栈最新序号之上：弹窗打开/点击置顶都会消耗序号，
 * 遮罩若沿用旧序号会被弹窗遮挡，故遮罩显示时及请求持续期间每次重新取号。
 */
function bumpLoadingZIndex() {
  if (!loadingInstance) return
  loadingZ = nextZIndex()
  if (loadingMaskEl) loadingMaskEl.style.zIndex = String(loadingZ)
}

function loadingStart() {
  pendingCount++
  if (loadingInstance) {
    // 遮罩已展示中：新请求可能在弹窗打开后发起，提升遮罩层级确保盖住弹窗
    bumpLoadingZIndex()
    return
  }
  if (pendingCount === 1 && showTimer === null) {
    showTimer = window.setTimeout(() => {
      showTimer = null
      if (pendingCount > 0) {
        loadingInstance = ElLoading.service({
          target: getLoadingTarget(),
          text: '加载中…',
          background: 'rgba(255, 255, 255, 0.6)',
        })
        loadingMaskEl = (loadingInstance as unknown as { $el?: HTMLElement }).$el ?? null
        // 创建时 EP 内部取号可能已被弹窗置顶操作抢先消耗，创建后再显式提升一次
        bumpLoadingZIndex()
      }
    }, LOADING_DELAY)
  }
}

function loadingEnd() {
  pendingCount = Math.max(0, pendingCount - 1)
  if (pendingCount === 0) {
    if (showTimer !== null) {
      clearTimeout(showTimer)
      showTimer = null
    }
    try {
      loadingInstance?.close()
    } catch {
      /* 遮罩宿主（如弹窗）可能已随 destroy-on-close 销毁，关闭失败直接忽略 */
    }
    loadingInstance = null
    loadingMaskEl = null
    loadingZ = -1
  }
}

// ==================== Axios 实例与拦截器 ====================

// 拦截器已将 response.data 直接返回（而非 AxiosResponse），
// 通过模块声明修正类型，避免调用方拿到 AxiosResponse<T> 而非 T。
declare module 'axios' {
  // eslint-disable-next-line @typescript-eslint/no-empty-object-type, @typescript-eslint/no-explicit-any
  export interface AxiosResponse<T = any> extends Promise<T> {}
}

const api = axios.create({
  baseURL: API_BASE,
  timeout: REQUEST_TIMEOUT,
  // 查询参数序列化：跳过 null / undefined / 空串，与原 buildQuery 行为一致
  paramsSerializer: {
    serialize: (params: Record<string, unknown>) => {
      const parts: string[] = []
      for (const [k, v] of Object.entries(params)) {
        if (v == null || v === '') continue
        parts.push(`${encodeURIComponent(k)}=${encodeURIComponent(String(v))}`)
      }
      return parts.join('&')
    },
  },
})

// ── 请求拦截器：注入 JWT 令牌 ──
api.interceptors.request.use((config) => {
  const token = readToken()
  if (token) config.headers.Authorization = `Bearer ${token}`
  return config
})

// ── 响应拦截器（成功）：直接返回 data ──
api.interceptors.response.use(
  (response) => response.data,
)

// ── 响应拦截器（失败）：统一错误处理 ──
api.interceptors.response.use(undefined, (error: AxiosError) => {
  const silent = (error.config as unknown as Record<string, unknown>)?.__silent === true
  const url = (error.config?.baseURL ?? '') + (error.config?.url ?? '')

  if (error.response) {
    const { status, data, headers } = error.response
    const body = (typeof data === 'object' && data !== null ? data : {}) as Record<string, unknown>

    // 401：登录态失效（挤号、账号停用等）
    if (status === 401) {
      if (!silent) {
        const message = typeof body.message === 'string' ? body.message : '登录已过期或未登录，请重新登录'
        handleUnauthorized(message)
      }
      throw new Error(`登录已过期或未登录，接口地址：${url}`)
    }

    // 404 + HTML：后端未启动时静态文件中间件返回的 404 页面
    const contentType = (headers?.['content-type'] as string) || ''
    if (status === 404 && contentType.includes('text/html')) {
      if (!silent) {
        ElMessage.error({ message: '后端服务暂时不可用（HTTP 404），后端 API 可能未启动或路由不可达，请稍后重试', appendTo: fullscreenElement(), grouping: true })
      }
      throw new ApiError(`后端服务暂时不可用（HTTP 404 HTML）：${url}`, {})
    }

    // 其它非 2xx：业务错误
    const detail = (body.message as string) || (body.title as string) || `HTTP ${status}`
    if (!silent) ElMessage.error({ message: detail, appendTo: fullscreenElement() })
    throw new ApiError(`${detail}，接口地址：${url}`, body)
  }

  // 无 response：网络异常 / 超时 / 请求取消
  if (!silent && isNetworkError(error)) {
    ElMessage.error({
      message: `无法连接到后端服务：${error.message || '网络连接异常'}，请确认后端服务已启动或网络连接正常`,
      appendTo: fullscreenElement(),
      grouping: true,
    })
  }
  throw error
})

// ==================== 导出方法（签名与原 fetch 版完全一致） ====================

/** GET 请求；timeoutMs 可选，长耗时接口（如磁盘扫描）可传入更大值；
 *  opts.silent 用于后台静默轮询：不弹 loading 遮罩、不弹错误提示、不跳转错误页，失败仅抛错由调用方自行处理 */
export async function httpGet<T>(url: string, params?: Record<string, unknown>, timeoutMs?: number, opts?: { silent?: boolean }): Promise<T> {
  const silent = opts?.silent === true
  if (!silent) loadingStart()
  try {
    return await api.get<T>(url, {
      params,
      timeout: timeoutMs,
      __silent: silent,
    } as Record<string, unknown>)
  } finally {
    if (!silent) loadingEnd()
  }
}

/** POST JSON 请求；signal 可选用于取消请求（如中止 SQL 执行），timeoutMs 可选用于长耗时接口 */
export async function httpPost<T>(url: string, body: unknown, signal?: AbortSignal, timeoutMs?: number, opts?: { silent?: boolean }): Promise<T> {
  const silent = opts?.silent === true
  if (!silent) loadingStart()
  try {
    return await api.post<T>(url, body, {
      signal,
      // 调用方已提供 signal 时由其自行管理超时，禁用 axios 内置超时
      timeout: signal ? 0 : timeoutMs,
      __silent: silent,
    } as Record<string, unknown>)
  } finally {
    if (!silent) loadingEnd()
  }
}

/** PUT JSON 请求 */
export async function httpPut<T>(url: string, body: unknown): Promise<T> {
  loadingStart()
  try {
    return await api.put<T>(url, body)
  } finally {
    loadingEnd()
  }
}

/** DELETE 请求 */
export async function httpDelete<T>(url: string): Promise<T> {
  loadingStart()
  try {
    return await api.delete<T>(url)
  } finally {
    loadingEnd()
  }
}

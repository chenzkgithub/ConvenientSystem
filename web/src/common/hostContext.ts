/**
 * 宿主环境判定（接口分离的唯一判定源）
 *
 * 判定依据按优先级：
 * - primary：桌面端 Kestrel 经 HostKindInjectionMiddleware 在 index.html 注入的
 *   `<meta name="cs-host" content="desktop">`（Web 服务器不注入，故 meta 缺失即 Web 端）；
 * - 兜底：旧版 exe + 新版 wwwroot 组合下 meta 尚未注入，读 localStorage 持久化值，
 *   避免每次刷新闪烁判定。
 *
 * 与 publicContext.ts 相同：模块加载时就地求值并固化，后续不做运行期动态探测，
 * 禁止在其它文件里自行嗅探 UA/webview，一律从本模块引入。
 *
 * 安全边界不在前端：本判定只决定客户端行为（是否发本地请求、展示哪些菜单），
 * 接口能否访问始终由后端控制（服务器端 /api/local/* 恒返回 410）。
 */
const metaHost =
  typeof document !== 'undefined'
    ? document.querySelector('meta[name="cs-host"]')?.getAttribute('content') ?? ''
    : ''

/** 宿主判定持久化键：桌面端确认后写入，供旧版 exe（无注入中间件）+ 新版前端组合兜底 */
const HOST_KIND_KEY = 'cs_host_kind'

type HostKind = 'desktop' | 'web'

function resolveHostKind(): HostKind {
  if (metaHost === 'desktop') {
    try {
      localStorage.setItem(HOST_KIND_KEY, 'desktop')
    } catch {
      /* 隐私模式等写入失败不影响本次判定 */
    }
    return 'desktop'
  }
  // meta 缺失：旧版 exe（注入中间件未上线）读兜底缓存；纯 Web 部署缓存里不会有 desktop 值
  try {
    if (localStorage.getItem(HOST_KIND_KEY) === 'desktop') return 'desktop'
  } catch {
    /* 读取失败按 Web 端处理 */
  }
  return 'web'
}

/** 当前宿主：desktop=桌面端 exe 内嵌 Kestrel；web=纯服务器部署 */
export const HOST_KIND: HostKind = resolveHostKind()

/** 是否桌面端宿主：本地接口/桌面专属菜单的唯一开关 */
export const IS_DESKTOP_HOST = HOST_KIND === 'desktop'

/** 是否纯 Web 宿主（独立浏览器/服务器部署） */
export const IS_WEB_HOST = !IS_DESKTOP_HOST

// ==================== 宿主能力集（能力中心最小版） ====================

import { ref } from 'vue'

/** 桌面端能力集（api/local/host/capabilities 的响应形状） */
export interface HostCapabilities {
  hostKind: 'desktop'
  appVersion: string
  /** 内网考勤库（YhSystemDb）连接串已配置 */
  localDbConfigured: boolean
  /** 本机可执行 git */
  gitAvailable: boolean
}

/**
 * 宿主能力：App 启动时拉取一次（loadHostCapabilities）。
 * 非桌面端或拉取失败为 null——调用方按「无本地能力」处理（如考勤引导页判 localDbConfigured !== true）。
 */
export const hostCapabilities = ref<HostCapabilities | null>(null)

/**
 * 拉取桌面端宿主能力（幂等，可重复调用）。静默失败置 null，不影响启动。
 * request.ts 静态依赖本模块，这里用动态 import 回引，避免静态循环依赖。
 */
export async function loadHostCapabilities(): Promise<void> {
  if (!IS_DESKTOP_HOST) return
  try {
    const { localGet } = await import('@/api/request')
    hostCapabilities.value = await localGet<HostCapabilities>(
      '/api/local/host/capabilities',
      undefined,
      undefined,
      { silent: true },
    )
  } catch {
    hostCapabilities.value = null
  }
}

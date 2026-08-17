import type { MenuNode } from '@/common/types'

/** 判断菜单 page 是否为第三方外部链接 */
export function isExternalLink(node: { page?: string | null; external?: boolean }): boolean {
  // 优先按显式 external 属性判断
  if (node.external === true) return true
  if (node.external === false) return false
  // 兼容旧数据：按 http/https 前缀推断
  return !!node.page && /^https?:\/\//i.test(node.page)
}

/** 外链承载页路由：把外部地址用 iframe 内嵌在主界面标签里显示 */
export const EXTERNAL_ROUTE_PATH = '/external'
export const EXTERNAL_ROUTE_NAME = 'external'

/**
 * 菜单叶子 → 主界面内部跳转目标。主窗口里点开的菜单一律在标签页内展示：
 * - 内部页面：直接用它自己的路由；
 * - 外链：交给 /external 承载页内嵌，url 为目标地址，title 用作标签标题。
 * （悬浮按钮 / 托盘菜单在主窗口之外，仍由宿主用独立窗口打开，不走这里。）
 */
export function toMenuLocation(node: { page?: string | null; title: string; external?: boolean }) {
  const page = node.page || ''
  if (isExternalLink(node)) {
    return { path: EXTERNAL_ROUTE_PATH, query: { url: page, title: node.title } }
  }
  return page
}

/**
 * 菜单节点在侧栏 el-menu 中的 index：内部页面为它的路由路径，外链为原始 URL。
 * 只用作选中标识，不直接交给 router.push（跳转统一走 toMenuLocation）。
 */
export function resolveMenuTarget(node: { page?: string | null; title: string; external?: boolean }): string {
  return node.page || ''
}

interface WebView2Bridge {
  postMessage: (message: unknown) => void
}

function hostBridge(): WebView2Bridge | undefined {
  return (window as unknown as { chrome?: { webview?: WebView2Bridge } }).chrome?.webview
}

/**
 * 在独立窗口打开第三方外链：
 * - 桌面壳（WebView2）环境：通知宿主用 BrowserForm 打开，同一页面已有窗口时直接激活前置（与悬浮按钮/托盘菜单一致）；
 * - 普通浏览器（开发环境）：回退为新标签页打开。
 * 主界面菜单不再走这里（改为 /external 内嵌），仅供承载页上“独立窗口打开”按钮使用：
 * 部分站点用 X-Frame-Options / CSP frame-ancestors 禁止被内嵌，只能改用独立窗口。
 */
export function openExternalWindow(node: { page?: string | null; title: string }): void {
  const url = node.page || ''
  if (!url) return
  const wv = hostBridge()
  if (wv) {
    wv.postMessage({ type: 'page:open', page: url, title: node.title, external: true })
  } else {
    window.open(url, '_blank')
  }
}

/**
 * 剪出侧栏要展示的菜单树：按 visible 和 enabled 过滤——visible/enabled 为 false 的不显示；
 * 另外去掉剪完后没有任何叶子的空分组。
 * 外链叶子点击时不走内部路由，由 openExternalWindow 开独立窗口（见 MainLayout 的 onMenuSelect）。
 */
export function filterVisibleMenus(nodes: MenuNode[]): MenuNode[] {
  const result: MenuNode[] = []
  for (const n of nodes) {
    if (n.visible === false || n.enabled === false) continue
    if (Array.isArray(n.children) && n.children.length > 0) {
      const children = filterVisibleMenus(n.children)
      if (children.length > 0) result.push({ ...n, children })
      continue
    }
    if (n.page) result.push(n)
  }
  return result
}

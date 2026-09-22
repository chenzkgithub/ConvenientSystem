import { createRouter, createWebHashHistory, type RouteComponent, type RouteRecordRaw } from 'vue-router'
import { ElMessage } from 'element-plus'
import { getViewComponent } from '@/common/viewComponents'
import { EXTERNAL_ROUTE_NAME, EXTERNAL_ROUTE_PATH, isMenuNodeAvailable } from '@/common/menuLink'
import { IS_DESKTOP_HOST } from '@/common/hostContext'
import type { MenuNode } from '@/common/types'

const HOME_ROUTE: RouteRecordRaw = {
  path: '/',
  name: 'home',
  component: () => import('@/common/views/HomeView.vue'),
}

const EXTERNAL_ROUTE: RouteRecordRaw = {
  // 外链承载页：菜单里的外部地址统一由它用 iframe 内嵌，目标地址走 query.url
  path: EXTERNAL_ROUTE_PATH,
  name: EXTERNAL_ROUTE_NAME,
  component: () => import('@/common/views/ExternalPageView.vue'),
}

const LOCK_SCREEN_ROUTE: RouteRecordRaw = {
  // 独立锁屏页：桌面端弹出窗口复用 Web 锁屏界面
  path: '/lock-screen',
  name: 'lock-screen',
  component: () => import('@/common/views/LockScreenView.vue'),
}

const LOTTERY_SUMMARY_ROUTE: RouteRecordRaw = {
  // 开奖汇总详情页：企业微信通知卡片外链入口，
  // 静态注册不依赖菜单加载，public=1 时免登录直接打开
  path: '/lottery-result-summary',
  name: 'lottery-result-summary',
  component: () => import('@/common/views/LotteryResultSummaryView.vue'),
}

const UNIVERSAL_BUILD_ROUTE: RouteRecordRaw = {
  // 通用构建发布工具（桌面专属：依赖本机构建环境）
  path: '/universal-build',
  name: 'universal-build',
  component: () => import('@/common/views/UniversalBuildView.vue'),
  meta: { desktopOnly: true },
}

const PIPELINE_ROUTE: RouteRecordRaw = {
  // 构建发布流水线（桌面专属：引擎在本机执行构建/部署）
  path: '/pipeline',
  name: 'pipeline',
  component: () => import('@/common/views/PipelineView.vue'),
  meta: { desktopOnly: true },
}

const GIT_WORKBENCH_ROUTE: RouteRecordRaw = {
  // Git 代码管理工作台（桌面专属：git 命令在本机执行）
  path: '/git-workbench',
  name: 'git-workbench',
  component: () => import('@/common/views/GitWorkbenchView.vue'),
  meta: { desktopOnly: true },
}

const PLACEHOLDER_ROUTE: RouteRecordRaw = {
  // 未实现的菜单路由统一显示“开发中”占位
  path: '/:pathMatch(.*)*',
  name: 'placeholder',
  component: () => import('@/common/views/PlaceholderView.vue'),
}

export const router = createRouter({
  // hash 模式：由 Kestrel 根提供 index.html，客户端路由无需服务端 fallback
  history: createWebHashHistory(),
  routes: [HOME_ROUTE, EXTERNAL_ROUTE, LOCK_SCREEN_ROUTE, LOTTERY_SUMMARY_ROUTE, UNIVERSAL_BUILD_ROUTE, PIPELINE_ROUTE, GIT_WORKBENCH_ROUTE, PLACEHOLDER_ROUTE],
})

// ===== 首帧导航守卫：动态路由注册前命中占位页时挂起等待，注册完成后重解析直达目标 =====
// 背景：菜单/公开页路由由后端配置动态注册，应用启动时首次导航必然先命中占位路由。
// 旧实现是"导航后修补"（isReady 后先 replace('/') 再 replace 目标路径），中间帧会闪首页/登录页；
// 改为"导航前阻塞"：首帧挂起等注册信号，携带原 query 重新解析，首帧即目标页、零闪烁。
let notifyDynamicRoutesReady: () => void = () => {}
// 12s 超时兜底：注册信号未发出也强制放行，避免后端不可用时首帧永久挂起白屏
const dynamicRoutesPending = new Promise<void>((resolve) => {
  notifyDynamicRoutesReady = resolve
  setTimeout(resolve, 12_000)
})

let dynamicRouteRetried = false
router.beforeEach(async (to) => {
  // 桌面专属静态路由：菜单已按宿主过滤，这里防 Web 端直敲 URL（双保险）
  if (to.meta?.desktopOnly && !IS_DESKTOP_HOST) {
    ElMessage.warning('此功能仅桌面端可用')
    return { path: '/' }
  }
  // 非占位路由（首页 / 外链承载 / 锁屏等静态路由）不受动态注册影响，直接放行
  if (to.name !== 'placeholder') return true
  // 已重试过仍命中占位页：目标确实未注册（真 404），放行渲染"开发中"占位页
  if (dynamicRouteRetried) return true
  dynamicRouteRetried = true
  await dynamicRoutesPending
  // 重新解析当前地址：此刻动态路由已注册，直达目标（standalone=1 / public=1 等 query 保留）
  return to.fullPath
})

// 捕获路由懒加载失败：chunk 丢失或网络中断时弹提示，留在当前页面
router.onError((err, to) => {
  console.error('[router] 页面加载失败', err)
  ElMessage.error({
    message: `页面加载失败：无法加载页面“${to.fullPath}”，请检查网络连接或刷新后重试`,
    grouping: true,
    duration: 3000,
  })
})

const registeredPaths = new Set<string>()

/**
 * 根据菜单配置动态注册内部路由。
 * - 仅处理 external !== true 且含 page 的叶子节点；
 * - component 通过 viewModules 映射为异步组件；
 * - 动态路由注册后会把占位路由移到末尾，确保新路由能优先匹配；
 * - 已注册过的 path 会跳过，避免重复添加。
 */
export function registerMenuRoutes(menus: MenuNode[]) {
  // 把占位路由暂时移除，稍后再加回末尾
  router.removeRoute('placeholder')

  const walk = (nodes: MenuNode[]) => {
    nodes.forEach((node) => {
      // 停用菜单不注册路由（连同其子节点一并跳过）
      if (node.enabled === false) return
      if (Array.isArray(node.children) && node.children.length > 0) {
        walk(node.children)
        return
      }

      const rawPage = node.page
      if (!rawPage || node.external === true) return
      // 桌面专属视图的菜单在 Web 端不注册路由（与静态路由守卫同依据：VIEW_META 元数据），
      // 直敲 URL 会落占位页，不会渲染出调用本地接口的坏页面
      if (!isMenuNodeAvailable(node)) return
      // 分离路径和 query（page 可能包含 ?url=... 等参数）
      const [pagePath, queryString] = rawPage.split('?')
      if (registeredPaths.has(pagePath)) return

      const importer = getViewComponent(node.component)
      if (!importer) return

      try {
        router.addRoute({
          path: pagePath,
          name: node.name || pagePath,
          component: importer as () => Promise<RouteComponent>,
        })
        registeredPaths.add(pagePath)
      } catch (e) {
        console.warn('[router] 注册菜单路由失败', node, e)
      }
    })
  }

  walk(menus)

  // 占位路由放到最后，保证动态路由优先匹配
  router.addRoute(PLACEHOLDER_ROUTE)

  // 通知首帧守卫：动态路由已注册，放行挂起的占位页导航并重解析直达目标
  notifyDynamicRoutesReady()
}

/** 公开页面项（来自 SysPublicPage 表，ListEnabled 接口返回） */
export interface PublicPageItem {
  pageKey: string
  title: string
  component: string
}

/**
 * 根据数据库配置动态注册公开页面路由（免登录，访问链接带 public=1）。
 * - component 通过 viewModules 映射为异步组件；
 * - 已注册过的 path 会跳过，避免重复添加；
 * - 复用占位路由重导航模式，确保首次访问动态注册的页面不会命中 placeholder。
 */
export function registerPublicRoutes(pages: PublicPageItem[]) {
  router.removeRoute('placeholder')

  for (const page of pages) {
    const path = page.pageKey
    if (!path || registeredPaths.has(path)) continue
    // 公开页访客没有桌面宿主上下文：桌面专属组件在 Web 端不注册（元数据真源=VIEW_META）
    if (!isMenuNodeAvailable(page)) continue

    const importer = getViewComponent(page.component)
    if (!importer) {
      console.warn('[router] 公开页面组件未找到', page)
      continue
    }

    try {
      router.addRoute({
        path,
        name: `public-${path}`,
        component: importer as () => Promise<RouteComponent>,
        meta: { title: page.title || page.pageKey },
      })
      registeredPaths.add(path)
    } catch (e) {
      console.warn('[router] 注册公开页面路由失败', page, e)
    }
  }

  router.addRoute(PLACEHOLDER_ROUTE)

  // 通知首帧守卫：公开页路由已注册，放行挂起的占位页导航并重解析直达目标
  notifyDynamicRoutesReady()
}

// 公开页面根据维护名称自动设置 document.title
router.afterEach((to) => {
  const title = to.meta?.title
  if (typeof title === 'string' && title) {
    document.title = title
  }
})

export default router

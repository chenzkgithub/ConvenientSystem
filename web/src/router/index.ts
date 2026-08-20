import { createRouter, createWebHashHistory, type RouteComponent, type RouteRecordRaw } from 'vue-router'
import { nextTick } from 'vue'
import { ElMessage } from 'element-plus'
import { getViewComponent } from '@/common/viewComponents'
import { EXTERNAL_ROUTE_NAME, EXTERNAL_ROUTE_PATH } from '@/common/menuLink'
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

const PLACEHOLDER_ROUTE: RouteRecordRaw = {
  // 未实现的菜单路由统一显示“开发中”占位
  path: '/:pathMatch(.*)*',
  name: 'placeholder',
  component: () => import('@/common/views/PlaceholderView.vue'),
}

export const router = createRouter({
  // hash 模式：由 Kestrel 根提供 index.html，客户端路由无需服务端 fallback
  history: createWebHashHistory(),
  routes: [HOME_ROUTE, EXTERNAL_ROUTE, LOCK_SCREEN_ROUTE, LOTTERY_SUMMARY_ROUTE, PLACEHOLDER_ROUTE],
})

// 捕获路由懒加载失败：chunk 丢失或网络中断时弹提示，留在当前页面
router.onError((err, to) => {
  console.error('[router] 页面加载失败', err)
  ElMessage.error({
    message: `页面加载失败：无法加载页面“${to.fullPath}”，请检查网络连接或刷新后重试`,
    grouping: true,
    duration: 6000,
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

  // 等初次导航完成后再判断：若当前路径仍命中占位路由，说明目标是刚注册的动态路由，
  // 重新解析一次并保留 query（如 standalone=1）。
  // 注意不能在初次导航完成前读取 currentRoute（此时是 START 位置 path='/'），
  // 否则会把独立窗口的目标页面误重定向回首页。
  // 关键：先 replace 到 "/" 再 replace 到目标路径，强制 Vue Router 重新匹配
  // （直接 replace 同一路径会被内部缓存命中，不会重新解析刚注册的动态路由）。
  void router.isReady().then(() =>
    nextTick(() => {
      const current = router.currentRoute.value
      if (current.name === 'placeholder' && current.path) {
        const target = { path: current.path, query: { ...current.query } }
        router.replace('/').then(() => router.replace(target)).catch(() => {})
      }
    }),
  )
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

  // 占位路由重导航（同 registerMenuRoutes 逻辑）
  void router.isReady().then(() =>
    nextTick(() => {
      const current = router.currentRoute.value
      if (current.name === 'placeholder' && current.path) {
        const target = { path: current.path, query: { ...current.query } }
        router.replace('/').then(() => router.replace(target)).catch(() => {})
      }
    }),
  )
}

// 公开页面根据维护名称自动设置 document.title
router.afterEach((to) => {
  const title = to.meta?.title
  if (typeof title === 'string' && title) {
    document.title = title
  }
})

export default router

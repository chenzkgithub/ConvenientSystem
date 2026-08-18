<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Lock, Close, User, Delete, SwitchButton, ArrowDown, Search, Fold, Expand, Refresh, Operation, Sunny, Moon } from '@element-plus/icons-vue'
import MenuTree from '@/common/components/MenuTree.vue'
import NoticeBell from '@/common/components/NoticeBell.vue'
import NoticeAlert from '@/common/components/NoticeAlert.vue'
import ProfileDialog from '@/common/components/ProfileDialog.vue'
import CommandPalette from '@/common/components/CommandPalette.vue'
import KeyboardHelp from '@/common/components/KeyboardHelp.vue'
import { useMenuStore } from '@/common/stores/menu'
import { useLockStore } from '@/common/stores/lock'
import { useAuthStore } from '@/common/stores/auth'
import { useTabsStore } from '@/common/stores/tabs'
import { useThemeStore } from '@/common/stores/theme'
import { useRecentStore } from '@/common/stores/recent'
import {
  resolveMenuTarget,
  filterVisibleMenus,
  toMenuLocation,
  EXTERNAL_ROUTE_NAME,
} from '@/common/menuLink'
import { registerMenuRoutes } from '@/router'
import { pinyinMatchIndex } from '@/common/pinyin'
import type { MenuNode } from '@/common/types'

const route = useRoute()
const router = useRouter()
const menuStore = useMenuStore()
const lock = useLockStore()
const auth = useAuthStore()
const tabsStore = useTabsStore()
const themeStore = useThemeStore()
const recentStore = useRecentStore()

// ===== 侧栏折叠状态 =====
const SIDEBAR_COLLAPSE_KEY = 'cs_sidebar_collapsed'
// 登录后左侧菜单默认折叠：localStorage 未设置时默认折叠，用户手动展开后才保持展开
const isSidebarCollapsed = ref(localStorage.getItem(SIDEBAR_COLLAPSE_KEY) !== 'false')
const sidebarWidth = computed(() => (isSidebarCollapsed.value ? '0px' : '232px'))

function toggleSidebar() {
  isSidebarCollapsed.value = !isSidebarCollapsed.value
  localStorage.setItem(SIDEBAR_COLLAPSE_KEY, String(isSidebarCollapsed.value))
}

// ===== 导航模式：面包屑（默认）与多标签页可切换 =====
// 年轻化改造：默认隐藏多标签栏、用面包屑导航；老用户可切回标签栏。
// tabsStore 后台逻辑始终保留（keep-alive 缓存管理、刷新计数器仍需要）。
const NAV_MODE_KEY = 'cs_nav_mode'
// 'breadcrumb' = 面包屑模式（默认）；'tabs' = 多标签模式
const navMode = ref<'breadcrumb' | 'tabs'>(localStorage.getItem(NAV_MODE_KEY) === 'tabs' ? 'tabs' : 'breadcrumb')
const showTabsBar = computed(() => navMode.value === 'tabs')

function toggleNavMode() {
  navMode.value = navMode.value === 'breadcrumb' ? 'tabs' : 'breadcrumb'
  localStorage.setItem(NAV_MODE_KEY, navMode.value)
}

// 面包屑：根据当前路由与菜单树生成路径（首页 / 分组 / 子分组 / 页面）
const breadcrumbs = computed(() => {
  const crumbs: { title: string; path?: string }[] = []
  if (route.path === '/') return crumbs
  // 尝试在菜单树中找到当前叶子节点及其祖先链
  const path = route.path
  const queryUrl = String(route.query.url || '')
  const findTrail = (nodes: MenuNode[], trail: MenuNode[]): MenuNode[] | null => {
    for (const n of nodes) {
      const target = resolveMenuTarget(n)
      if (target === path || (queryUrl && target === queryUrl)) {
        return [...trail, n]
      }
      if (Array.isArray(n.children) && n.children.length > 0) {
        const found = findTrail(n.children, [...trail, n])
        if (found) return found
      }
    }
    return null
  }
  const trail = findTrail(sidebarMenus.value, [])
  if (trail) {
    for (const n of trail) crumbs.push({ title: n.title })
  } else {
    // 兜底：不在菜单里的页面，用标题占位
    crumbs.push({ title: resolveTitle() })
  }
  return crumbs
})

// 刷新当前页：递增刷新计数器使 keep-alive 组件重新挂载（复用标签页刷新逻辑）
function refreshCurrent() {
  tabsStore.refreshTab(route.fullPath)
}

// 侧栏展示所有 visible 的菜单，含外链；外链也在标签页内打开（见 onMenuSelect）
const sidebarMenus = computed(() => filterVisibleMenus(menuStore.menus))

// el-menu 高亮当前菜单项：内部页面用 fullPath（以区分 query）；
// 外链页的路由是 /external?url=...，而侧栏外链项的 index 是原始 URL，故取 query.url 才能对上。
const activeIndex = computed(() =>
  route.name === EXTERNAL_ROUTE_NAME ? String(route.query.url || '') : route.fullPath,
)

// ===== 侧栏菜单搜索 =====
/** 搜索命中项：叶子菜单本身 + 它所在的分组路径 */
interface MenuHit {
  index: string
  title: string
  group: string
}

const menuKeyword = ref('')

/**
 * 关键字命中的叶子菜单：标题、所在分组名、页面路径任一命中即入选。
 * 标题与分组名可用拼音首字母或全拼搜（cdgl / caidan 都能找到「菜单管理」）。
 * 搜索态平铺结果而不是在树里过滤——菜单有三四层深，过滤后仍要逐层点开分组才看得到目标。
 */
const menuHits = computed<MenuHit[]>(() => {
  const kw = menuKeyword.value.trim()
  if (!kw) return []
  // rank 越小越靠前：标题命中优于分组名/路径命中，同类里从头命中的优先
  const ranked: { hit: MenuHit; rank: number }[] = []
  const collect = (index: string, title: string, group: string) => {
    const byTitle = pinyinMatchIndex(title, kw)
    // 分组名与标题分开匹配而不是拼成一串，否则「系统管理/菜单管理」会被 glcd 这类跟读的缩写命中
    const byGroup = pinyinMatchIndex(group, kw)
    const byPath = index.toLowerCase().indexOf(kw.toLowerCase())
    if (byTitle < 0 && byGroup < 0 && byPath < 0) return
    const rank =
      byTitle >= 0 ? byTitle : 100 + Math.min(...[byGroup, byPath].filter((n) => n >= 0))
    ranked.push({ hit: { index, title, group }, rank })
  }
  // 首页不在菜单数据里，单独参与匹配
  collect('/', '首页', '')
  const walk = (nodes: MenuNode[], trail: string[]) => {
    for (const n of nodes) {
      if (Array.isArray(n.children) && n.children.length > 0) {
        walk(n.children, [...trail, n.title])
        continue
      }
      const index = resolveMenuTarget(n)
      if (index) collect(index, n.title, trail.join(' / '))
    }
  }
  walk(sidebarMenus.value, [])
  // sort 稳定，rank 相同时保持菜单原顺序
  return ranked.sort((a, b) => a.rank - b.rank).map((r) => r.hit)
})

/** 回车打开相关度最高的那条结果 */
function onSearchEnter() {
  const first = menuHits.value[0]
  if (first) onMenuSelect(first.index)
}

// 侧栏点击：统一走内部路由，外链由 toMenuLocation 换成 /external 承载页。
// el-menu 因此不能用 router 模式——外链项的 index 是 http 地址，直接交给 router.push 匹配不到任何路由。
function onMenuSelect(index: string) {
  if (!index) return
  menuKeyword.value = '' // 选完回到完整菜单树，当前项在树里高亮
  const leaf = menuStore.collectLeaves().find((l) => (l.page || '') === index)
  void router.push(leaf ? toMenuLocation(leaf) : index)
}

// keep-alive 缓存键：包含路由名 + fullPath + 刷新计数器，
// 刷新时递增计数器使组件重新挂载，无需改变路由（避免新开标签）
const viewKey = computed(() => {
  const rc = tabsStore.refreshCounters[route.fullPath] || 0
  return `${String(route.name ?? '')}:${route.fullPath}:${rc}`
})

// 顶栏头像首字母
const avatarText = computed(() => (auth.displayName || auth.currentAccount || '').trim().slice(0, 1).toUpperCase())

// ===== 头像下拉菜单：个人资料 / 清理缓存 / 退出登录 =====
const profileVisible = ref(false)

function onCommand(cmd: string) {
  if (cmd === 'profile') profileVisible.value = true
  else if (cmd === 'clearCache') void onClearCache()
  else if (cmd === 'logout') void onLogout()
}

/** 退出登录：清空标签页并丢弃登录态（App.vue 依据 loggedIn 回到登录页）。 */
async function onLogout() {
  try {
    await ElMessageBox.confirm('确定要退出当前账号吗？', '退出登录', {
      type: 'warning',
      confirmButtonText: '退出',
      cancelButtonText: '取消',
    })
  } catch {
    return // 用户取消
  }
  tabsStore.reset()
  lock.reset()
  auth.logout()
}

/**
 * 清理缓存：清空全部本地存储（登录态、标签页、SQL 工具/代码编辑器草稿等）并退出到登录页。
 * 不逐个列举键名，避免新增视图的缓存键被遗漏；刷新后各 store 从空存储重建。
 */
async function onClearCache() {
  try {
    await ElMessageBox.confirm(
      '将清除所有本地缓存（包括登录态）并退出到登录页，是否继续？',
      '清理缓存',
      { type: 'warning', confirmButtonText: '确定清理', cancelButtonText: '取消' },
    )
  } catch {
    return // 用户取消
  }
  auth.logout()
  ElMessage.success('缓存已清理，正在跳转登录页…')
  setTimeout(() => window.location.reload(), 600)
}

/** 改密成功：旧 JWT 仍有效，为避免新旧密码并存，提示后强制重新登录。 */
function onPasswordChanged() {
  ElMessage.success('密码修改成功，请使用新密码重新登录')
  setTimeout(() => {
    tabsStore.reset()
    lock.reset()
    auth.logout()
  }, 1500)
}

// 根据任意标签 path（路由 fullPath，含 query）解析标题：首页 / 外链 title / 菜单叶子标题 / 兜底 path
function resolveTitleByPath(fullPath: string): string {
  const [path, queryString] = fullPath.split('?')
  if (path === '/') return '首页'
  const title = new URLSearchParams(queryString || '').get('title')
  if (title) return title
  const leaves = menuStore.collectLeaves()
  // 同一 path 可能被多个菜单靠 query 区分（如多彩种共用 /lottery，大乐透无 query、其余带 ?type=xxx），
  // 必须先整体按 fullPath 精确匹配一遍，匹配不到才退回按 path 匹配；
  // 若写成单次遍历里 fullPath || path 的或条件，会先命中不带 query 的那条菜单，
  // 导致排列五、福彩3D 等页签标题都显示成大乐透。
  const leaf =
    leaves.find((l) => resolveMenuTarget(l) === fullPath) ??
    leaves.find((l) => l.page === path)
  return leaf?.title || path
}

// 当前路由对应的标签标题
function resolveTitle(): string {
  return resolveTitleByPath(route.fullPath)
}

// ===== 标签页记忆：刷新后恢复上次打开的标签与当前页面 =====
// 标签由 tabs store 从 localStorage 恢复；路由监听器（immediate: true）会把当前 URL 对应标签置为激活。
// 登录切换时的重置由下方 watch(auth.loggedIn) 与退出登录处的 tabsStore.reset() 负责，
// 此处不能无条件 reset，否则刷新（已登录态从本地恢复）会清掉刚恢复的标签页。

// 路由变化 → 打开/激活对应标签
watch(
  () => route.fullPath,
  () => {
    tabsStore.openTab(route.fullPath, resolveTitle())
  },
  { immediate: true },
)

// 菜单异步加载完成后，刷新所有标签的标题：
// 不只当前标签（避免深链进入时标题为 path），也包括从 localStorage 恢复的旧标签
// （旧版本可能存着错误标题，如多彩种页签都叫大乐透）
watch(
  () => menuStore.menus,
  () => {
    tabsStore.tabs.forEach((t) => tabsStore.renameTab(t.path, resolveTitleByPath(t.path)))
    tabsStore.openTab(route.fullPath, resolveTitle())
  },
  { deep: true },
)

function goTab(path: string) {
  if (path !== route.fullPath) router.push(path)
}

async function onCloseTab(path: string) {
  // 先询问该标签的关闭守卫（如代码编辑器未保存时弹窗确认），被拦截则不关闭。
  if (!(await tabsStore.canCloseTab(path))) return
  const next = tabsStore.closeTab(path)
  if (next && next !== route.fullPath) router.push(next)
}

// ===== 标签页右键菜单 =====
interface CtxMenuState {
  visible: boolean
  x: number
  y: number
  path: string
  title: string
  pinned: boolean
  closable: boolean
}

const ctxMenu = ref<CtxMenuState>({
  visible: false, x: 0, y: 0, path: '', title: '', pinned: false, closable: true,
})

function onTabContextMenu(e: MouseEvent, tab: typeof tabsStore.tabs[number]) {
  e.preventDefault()
  // 计算菜单位置，避免超出视口
  const menuW = 180, menuH = 280
  const x = e.clientX + menuW > window.innerWidth ? e.clientX - menuW : e.clientX
  const y = e.clientY + menuH > window.innerHeight ? e.clientY - menuH : e.clientY
  ctxMenu.value = {
    visible: true, x, y,
    path: tab.path,
    title: tab.title,
    pinned: !!tab.pinned,
    closable: tab.closable,
  }
}

function hideCtxMenu() {
  ctxMenu.value.visible = false
}

async function ctxRefresh() {
  hideCtxMenu()
  // 递增刷新计数器，viewKey 变化使 keep-alive 组件重新挂载，不改变路由
  tabsStore.refreshTab(ctxMenu.value.path)
}

async function ctxClose() {
  hideCtxMenu()
  await onCloseTab(ctxMenu.value.path)
}

function ctxCloseOther() {
  hideCtxMenu()
  const path = ctxMenu.value.path
  tabsStore.closeOtherTabs(path)
  if (path !== route.fullPath) router.push(path)
}

function ctxCloseAll() {
  hideCtxMenu()
  tabsStore.closeAllTabs()
  const remain = tabsStore.active
  if (remain !== route.fullPath) router.push(remain)
}

function ctxTogglePin() {
  hideCtxMenu()
  tabsStore.togglePin(ctxMenu.value.path)
}

function ctxCopyLink() {
  hideCtxMenu()
  const url = window.location.origin + window.location.pathname + '#' + ctxMenu.value.path
  navigator.clipboard.writeText(url).then(() => {
    ElMessage.success('链接已复制')
  }).catch(() => {
    ElMessage.error('复制失败')
  })
}

function ctxOpenInNewWindow() {
  hideCtxMenu()
  const url = window.location.pathname + '#' + ctxMenu.value.path + '?standalone=1'
  window.open(url, '_blank', 'width=1100,height=720')
}

// 点击其他地方关闭右键菜单
function onDocClick() { hideCtxMenu() }
onMounted(() => document.addEventListener('click', onDocClick))
onBeforeUnmount(() => document.removeEventListener('click', onDocClick))

// ===== 桌面壳托盘菜单联动 =====
interface HostMenuItem {
  title: string
  page?: string
  name?: string
  children?: HostMenuItem[]
  float?: boolean
  external?: boolean
}

function hostBridge() {
  return (
    window as unknown as {
      chrome?: { webview?: { postMessage?: (m: unknown) => void; addEventListener?: Function } }
    }
  ).chrome?.webview
}

// 将菜单树转为上报结构：分组保留 children，叶子带原始 page（内部路由或外链 URL），
// 由桌面壳在托盘点击时用独立窗口打开。
function toHostMenu(nodes: MenuNode[]): HostMenuItem[] {
  return nodes
    .filter((n) => n.enabled !== false)
    .map((n) => {
    if (Array.isArray(n.children) && n.children.length > 0) {
      return { title: n.title, children: toHostMenu(n.children), float: n.float || undefined }
    }
    return {
      title: n.title,
      page: n.page || undefined,
      name: n.name || undefined,
      float: n.float || undefined,
      external: n.external || undefined,
    }
  })
}

// 菜单变化时把“首页所有页面”上报给桌面壳，用于生成托盘右键菜单
watch(
  () => menuStore.menus,
  () => {
    registerMenuRoutes(menuStore.menus)
    hostBridge()?.postMessage?.({ type: 'menu:list', items: toHostMenu(menuStore.menus) })
  },
  { deep: true, immediate: true },
)

// 登录切换/退出时重置标签页，避免下一位用户继承上一位的页签；
// 刷新时 loggedIn 从本地恢复不触发本 watch，标签记忆得以保留
watch(
  () => auth.loggedIn,
  (val) => {
    tabsStore.reset()
    recentStore.reset()
    if (val) router.replace('/')
  },
)

// 路由变化时记录最近访问页面
watch(
  () => route.fullPath,
  (path) => {
    if (!path || path === '/') return
    const leaf = menuStore.collectLeaves().find((l) => (l.page || '') === path)
    if (leaf) {
      recentStore.record(path, leaf.title)
    }
  },
)

onMounted(() => {
  if (!menuStore.loaded) menuStore.load()
})

// 最近访问时间显示：刚刚/分钟前/小时前/昨天
function formatRecentTime(ts: number): string {
  const diff = Date.now() - ts
  if (diff < 60_000) return '刚刚'
  if (diff < 3_600_000) return Math.floor(diff / 60_000) + '分钟前'
  if (diff < 86_400_000) return Math.floor(diff / 3_600_000) + '小时前'
  if (diff < 172_800_000) return '昨天'
  return Math.floor(diff / 86_400_000) + '天前'
}
</script>

<template>
  <el-container class="main-layout">
    <el-aside :width="sidebarWidth" class="layout-aside" :class="{ collapsed: isSidebarCollapsed }">
      <div class="brand">
        <div class="brand-logo">
          <svg width="22" height="22" viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
            <circle cx="12" cy="12" r="9" stroke="currentColor" stroke-width="1.8" />
            <path d="M8 12.2l2.6 2.6L16 9" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" />
            <circle cx="12" cy="3" r="1.6" fill="currentColor" />
            <circle cx="4" cy="18" r="1.6" fill="currentColor" />
            <circle cx="20" cy="18" r="1.6" fill="currentColor" />
          </svg>
        </div>
        <div class="brand-text">
          <span class="brand-name">ConvenientSystem</span>
          <span class="brand-sub">Convenient</span>
        </div>
      </div>
      <div class="aside-search">
        <el-input
          v-model="menuKeyword"
          placeholder="搜索菜单（支持拼音）"
          :prefix-icon="Search"
          clearable
          @keyup.enter="onSearchEnter"
          @keyup.esc="menuKeyword = ''"
        />
      </div>
      <!-- 最近访问：显示在搜索框下方，菜单区上方 -->
      <div v-if="!menuKeyword.trim() && recentStore.items.length > 0" class="aside-recent">
        <div class="recent-header">
          <span class="recent-title">最近访问</span>
          <button class="recent-clear" title="清空记录" @click="recentStore.clear()">✕</button>
        </div>
        <div class="recent-list">
          <div
            v-for="item in recentStore.items"
            :key="item.path"
            class="recent-item"
            @click="router.push(item.path)"
          >
            <span class="recent-name">{{ item.title }}</span>
            <span class="recent-time">{{ formatRecentTime(item.timestamp) }}</span>
            <button
              class="recent-remove"
              title="移除"
              @click.stop="recentStore.remove(item.path)"
            >✕</button>
          </div>
        </div>
      </div>
      <!-- 只有菜单区滚动，品牌区与搜索框始终可见 -->
      <div class="aside-menu">
        <!-- 搜索态：平铺命中的菜单，第二行给出所在分组，便于区分同名页面 -->
        <el-menu v-if="menuKeyword.trim()" :default-active="activeIndex" @select="onMenuSelect">
          <el-menu-item v-for="hit in menuHits" :key="hit.index" :index="hit.index" class="is-hit">
            <span class="hit-title">{{ hit.title }}</span>
            <span v-if="hit.group" class="hit-group">{{ hit.group }}</span>
          </el-menu-item>
          <div v-if="menuHits.length === 0" class="aside-empty">没有匹配的菜单</div>
        </el-menu>
        <el-menu v-else :default-active="activeIndex" unique-opened @select="onMenuSelect">
          <el-menu-item index="/">首页</el-menu-item>
          <MenuTree :nodes="sidebarMenus" />
        </el-menu>
      </div>
    </el-aside>
    <el-container>
      <el-header height="60px" class="layout-header">
        <div class="header-left">
          <el-button
            circle
            size="small"
            :title="isSidebarCollapsed ? '展开菜单' : '收起菜单'"
            class="sidebar-toggle"
            @click="toggleSidebar"
          >
            <el-icon :size="16">
              <component :is="isSidebarCollapsed ? Expand : Fold" />
            </el-icon>
          </el-button>
          <!-- 面包屑导航（默认模式）：由路由与菜单树生成路径 -->
          <el-breadcrumb v-if="!showTabsBar" separator="/" class="header-breadcrumb">
            <el-breadcrumb-item :to="{ path: '/' }">首页</el-breadcrumb-item>
            <el-breadcrumb-item v-for="(crumb, idx) in breadcrumbs" :key="idx">
              {{ crumb.title }}
            </el-breadcrumb-item>
          </el-breadcrumb>
        </div>
        <div class="header-right">
          <!-- 刷新当前页（补偿标签栏右键刷新能力的丢失） -->
          <el-button circle size="small" class="sidebar-toggle" title="刷新当前页" @click="refreshCurrent">
            <el-icon :size="16"><Refresh /></el-icon>
          </el-button>
          <!-- 导航模式切换：面包屑 ↔ 多标签 -->
          <el-button circle size="small" class="sidebar-toggle" :title="showTabsBar ? '切换为面包屑导航' : '切换为多标签导航'" @click="toggleNavMode">
            <el-icon :size="16"><Operation /></el-icon>
          </el-button>
          <!-- 暗黑模式切换 -->
          <el-button circle size="small" class="sidebar-toggle" :title="themeStore.isDark ? '切换亮色模式' : '切换暗黑模式'" @click="themeStore.toggle()">
            <el-icon :size="16">
              <component :is="themeStore.isDark ? Sunny : Moon" />
            </el-icon>
          </el-button>
          <NoticeBell />
          <el-button v-if="lock.featureEnabled" :icon="Lock" @click="lock.lock()">立即锁屏</el-button>
          <!-- 点击头像展开：个人资料 / 清理缓存 / 退出登录 -->
          <el-dropdown trigger="click" @command="onCommand">
            <div class="header-userchip">
              <!-- 已设头像显示图片，否则回退首字母 -->
              <div class="header-avatar" :class="{ 'has-img': !!auth.avatar }">
                <img v-if="auth.avatar" :src="auth.avatar" alt="头像" />
                <template v-else>{{ avatarText }}</template>
              </div>
              <div class="header-userbox">
                <span class="header-hello">欢迎回来</span>
                <span class="header-user">{{ auth.displayName || auth.currentAccount }}</span>
              </div>
              <el-icon class="header-caret"><ArrowDown /></el-icon>
            </div>
            <template #dropdown>
              <el-dropdown-menu>
                <el-dropdown-item command="profile" :icon="User">修改个人资料</el-dropdown-item>
                <el-dropdown-item command="clearCache" :icon="Delete">清理缓存</el-dropdown-item>
                <el-dropdown-item command="logout" divided :icon="SwitchButton">退出登录</el-dropdown-item>
              </el-dropdown-menu>
            </template>
          </el-dropdown>
        </div>
      </el-header>
      <!-- 登录后重要/紧急未读通知从右侧依次弹出小窗口提醒（无未读时不弹） -->
      <NoticeAlert />
      <div v-if="showTabsBar" class="tabs-bar">
        <div
          v-for="tab in tabsStore.tabs"
          :key="tab.path"
          class="tab-item"
          :class="{ active: tabsStore.active === tab.path, pinned: tab.pinned }"
          @click="goTab(tab.path)"
          @contextmenu="onTabContextMenu($event, tab)"
        >
          <span v-if="tab.pinned" class="tab-pin-icon">📌</span>
          <span class="tab-title">{{ tab.title }}</span>
          <el-icon v-if="tab.closable && !tab.pinned" class="tab-close" @click.stop="onCloseTab(tab.path)">
            <Close />
          </el-icon>
        </div>
      </div>
      <!-- 标签页右键菜单 -->
      <Teleport to="body">
        <div
          v-if="ctxMenu.visible"
          class="tab-ctx-menu"
          :style="{ left: ctxMenu.x + 'px', top: ctxMenu.y + 'px' }"
          @click.stop
          @contextmenu.prevent
        >
          <div class="ctx-item" @click="ctxRefresh">
            <span class="ctx-icon">↻</span> 刷新
          </div>
          <div class="ctx-separator"></div>
          <div class="ctx-item" :class="{ disabled: !ctxMenu.closable || ctxMenu.pinned }" @click="ctxClose">
            <span class="ctx-icon">✕</span> 关闭
          </div>
          <div class="ctx-item" @click="ctxCloseOther">
            <span class="ctx-icon">◉</span> 关闭其它
          </div>
          <div class="ctx-item" @click="ctxCloseAll">
            <span class="ctx-icon">☐</span> 全部关闭
          </div>
          <div class="ctx-separator"></div>
          <div class="ctx-item" @click="ctxTogglePin">
            <span class="ctx-icon">{{ ctxMenu.pinned ? '📌' : '📍' }}</span>
            {{ ctxMenu.pinned ? '取消固定' : '固定标签' }}
          </div>
          <div class="ctx-item" @click="ctxCopyLink">
            <span class="ctx-icon">🔗</span> 复制链接
          </div>
          <div class="ctx-item" @click="ctxOpenInNewWindow">
            <span class="ctx-icon">↗</span> 新窗口打开
          </div>
        </div>
      </Teleport>
      <el-main class="layout-main">
        <router-view v-slot="{ Component }">
          <!-- exclude 占位视图：避免把“开发中”页面缓存下来占用名额 -->
          <transition name="route-fade" mode="out-in">
            <keep-alive :max="15" :exclude="['PlaceholderView']">
              <component :is="Component" :key="viewKey" />
            </keep-alive>
          </transition>
        </router-view>
      </el-main>
    </el-container>

    <!-- 个人资料弹窗（头像下拉打开） -->
    <ProfileDialog v-model="profileVisible" @password-changed="onPasswordChanged" />
    <!-- 命令面板 Ctrl+K -->
    <CommandPalette />
    <!-- 快捷键帮助 ? -->
    <KeyboardHelp />
  </el-container>
</template>

<script setup lang="ts">
import { computed, onMounted, onUnmounted, watch } from 'vue'
import { ElMessage } from 'element-plus'
import { useAuthStore } from '@/common/stores/auth'
import { useLockStore } from '@/common/stores/lock'
import { useMenuStore } from '@/common/stores/menu'
import { useThemeStore } from '@/common/stores/theme'
import { registerMenuRoutes, registerPublicRoutes, type PublicPageItem } from '@/router'
import { checkAuthStatus } from '@/common/api/login'
import { httpGet } from '@/api/request'
import { IS_PUBLIC_CONTEXT, IS_STANDALONE, IS_BARE_WINDOW } from '@/common/publicContext'
import LoginView from '@/common/views/LoginView.vue'
import MainLayout from '@/common/layout/MainLayout.vue'
import LockOverlay from '@/common/components/LockOverlay.vue'
import GlobalAutoTip from '@/common/components/GlobalAutoTip.vue'
import zhCn from 'element-plus/es/locale/lang/zh-cn'

const auth = useAuthStore()
const lock = useLockStore()
const menuStore = useMenuStore()
const theme = useThemeStore()
theme.init()

const loggedIn = computed(() => auth.loggedIn)
const isLocked = computed(() => lock.isLocked)

// Element Plus 表格全局默认配置：单元格内容显示不全（被省略号截断）时悬浮显示完整内容。
// 全局样式已强制表格单元格单行显示，故所有列表都需要该悬浮提示，统一在此开启，
// 无需每列再写 show-overflow-tooltip；个别列（如按钮组成的操作列）写 :show-overflow-tooltip="false" 关闭。
// 浮层外观与行为与全站统一（见 common/components/CommonTooltip.vue）：浅色 + 浅蓝底色、右上角带复制按钮。
// tooltipOptions（时间参数必须与 CommonTooltip.vue 保持一致）：
// - popperClass 与 CommonTooltip 一致（cs-tip 控外观、cs-tip-copyable 让 common/tipCopy.ts 注入复制按钮）；
// - showAfter/autoClose 向浏览器自带提示对齐：停留 0.5s 才显示（鼠标扫过多个单元格不会一路弹提示），
//   显示 5s 后自动消失；鼠标移进浮层会重新触发 onOpen，该倒计时跟着重算，不影响复制；
// - enterable + hideAfter 让鼠标能从单元格移进浮层点复制按钮（表格内部默认 hideAfter=0，一离开单元格就消失）；
// - showArrow 保持默认的 true（保留箭头）：限高与滚动条都放在浮层内的内容层上，
//   浮层本体不写 overflow，因此外侧的箭头不会被计入滚动区域（详见 styles/main.css 注释）；
// - offset 保持表格内部默认的 0：浮层必须紧贴单元格，留了空隙鼠标就无法连续移到浮层上，
//   中途会先进入上方元素并触发它自己的提示，导致永远选不中内容、点不到复制按钮。
// 优先级：列上的显式值 > 表格上的显式值 > 此处全局默认。
const dialogConfig = {
  appendToBody: true,
}

const tableConfig = {
  showOverflowTooltip: true,
  tooltipEffect: 'light',
  tooltipOptions: {
    popperClass: 'cs-tip cs-tip-copyable',
    showAfter: 500,
    autoClose: 5000,
    enterable: true,
    hideAfter: 300,
  },
}

// 页面上下文统一由 common/publicContext.ts 判定（含判据与固化时机说明）：
// - IS_STANDALONE：纯净窗口（托盘/悬浮按钮打开内部页面），仍需登录与菜单路由
// - IS_PUBLIC_CONTEXT：外部分享链接，全程免登录，不碰任何需认证的接口

// 心跳检查：每 10 秒检查登录账号是否仍处于启用状态
// 后端中间件也会独立检查每个请求中的用户状态，两层防护确保停用账户无法继续使用
let heartbeatTimer: ReturnType<typeof setInterval> | null = null
let disabledNotified = false

// 记录用户最后真实操作时间，心跳时上报给后端用于更新 LastActive
let lastActivityAt = new Date().toISOString()

async function doHeartbeat() {
  if (!auth.loggedIn) return
  try {
    const res = await checkAuthStatus(lastActivityAt)
    if (!res.enabled && !disabledNotified) {
      disabledNotified = true
      ElMessage({ message: '您的账号已被管理员停用，即将退出登录', type: 'error', duration: 3000 })
      // 账户被停用前，重新加载菜单（不让停用用户看访余下过旧菜单）
      menuStore.reset()
      setTimeout(() => { disabledNotified = false; auth.logout('account_disabled') }, 2500)
    }
  } catch {
    // 网络异常时不强制退出
  }
}

// 登录状态变化时启停心跳计时器
watch(loggedIn, (val) => {
  if (heartbeatTimer) {
    clearInterval(heartbeatTimer)
    heartbeatTimer = null
  }
  if (val && !IS_BARE_WINDOW) {
    // 检查间隔：10 秒（之前为 30 秒）
    heartbeatTimer = setInterval(doHeartbeat, 10_000)
  }
}, { immediate: true })

// 空闲检测：按系统配置 Security.SessionTimeoutMinutes，多久无操作后自动退出登录
let idleTimer: ReturnType<typeof setTimeout> | null = null
let idleNotified = false
let idleThrottle = 0
const IDLE_EVENTS = ['mousemove', 'mousedown', 'keydown', 'wheel', 'scroll', 'touchstart']

function resetIdleTimer() {
  if (idleTimer) {
    clearTimeout(idleTimer)
    idleTimer = null
  }
  if (!auth.loggedIn || auth.sessionTimeoutMinutes <= 0 || IS_BARE_WINDOW) {
    idleNotified = false
    return
  }
  idleTimer = setTimeout(() => {
    if (!auth.loggedIn || idleNotified) return
    idleNotified = true
    ElMessage({ message: '长时间未操作，已自动退出登录', type: 'warning', duration: 3000 })
    auth.logout('api_401')
  }, auth.sessionTimeoutMinutes * 60_000)
}

function onIdleActivity() {
  if (idleNotified) return
  // 节流：每秒最多重置一次计时器，避免鼠标移动频繁触发
  const now = Date.now()
  if (now - idleThrottle < 1000) return
  idleThrottle = now
  lastActivityAt = new Date().toISOString()
  resetIdleTimer()
}

// 登录状态或会话超时配置变化时，重新启动空闲计时器
watch([loggedIn, () => auth.sessionTimeoutMinutes], () => {
  idleNotified = false
  resetIdleTimer()
}, { immediate: true })

// 启动时先拉取锁屏功能开关；若刷新页面时已登录，同步启用空闲自动锁屏。
onMounted(async () => {
  // 注册全局用户活动监听，用于空闲超时退出
  for (const evt of IDLE_EVENTS) {
    window.addEventListener(evt, onIdleActivity, { passive: true })
  }

  // 公开上下文：只注册公开页面路由，菜单、锁屏、登录态全不参与
  if (IS_PUBLIC_CONTEXT) {
    try {
      const pages = await httpGet<PublicPageItem[]>('/api/Common/SysPublicPage/ListEnabled')
      if (pages) registerPublicRoutes(pages)
    } catch { /* 后端不可用时降级 */ }
    return
  }

  // 纯净窗口：直接加载菜单注册路由，跳过公开页面 API（独立窗口只打开内部页面，
  // 不需要公开路由；且 registerPublicRoutes 的重导航会与 registerMenuRoutes 的
  // 重导航产生时序冲突，导致首次打开命中 placeholder 后无法切换到目标页面）。
  // 锁屏由主窗口/原生窗口统一接管。
  if (IS_STANDALONE) {
    // 与主窗口同源（localStorage 共享），正常情况下 loggedIn 应为 true；
    // 但偶发时序差异可能导致 auth store 尚未读到持久化数据，仍尝试加载菜单（内部含缓存兜底）。
    if (!menuStore.loaded) await menuStore.load()
    registerMenuRoutes(menuStore.menus)
    return
  }

  // 主窗口：注册公开页面路由（保证深链可直达），菜单由 MainLayout 负责加载。
  try {
    const pages = await httpGet<PublicPageItem[]>('/api/Common/SysPublicPage/ListEnabled')
    if (pages) registerPublicRoutes(pages)
  } catch { /* 后端不可用时降级，不影响正常登录流程 */ }

  // 未登录时不拉取锁屏配置：/Lock/AppConfig 需要认证，此时必然 401，
  // 拿不到用户的真实开关反而会污染状态，配置改由登录成功后读取。
  if (loggedIn.value) {
    // 先拉配置：功能关闭时 loadConfig 会清掉从 localStorage 还原的历史锁屏状态；
    // 再把真正生效的锁屏补发给桌面壳（syncHost 内部已按 featureEnabled 把关），
    // 否则壳里新开的窗口不会跟着上锁。次序不能颠倒，否则会广播陈旧锁屏。
    await lock.loadConfig()
    lock.syncHost()
    lock.start()
  }
})

onUnmounted(() => {
  if (idleTimer) {
    clearTimeout(idleTimer)
    idleTimer = null
  }
  for (const evt of IDLE_EVENTS) {
    window.removeEventListener(evt, onIdleActivity)
  }
})
</script>

<template>
  <el-config-provider :locale="zhCn" :table="tableConfig" :dialog="dialogConfig">
  <!-- 全局自动悬浮提示：全系统只挂一个实例，任何页面的原生 title
       与被省略号截断的文本都自动弹统一浮层，新页面无需写任何代码 -->
  <GlobalAutoTip />
  <!-- 纯净窗口/公开页面：跳过登录门禁与主框架，只渲染目标页面内容 -->
  <div v-if="IS_BARE_WINDOW" class="standalone-page">
    <router-view />
  </div>
  <template v-else>
    <!-- 未登录显示登录页；登录后显示主布局 -->
    <LoginView v-if="!loggedIn" />
    <MainLayout v-else />

    <!-- 全局锁屏遮罩：登录后按空闲计时或手动触发 -->
    <LockOverlay v-if="loggedIn && isLocked" />
  </template>
  </el-config-provider>
</template>

<script setup lang="ts">
import { computed, onMounted, onActivated, onBeforeUnmount, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import {
  Search, Warning, User, TrendCharts, Grid,
  Calendar, ChatDotRound, Tools, Suitcase, Trophy,
  Timer, Menu, Promotion, Lock, Setting, Folder,
} from '@element-plus/icons-vue'
import { useMenuStore } from '@/common/stores/menu'
import { useAuthStore } from '@/common/stores/auth'
import { toMenuLocation } from '@/common/menuLink'
import { pinyinMatchIndex } from '@/common/pinyin'
import type { MenuNode } from '@/common/types'
import { listOnlineUsers, type OnlineUserDto } from '@/common/api/userOnline'
import { getStatistics, listLogs, getSmsTrend } from '@/sms/api/sms'
import type { SmsStatisticsDto, SmsLogDto } from '@/sms/types'
import { listEmailLogs, getEmailTrend } from '@/email/api/email'
import type { EmailLogDto } from '@/email/types'
import { getAuditTrend, getAuditLoginTrend } from '@/common/api/audit'
import { httpGet } from '@/api/request'
import type { SendTrend } from '@/common/types'
import { getLotteryHomeResults, type LotteryHomeResult, type LotteryType } from '@/common/lottery'
import LotteryNoticeDialog from '@/common/components/LotteryNoticeDialog.vue'
import LotteryIssueVerifyDialog from '@/common/components/LotteryIssueVerifyDialog.vue'
import HomeNoticeBanner from '@/common/components/HomeNoticeBanner.vue'
import BaseChart from '@/common/components/BaseChart.vue'
import CommonTooltip from '@/common/components/CommonTooltip.vue'
import type { EChartsCoreOption } from 'echarts/core'
import { formatDate } from '@/common/formatDate'

const router = useRouter()
const menuStore = useMenuStore()
const auth = useAuthStore()

// ===== 菜单数据 =====
const groups = computed(() => menuStore.collectGrouped())
const allLeaves = computed(() => menuStore.collectLeaves())

// ===== 权限检查 =====
const canViewOnline = computed(() => auth.menuCodes.includes('online-users'))
const canViewSms = computed(() => auth.menuCodes.includes('sms-log'))
const canViewEmail = computed(() => auth.menuCodes.includes('email-log'))
const canViewAudit = computed(() => auth.menuCodes.includes('audit-log'))
const canViewLottery = computed(() => auth.menuCodes.includes('lottery'))
const canViewWebMonitor = computed(() => auth.menuCodes.includes('web-monitor'))

// ===== 搜索 =====
const keyword = ref('')
/**
 * 命中的功能页面：标题或菜单编码命中即入选，标题支持拼音首字母与全拼（见 common/pinyin.ts）。
 * 按相关度排序：标题命中优于编码命中，同类里从头命中的靠前（回车直接打开第一条）。
 */
const filteredLeaves = computed(() => {
  const kw = keyword.value.trim()
  if (!kw) return []
  const ranked: { leaf: MenuNode; rank: number }[] = []
  for (const leaf of allLeaves.value) {
    const byTitle = pinyinMatchIndex(leaf.title || '', kw)
    const byName = pinyinMatchIndex(leaf.name || '', kw)
    if (byTitle < 0 && byName < 0) continue
    ranked.push({ leaf, rank: byTitle >= 0 ? byTitle : 100 + byName })
  }
  // sort 稳定，rank 相同时保持菜单原顺序
  return ranked.sort((a, b) => a.rank - b.rank).map((r) => r.leaf)
})
function handleSearchEnter() {
  if (filteredLeaves.value.length > 0) openLeaf(filteredLeaves.value[0])
}

// ===== 在线用户 =====
const onlineUsers = ref<OnlineUserDto[]>([])
async function loadOnlineUsers() {
  if (!canViewOnline.value) return
  try {
    onlineUsers.value = await listOnlineUsers()
  } catch { /* 权限不足或后端不可达，静默 */ }
}

// ===== 短信统计 =====
const smsStats = ref<SmsStatisticsDto | null>(null)
async function loadSmsStats() {
  if (!canViewSms.value) return
  try {
    smsStats.value = await getStatistics()
  } catch { /* 静默 */ }
}

// ===== 短信最近日志 =====
const smsLogs = ref<SmsLogDto[]>([])
async function loadSmsLogs() {
  if (!canViewSms.value) return
  try {
    const res = await listLogs({ page: 1, size: 6 })
    smsLogs.value = res.list
  } catch { /* 静默 */ }
}

// ===== 邮件最近日志 =====
const emailLogs = ref<EmailLogDto[]>([])
async function loadEmailLogs() {
  if (!canViewEmail.value) return
  try {
    const res = await listEmailLogs({ page: 1, size: 6 })
    emailLogs.value = res.list
  } catch { /* 静默 */ }
}

// ===== 彩票中奖结果 =====
const lotteryResults = ref<LotteryHomeResult[]>([])
async function loadLotteryResults() {
  if (!canViewLottery.value) return
  try {
    lotteryResults.value = await getLotteryHomeResults()
  } catch { /* 静默 */ }
}
function ballText(item: LotteryHomeResult, n: number): string {
  return item.positional ? String(n) : String(n).padStart(2, '0')
}

// 官网通告 · 全国中奖情况弹窗（双击彩种行触发，与走势图双击弹窗同口径）
const noticeDialogRef = ref<InstanceType<typeof LotteryNoticeDialog>>()
function openNotice(item: LotteryHomeResult) {
  if (item.issueNumber) noticeDialogRef.value?.open(item.type, item.issueNumber)
}

// 当期验奖弹窗（卡片「验奖」按钮触发，与选号记录页表头「验证奖金」共用同一组件）
const verifyDialogRef = ref<InstanceType<typeof LotteryIssueVerifyDialog>>()
/** 正在验奖的彩种（只给被点的那张卡片上 loading） */
const verifyingType = ref('')
async function openIssueVerify(item: LotteryHomeResult) {
  if (!item.issueNumber) return
  verifyingType.value = item.type
  try {
    // 按本期开奖日验奖，与卡片展示的那一期保持一致
    await verifyDialogRef.value?.open(item.type as LotteryType, item.drawDate?.slice(0, 10))
  } finally {
    verifyingType.value = ''
  }
}

// ===== 最近活动 Tab =====
const activityTab = ref<'sms' | 'email'>('email')
watch(activityTab, (tab) => {
  if (tab === 'sms' && smsLogs.value.length === 0) loadSmsLogs()
  if (tab === 'email' && emailLogs.value.length === 0) loadEmailLogs()
})

// ===== 操作趋势与时间范围筛选 =====
const trendDays = ref(7)
const smsTrend = ref<SendTrend | null>(null)
const emailTrend = ref<SendTrend | null>(null)
const auditTrend = ref<SendTrend | null>(null)
const trendLoading = ref(false)
const hasTrend = computed(() => canViewSms.value || canViewEmail.value || canViewAudit.value)

async function loadTrends() {
  if (!hasTrend.value) return
  trendLoading.value = true
  try {
    const tasks: Promise<void>[] = []
    if (canViewSms.value) {
      tasks.push(getSmsTrend(trendDays.value).then(t => { smsTrend.value = t }).catch(() => {}))
    }
    if (canViewEmail.value) {
      tasks.push(getEmailTrend(trendDays.value).then(t => { emailTrend.value = t }).catch(() => {}))
    }
    if (canViewAudit.value) {
      tasks.push(getAuditTrend(trendDays.value).then(t => { auditTrend.value = t }).catch(() => {}))
    }
    await Promise.all(tasks)
  } finally {
    trendLoading.value = false
  }
}
watch(trendDays, loadTrends)

// 操作趋势折线图配置（按权限动态增减系列）
const trendOption = computed<EChartsCoreOption>(() => {
  const base = smsTrend.value?.points ?? emailTrend.value?.points ?? auditTrend.value?.points ?? []
  const dates = base.map(p => p.date.slice(5)) // MM-DD
  const legend: string[] = []
  const series: Record<string, unknown>[] = []
  if (canViewSms.value && smsTrend.value) {
    legend.push('短信')
    series.push({
      name: '短信', type: 'line', smooth: true, showSymbol: false,
      areaStyle: { opacity: 0.08 }, itemStyle: { color: '#2563eb' },
      data: smsTrend.value.points.map(p => p.total),
    })
  }
  if (canViewEmail.value && emailTrend.value) {
    legend.push('邮件')
    series.push({
      name: '邮件', type: 'line', smooth: true, showSymbol: false,
      areaStyle: { opacity: 0.08 }, itemStyle: { color: '#7c3aed' },
      data: emailTrend.value.points.map(p => p.total),
    })
  }
  if (canViewAudit.value && auditTrend.value) {
    legend.push('审计')
    series.push({
      name: '审计', type: 'line', smooth: true, showSymbol: false,
      areaStyle: { opacity: 0.08 }, itemStyle: { color: '#f59e0b' },
      data: auditTrend.value.points.map(p => p.total),
    })
  }
  return {
    tooltip: { trigger: 'axis' },
    legend: { data: legend, top: 0 },
    grid: { left: 40, right: 16, top: 36, bottom: 28 },
    xAxis: { type: 'category', data: dates, boundaryGap: false },
    yAxis: { type: 'value', minInterval: 1 },
    series,
  }
})

// 状态分布饼图配置（合并短信+邮件+审计的成功/失败）
const statusOption = computed<EChartsCoreOption>(() => {
  let success = 0
  let failed = 0
  if (canViewSms.value && smsTrend.value) { success += smsTrend.value.totalSuccess; failed += smsTrend.value.totalFailed }
  if (canViewEmail.value && emailTrend.value) { success += emailTrend.value.totalSuccess; failed += emailTrend.value.totalFailed }
  if (canViewAudit.value && auditTrend.value) { success += auditTrend.value.totalSuccess; failed += auditTrend.value.totalFailed }
  return {
    tooltip: { trigger: 'item', formatter: '{b}: {c} ({d}%)' },
    legend: { bottom: 0 },
    series: [{
      type: 'pie', radius: ['45%', '70%'], avoidLabelOverlap: false,
      label: { show: false },
      data: [
        { value: success, name: '成功', itemStyle: { color: '#22c55e' } },
        { value: failed, name: '失败', itemStyle: { color: '#ef4444' } },
      ],
    }],
  }
})

// ===== 监控健康度（首页数据看板） =====
interface MonitorHealthFailedItem {
  id: number
  name: string
  errorMsg?: string | null
  lastCheckAt?: string | null
}
interface MonitorHealth {
  total: number
  enabledCount: number
  okCount: number
  failCount: number
  pendingCount: number
  failedTargets: MonitorHealthFailedItem[]
}
const webHealth = ref<MonitorHealth | null>(null)
const hasMonitor = computed(() => canViewWebMonitor.value)

async function loadMonitorHealth() {
  if (canViewWebMonitor.value) {
    try { webHealth.value = await httpGet<MonitorHealth>('/api/Common/WebMonitor/Health') } catch { /* 静默 */ }
  }
}

// 监控分组展示（网站监控，按权限裁剪）
const monitorGroups = computed(() => {
  const list: { label: string; health: MonitorHealth }[] = []
  if (canViewWebMonitor.value && webHealth.value) list.push({ label: '网站监控', health: webHealth.value })
  return list
})

// ===== 登录活跃度（首页数据看板，基于审计登录记录按日聚合） =====
const loginDays = ref(7)
const loginTrend = ref<SendTrend | null>(null)
const loginLoading = ref(false)

async function loadLoginTrend() {
  if (!canViewAudit.value) return
  loginLoading.value = true
  try {
    loginTrend.value = await getAuditLoginTrend(loginDays.value)
  } catch { /* 静默 */ } finally {
    loginLoading.value = false
  }
}
watch(loginDays, loadLoginTrend)

// 登录活跃堆叠柱状图（成功绿/失败红）
const loginOption = computed<EChartsCoreOption>(() => {
  const points = loginTrend.value?.points ?? []
  return {
    tooltip: { trigger: 'axis' },
    legend: { data: ['登录成功', '登录失败'], top: 0 },
    grid: { left: 40, right: 16, top: 36, bottom: 28 },
    xAxis: { type: 'category', data: points.map(p => p.date.slice(5)) },
    yAxis: { type: 'value', minInterval: 1 },
    series: [
      { name: '登录成功', type: 'bar', stack: 'login', itemStyle: { color: '#22c55e' }, data: points.map(p => p.success) },
      { name: '登录失败', type: 'bar', stack: 'login', itemStyle: { color: '#ef4444' }, data: points.map(p => p.failed) },
    ],
  }
})

// ===== 统计汇总 =====
const stats = computed(() => {
  let internal = 0
  let external = 0
  allLeaves.value.forEach(l => {
    if (l.external === true || /^https?:\/\//i.test(l.page || '')) external++
    else internal++
  })
  return { internal, external, total: allLeaves.value.length }
})

// ===== 欢迎语与时钟 =====
const now = ref(new Date())
let timer: ReturnType<typeof setInterval> | null = null
let trendTimer: ReturnType<typeof setInterval> | null = null

const greeting = computed(() => {
  const h = now.value.getHours()
  if (h < 6) return '夜深了'
  if (h < 9) return '早上好'
  if (h < 12) return '上午好'
  if (h < 14) return '中午好'
  if (h < 18) return '下午好'
  return '晚上好'
})
const dateText = computed(() => {
  const d = now.value
  const week = ['日', '一', '二', '三', '四', '五', '六'][d.getDay()]
  return `${d.getFullYear()}年${d.getMonth() + 1}月${d.getDate()}日 星期${week}`
})
const timeText = computed(() => {
  const d = now.value
  const h = String(d.getHours()).padStart(2, '0')
  const m = String(d.getMinutes()).padStart(2, '0')
  return `${h}:${m}`
})

// ===== 辅助函数 =====
function smsStatusText(s: number): string {
  return s === 0 ? '待发送' : s === 1 ? '成功' : '失败'
}
function smsStatusType(s: number): string {
  return s === 1 ? 'success' : s === 2 ? 'danger' : 'info'
}
function emailStatusText(s: number): string {
  return s === 1 ? '成功' : '失败'
}
function emailStatusType(s: number): string {
  return s === 1 ? 'success' : 'danger'
}
function relTime(iso: string): string {
  const diff = Date.now() - new Date(iso).getTime()
  if (diff < 0) return '刚刚'
  const s = Math.floor(diff / 1000)
  if (s < 60) return '刚刚'
  const m = Math.floor(s / 60)
  if (m < 60) return `${m}分钟前`
  const h = Math.floor(m / 60)
  if (h < 24) return `${h}小时前`
  const d = Math.floor(h / 24)
  return `${d}天前`
}
function maskPhone(phone: string): string {
  if (phone.length < 7) return phone
  return phone.slice(0, 3) + '****' + phone.slice(-4)
}

// ===== 生命周期 =====
/** 轻量面板数据：登录后实时性要求高，每 30s 轮询刷新 */
function loadRealtime() {
  loadOnlineUsers()
  loadSmsStats()
  loadSmsLogs()
  loadEmailLogs()
  loadLotteryResults()
  loadMonitorHealth()
}

onMounted(() => {
  if (!menuStore.loaded) menuStore.load()
  // 并行加载所有面板数据
  loadRealtime()
  loadTrends()
  loadLoginTrend()
  timer = setInterval(() => {
    now.value = new Date()
    loadRealtime()
  }, 30_000)
  // 趋势图为按日聚合数据，每 5 分钟刷新一次即可
  trendTimer = setInterval(() => {
    loadTrends()
    loadLoginTrend()
  }, 300_000)
})
// keep-alive 缓存切回首页时也刷新一次，避免看到过期数据
onActivated(() => loadRealtime())
onBeforeUnmount(() => {
  if (timer) clearInterval(timer)
  if (trendTimer) clearInterval(trendTimer)
})

function openLeaf(node: MenuNode) {
  if (!node.page) return
  void router.push(toMenuLocation(node))
}

// ===== 分组图标 =====
// 顶部模块图标映射：使用 Element Plus SVG 图标，保证端正、尺寸一致
const groupIcons: Record<string, unknown> = {
  '昀晗': Calendar,
  '短信管理': ChatDotRound,
  '开发工具': Tools,
  '常用工具': Suitcase,
  '国家公益事业': Trophy,
  '任务调度': Timer,
  '菜单管理': Menu,
  '邮件管理': Promotion,
  '权限管理': Lock,
  '系统管理': Setting,
}
function getGroupIcon(title: string): unknown {
  return groupIcons[title] || Folder
}
</script>

<template>
  <div class="home-page">

    <!-- ===== Hero 欢迎区（集成搜索） ===== -->
    <section class="hero">
      <div class="hero-deco" aria-hidden="true"></div>
      <div class="hero-left">
        <div class="hero-greeting">{{ greeting }}，{{ auth.displayName || auth.currentAccount || '管理员' }}</div>
        <div class="hero-date">{{ dateText }}</div>
        <div class="hero-search">
          <el-input
            v-model="keyword"
            placeholder="搜索功能名称或拼音首字母，回车快速打开…"
            size="large"
            clearable
            @keyup.enter="handleSearchEnter"
          >
            <template #prefix>
              <el-icon><Search /></el-icon>
            </template>
          </el-input>
          <div v-if="filteredLeaves.length" class="search-results">
            <div
              v-for="leaf in filteredLeaves.slice(0, 8)"
              :key="leaf.page || leaf.title"
              class="search-result-item"
              @click="openLeaf(leaf)"
            >
              <span class="search-result-icon">{{ (leaf.title || '').slice(0, 1) }}</span>
              <span class="search-result-title">{{ leaf.title }}</span>
            </div>
          </div>
        </div>
      </div>
      <div class="hero-right">
        <div class="hero-time">{{ timeText }}</div>
        <div class="hero-time-label">系统时间</div>
      </div>
    </section>
    

    <!-- ===== 系统公告横幅（未读公告轮播，点击查看详情并标记已读） ===== -->
    <HomeNoticeBanner />

    <!-- ===== KPI 统计卡片 ===== -->
    <div class="kpi-row">
      <div v-if="canViewOnline" class="kpi-card">
        <div class="kpi-icon kpi-online"><el-icon :size="22"><User /></el-icon></div>
        <div class="kpi-body">
          <div class="kpi-value">{{ onlineUsers.length }}</div>
          <div class="kpi-label">在线用户</div>
        </div>
      </div>
      <div v-if="canViewSms && smsStats" class="kpi-card">
        <div class="kpi-icon kpi-sms"><el-icon :size="22"><ChatDotRound /></el-icon></div>
        <div class="kpi-body">
          <div class="kpi-value">{{ smsStats.todayCount }}</div>
          <div class="kpi-label">今日短信</div>
        </div>
      </div>
      <div v-if="canViewSms && smsStats" class="kpi-card">
        <div class="kpi-icon kpi-rate"><el-icon :size="22"><TrendCharts /></el-icon></div>
        <div class="kpi-body">
          <div class="kpi-value">{{ smsStats.successRate.toFixed(1) }}%</div>
          <div class="kpi-label">短信成功率</div>
        </div>
      </div>
      <div v-if="canViewEmail" class="kpi-card">
        <div class="kpi-icon kpi-email"><el-icon :size="22"><Promotion /></el-icon></div>
        <div class="kpi-body">
          <div class="kpi-value">{{ emailLogs.length }}</div>
          <div class="kpi-label">近期邮件</div>
        </div>
      </div>
      <div class="kpi-card">
        <div class="kpi-icon kpi-func"><el-icon :size="22"><Grid /></el-icon></div>
        <div class="kpi-body">
          <div class="kpi-value">{{ stats.total }}</div>
          <div class="kpi-label">功能入口</div>
        </div>
      </div>
    </div>

    <!-- ===== 双栏面板：在线用户 + 登录活跃度 ===== -->
    <div v-if="canViewOnline || canViewAudit" class="panels-row">
      <!-- 在线用户 -->
      <div v-if="canViewOnline" class="panel panel-online">
        <div class="panel-header">
          <span class="panel-title">在线用户</span>
          <span class="panel-count">{{ onlineUsers.length }} 人</span>
        </div>
        <div class="panel-body">
          <div v-if="onlineUsers.length === 0" class="panel-empty">暂无在线用户</div>
          <div v-for="u in onlineUsers.slice(0, 8)" :key="u.userId" class="online-item">
            <div class="online-avatar">{{ (u.displayName || u.account || '?').slice(0, 1).toUpperCase() }}</div>
            <div class="online-info">
              <div class="online-name">{{ u.displayName || u.account }}</div>
              <div class="online-meta">{{ relTime(u.lastActive) }} · {{ u.ip }}</div>
            </div>
            <div class="online-login-time">
              <div>活跃 {{ formatDate(u.lastActive) }}</div>
              <div>登录 {{ formatDate(u.loginTime) }}</div>
            </div>
          </div>
        </div>
      </div>

      <!-- 登录活跃度 -->
      <div v-if="canViewAudit" class="panel chart-panel chart-login">
        <div class="panel-header">
          <span class="panel-title">登录活跃度</span>
          <el-radio-group v-model="loginDays" size="small">
            <el-radio-button :value="7">近7天</el-radio-button>
            <el-radio-button :value="30">近30天</el-radio-button>
          </el-radio-group>
        </div>
        <div v-loading="loginLoading" class="panel-body">
          <BaseChart :option="loginOption" height="260px" />
        </div>
      </div>
    </div>

    <!-- ===== 监控健康度 + 最近活动 ===== -->
    <div v-if="hasMonitor || canViewSms || canViewEmail" class="charts-row">
      <div v-if="hasMonitor" class="panel chart-panel chart-health">
        <div class="panel-header">
          <span class="panel-title">监控健康度</span>
        </div>
        <div class="panel-body">
          <div v-if="monitorGroups.length === 0" class="panel-empty">暂无监控数据</div>
          <div v-for="g in monitorGroups" :key="g.label" class="health-group">
            <div class="health-group-title">{{ g.label }}</div>
            <div class="health-stats">
              <span class="health-stat health-ok">正常 {{ g.health.okCount }}</span>
              <span class="health-stat health-fail">异常 {{ g.health.failCount }}</span>
              <span class="health-stat health-pending">未探测 {{ g.health.pendingCount }}</span>
              <span class="health-stat health-total">共 {{ g.health.total }} 个（启用 {{ g.health.enabledCount }}）</span>
            </div>
            <div v-if="g.health.failedTargets.length > 0" class="health-failed-list">
              <CommonTooltip
                v-for="t in g.health.failedTargets"
                :key="t.id"
                :content="t.errorMsg || '未知原因'"
              >
                <el-tag type="danger" effect="plain" size="small" class="health-failed-tag">{{ t.name }}</el-tag>
              </CommonTooltip>
            </div>
          </div>
        </div>
      </div>

      <!-- 最近活动（短信/邮件 Tab） -->
      <div v-if="canViewSms || canViewEmail" class="panel panel-activity">
        <div class="panel-header">
          <el-tabs v-model="activityTab" class="activity-tabs">
            <el-tab-pane v-if="canViewEmail" label="邮件记录" name="email" />
            <el-tab-pane v-if="canViewSms" label="短信记录" name="sms" />
          </el-tabs>
        </div>
        <div class="panel-body">
          <!-- 邮件记录 -->
          <template v-if="activityTab === 'email'">
            <div v-if="emailLogs.length === 0" class="panel-empty">暂无邮件记录</div>
            <div v-for="log in emailLogs" :key="log.id" class="log-item">
              <div class="log-main">
                <el-tag :type="emailStatusType(log.status)" size="small" effect="light">{{ emailStatusText(log.status) }}</el-tag>
                <span class="log-subject">{{ log.subject || '(无主题)' }}</span>
              </div>
              <div class="log-time">{{ relTime(log.createTime) }}</div>
            </div>
          </template>
          <!-- 短信记录 -->
          <template v-if="activityTab === 'sms'">
            <div v-if="smsLogs.length === 0" class="panel-empty">暂无短信记录</div>
            <div v-for="log in smsLogs" :key="log.id" class="log-item">
              <div class="log-main">
                <el-tag :type="smsStatusType(log.status)" size="small" effect="light">{{ smsStatusText(log.status) }}</el-tag>
                <span class="log-phone">{{ maskPhone(log.phone) }}</span>
              </div>
              <div class="log-time">{{ relTime(log.createTime) }}</div>
            </div>
          </template>
        </div>
      </div>
    </div>

    <!-- ===== 彩票中奖结果 ===== -->
    <div v-if="canViewLottery && lotteryResults.length > 0" class="lottery-panel">
      <div class="panel-header">
        <span class="panel-title">彩票中奖结果</span>
        <span class="panel-count">最新一期</span>
        <span class="lottery-hint">双击可查看官网通告</span>
      </div>
      <div class="panel-body lottery-body">
        <div v-for="item in lotteryResults" :key="item.type" class="lottery-item"
          :class="{ clickable: !!item.issueNumber }" @dblclick="openNotice(item)">
          <div class="lottery-head">
            <span class="lottery-name">{{ item.name }}</span>
            <span v-if="item.issueNumber" class="lottery-issue">第 {{ item.issueNumber }} 期 · {{ formatDate(item.drawDate).slice(0, 10) }}</span>
            <span v-else class="lottery-issue">暂无开奖数据</span>
          </div>
          <div class="lottery-balls">
            <template v-if="item.issueNumber">
              <span v-for="(n, i) in item.front" :key="'f' + i" class="lottery-ball ball-front">{{ ballText(item, n) }}</span>
              <template v-if="item.back.length > 0">
                <span class="lottery-plus">+</span>
                <span v-for="(n, i) in item.back" :key="'b' + i" class="lottery-ball ball-back">{{ ballText(item, n) }}</span>
              </template>
            </template>
            <span v-else class="lottery-none">—</span>
          </div>
          <div class="lottery-mine">
            <template v-if="item.issueNumber">
              <span v-if="item.betCount === 0" class="lottery-none">本期未参与选号</span>
              <CommonTooltip v-else>
                <template #content>
                  <div v-for="(b, i) in item.bets" :key="i" class="lottery-tip-row">{{ b.pick }} → {{ b.prize }}</div>
                </template>
                <el-tag v-if="item.winCount > 0" type="danger" effect="light">中奖 {{ item.winCount }}/{{ item.betCount }} 注</el-tag>
                <el-tag v-else type="info" effect="plain">未中奖（{{ item.betCount }} 注）</el-tag>
              </CommonTooltip>
              <!-- 验奖：逐注命中高亮 + 税前税后奖金 + 全国奖级表 -->
              <el-button link type="primary" size="small" :loading="verifyingType === item.type"
                @click.stop="openIssueVerify(item)">验奖</el-button>
            </template>
            <span v-else class="lottery-none">—</span>
          </div>
        </div>
      </div>
      <!-- 官网通告 · 全国中奖情况弹窗（双击彩种行触发，共享组件） -->
      <LotteryNoticeDialog ref="noticeDialogRef" />
      <!-- 当期验奖弹窗（卡片「验奖」按钮触发，共享组件） -->
      <LotteryIssueVerifyDialog ref="verifyDialogRef" />
    </div>

    <!-- ===== 数据统计图表 ===== -->
    <div v-if="hasTrend" class="charts-row">
      <div class="panel chart-panel chart-trend">
        <div class="panel-header">
          <span class="panel-title">操作趋势</span>
          <el-radio-group v-model="trendDays" size="small">
            <el-radio-button :value="7">近7天</el-radio-button>
            <el-radio-button :value="30">近30天</el-radio-button>
            <el-radio-button :value="90">近90天</el-radio-button>
          </el-radio-group>
        </div>
        <div v-loading="trendLoading" class="panel-body">
          <BaseChart :option="trendOption" height="280px" />
        </div>
      </div>
      <div class="panel chart-panel chart-status">
        <div class="panel-header">
          <span class="panel-title">状态分布</span>
        </div>
        <div v-loading="trendLoading" class="panel-body">
          <BaseChart :option="statusOption" height="280px" />
        </div>
      </div>
    </div>

    <!-- ===== 功能分组 ===== -->
    <div v-if="groups.length" class="home-groups">
      <div v-for="group in groups" :key="group.title" class="home-group">
        <div class="home-group-header">
          <span class="home-group-icon"><el-icon :size="16"><component :is="getGroupIcon(group.title)" /></el-icon></span>
          <span class="home-group-title">{{ group.title }}</span>
          <span class="home-group-count">{{ group.leaves.length }} 项</span>
        </div>
        <div class="home-grid">
          <div
            v-for="leaf in group.leaves"
            :key="leaf.page || leaf.title"
            class="home-card"
            @click="openLeaf(leaf)"
          >
            <div class="home-card-icon">{{ (leaf.title || '').slice(0, 1) }}</div>
            <div class="home-card-body">
              <div class="home-card-title">{{ leaf.title }}</div>
            </div>
            <div v-if="leaf.external === true || /^https?:\/\//i.test(leaf.page || '')" class="home-card-badge">
              外链
            </div>
          </div>
        </div>
      </div>
    </div>
    <div v-else class="home-empty">
      <el-icon :size="48"><Warning /></el-icon>
      <p>暂无可用功能，请联系管理员分配菜单权限</p>
    </div>
  </div>
</template>


<style scoped>
.home-page {
  padding: 20px;
  max-width: 1280px;
  margin: 0 auto;
}

/* ===== Hero 欢迎区 ===== */
.hero {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 24px;
  padding: 26px 32px 30px;
  border-radius: 18px;
  background: var(--brand-gradient);
  color: #fff;
  margin-bottom: 18px;
  box-shadow: 0 10px 28px rgba(59, 130, 246, 0.28);
  position: relative;
}
/* 装饰圆单独成层裁剪，避免 hero 整体 overflow:hidden 裁掉搜索结果下拉 */
.hero-deco {
  position: absolute;
  inset: 0;
  border-radius: inherit;
  overflow: hidden;
  pointer-events: none;
}
.hero-deco::before,
.hero-deco::after {
  content: '';
  position: absolute;
  border-radius: 50%;
}
.hero-deco::before { width: 260px; height: 260px; right: -70px; top: -90px; background: rgba(255, 255, 255, 0.09); }
.hero-deco::after { width: 150px; height: 150px; right: 130px; bottom: -70px; background: rgba(255, 255, 255, 0.06); }
.hero-left { flex: 1; min-width: 0; position: relative; z-index: 1; }
.hero-greeting { font-size: 26px; font-weight: 700; letter-spacing: 0.5px; margin-bottom: 6px; }
.hero-date { font-size: 13px; opacity: 0.85; margin-bottom: 18px; }
.hero-search { max-width: 460px; position: relative; }
.hero-search :deep(.el-input__wrapper) {
  background: rgba(255, 255, 255, 0.16);
  box-shadow: none;
  border-radius: 10px;
}
.hero-search :deep(.el-input__wrapper.is-focus) { background: rgba(255, 255, 255, 0.26); }
.hero-search :deep(.el-input__inner) { color: #fff; }
.hero-search :deep(.el-input__inner::placeholder) { color: rgba(255, 255, 255, 0.7); }
.hero-search :deep(.el-input__prefix .el-icon) { color: rgba(255, 255, 255, 0.9); }
.hero-search :deep(.el-input__clear) { background: transparent; color: rgba(255, 255, 255, 0.9); }
.hero-right { flex-shrink: 0; text-align: right; position: relative; z-index: 1; }
.hero-time {
  font-size: 46px;
  font-weight: 700;
  letter-spacing: 2px;
  line-height: 1.1;
  font-variant-numeric: tabular-nums;
  text-shadow: 0 2px 10px rgba(0, 0, 0, 0.15);
}
.hero-time-label { font-size: 12px; opacity: 0.8; letter-spacing: 6px; margin-top: 2px; }

/* ===== KPI 统计卡片 ===== */
.kpi-row {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
  gap: 14px;
  margin-bottom: 18px;
}
.kpi-card {
  background: #fff;
  border: 1px solid var(--border);
  border-radius: 14px;
  padding: 18px 20px;
  display: flex;
  align-items: center;
  gap: 14px;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.04);
  transition: transform 0.18s ease, box-shadow 0.18s ease;
}
.kpi-card:hover {
  transform: translateY(-3px);
  box-shadow: 0 8px 20px rgba(0, 0, 0, 0.08);
}
.kpi-icon {
  width: 46px;
  height: 46px;
  border-radius: 12px;
  display: flex;
  align-items: center;
  justify-content: center;
  color: #fff;
  flex-shrink: 0;
  box-shadow: 0 4px 10px rgba(16, 24, 40, 0.15);
}
.kpi-icon.kpi-online { background: linear-gradient(135deg, #22d3ee, #0891b2); }
.kpi-icon.kpi-sms { background: linear-gradient(135deg, #38bdf8, #2563eb); }
.kpi-icon.kpi-rate { background: linear-gradient(135deg, #60a5fa, #3b82f6); }
.kpi-icon.kpi-email { background: linear-gradient(135deg, #818cf8, #4f46e5); }
.kpi-icon.kpi-func { background: linear-gradient(135deg, #0ea5e9, #0284c7); }
.kpi-value { font-size: 24px; font-weight: 700; color: var(--text-main); line-height: 1.2; font-variant-numeric: tabular-nums; }
.kpi-label { font-size: 13px; color: var(--text-sub); margin-top: 2px; }

/* ===== 双栏面板 ===== */
.panels-row {
  display: flex;
  gap: 16px;
  margin-bottom: 18px;
}
.panel {
  flex: 1;
  background: #fff;
  border: 1px solid var(--border);
  border-radius: 14px;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.04);
  overflow: hidden;
  min-width: 0;
}
.panel-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 13px 18px;
  border-bottom: 1px solid var(--border);
  background: linear-gradient(180deg, #fbfcfd, #fff);
}
/* 面板标题前加品牌色竖条点缀 */
.panel-title {
  font-size: 15px;
  font-weight: 600;
  color: var(--text-main);
  display: inline-flex;
  align-items: center;
  gap: 8px;
}
.panel-title::before {
  content: '';
  width: 4px;
  height: 14px;
  border-radius: 2px;
  background: var(--brand-gradient);
}
.panel-count { font-size: 12px; color: var(--text-sub); background: var(--brand-50); padding: 2px 8px; border-radius: 10px; }
.lottery-hint { font-size: 12px; color: var(--text-sub); margin-left: auto; }
/* 彩票面板头部：标题与“最新一期”靠左，双击提示靠右 */
.lottery-panel .panel-count { margin-left: 8px; margin-right: auto; }
.panel-body { padding: 8px 18px 16px; max-height: 320px; overflow-y: auto; }

/* ===== 彩票中奖结果 ===== */
.lottery-panel {
  background: #fff;
  border: 1px solid var(--border);
  border-radius: 14px;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.04);
  overflow: hidden;
  margin-bottom: 18px;
}
.lottery-body { max-height: none; padding: 4px 18px 12px; }
.lottery-item {
  display: flex;
  align-items: center;
  gap: 16px;
  padding: 10px 8px;
  margin: 0 -8px;
  border-radius: 10px;
  border-bottom: 1px dashed var(--border);
  transition: background 0.15s;
}
.lottery-item:last-child { border-bottom: none; }
.lottery-item.clickable { cursor: pointer; }
.lottery-item.clickable:hover { background: #f6faf9; }
.lottery-head { width: auto; flex-shrink: 0; display: flex; flex-direction: column; gap: 2px; }
.lottery-name { font-size: 14px; font-weight: 600; color: var(--text-main); }
.lottery-issue { font-size: 12px; color: var(--text-sub); white-space: nowrap; }
.lottery-balls { flex: 1; min-width: 0; display: flex; align-items: center; gap: 6px; flex-wrap: wrap; }
.lottery-ball {
  width: 28px;
  height: 28px;
  border-radius: 50%;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  font-size: 13px;
  font-weight: 600;
  color: #fff;
}
.ball-front { background: linear-gradient(135deg, #f05a5b, #d62828); box-shadow: 0 2px 6px rgba(230, 57, 58, 0.35); }
.ball-back { background: linear-gradient(135deg, #5cadff, #2b8ce6); box-shadow: 0 2px 6px rgba(64, 158, 255, 0.35); }
.lottery-plus { color: var(--text-sub); font-size: 13px; }
.lottery-mine { flex-shrink: 0; display: flex; align-items: center; gap: 8px; }
.lottery-none { font-size: 13px; color: var(--text-sub); }
.lottery-tip-row { font-size: 12px; line-height: 1.8; }

/* ===== 数据统计图表 ===== */
.charts-row {
  display: flex;
  gap: 16px;
  margin-bottom: 20px;
}
.chart-trend { flex: 2; }
.chart-status { flex: 1; }
.chart-health { flex: 1; }
.chart-login { flex: 2; }
/* 最近活动面板与监控健康度同行时保持 1:2 宽比，与上一行对齐 */
.charts-row .panel-activity { flex: 2; }
.chart-panel .panel-body { max-height: none; overflow: visible; padding: 12px 12px 8px; }

/* 监控健康度分组 */
.health-group { padding: 8px 6px; }
.health-group + .health-group { border-top: 1px dashed var(--border); }
.health-group-title {
  font-weight: 600;
  font-size: 13px;
  color: var(--el-text-color-primary);
  margin-bottom: 8px;
}
.health-stats {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  font-size: 12px;
}
.health-stat {
  padding: 3px 10px;
  border-radius: 12px;
  font-weight: 500;
}
.health-ok { background: #f0fdf4; color: #16a34a; }
.health-fail { background: #fef2f2; color: #dc2626; }
.health-pending { background: #f5f5f5; color: #737373; }
.health-total { background: #eff6ff; color: #2563eb; }
.health-failed-list {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  margin-top: 8px;
}
.health-failed-tag { cursor: help; }
@media (max-width: 900px) {
  .charts-row { flex-direction: column; }
}

/* 在线用户 */
.online-item {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 8px 6px;
  margin: 0 -6px;
  border-radius: 8px;
  border-bottom: 1px solid #f5f5f5;
  transition: background 0.15s;
}
.online-item:hover { background: #f6faf9; }
.online-item:last-child { border-bottom: none; }
.online-avatar {
  width: 36px;
  height: 36px;
  border-radius: 50%;
  background: var(--brand-gradient);
  color: #fff;
  font-size: 14px;
  font-weight: 600;
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
}
.online-info { flex: 1; min-width: 0; }
.online-name { font-size: 14px; font-weight: 500; color: var(--text-main); }
.online-meta { font-size: 12px; color: var(--text-sub); }
.online-login-time {
  font-size: 12px;
  color: var(--text-sub);
  flex-shrink: 0;
  white-space: nowrap;
  text-align: right;
  line-height: 1.6;
}

/* 活动日志 */
.activity-tabs { width: 100%; }
.activity-tabs :deep(.el-tabs__header) { margin: 0; }
.activity-tabs :deep(.el-tabs__nav-wrap::after) { display: none; }
.activity-tabs :deep(.el-tabs__item) { font-size: 14px; height: 36px; padding: 0 16px; }

.log-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  padding: 8px 6px;
  margin: 0 -6px;
  border-radius: 8px;
  border-bottom: 1px solid #f5f5f5;
  transition: background 0.15s;
}
.log-item:hover { background: #f6faf9; }
.log-item:last-child { border-bottom: none; }
.log-main { display: flex; align-items: center; gap: 8px; min-width: 0; }
.log-phone, .log-subject {
  font-size: 13px;
  color: var(--text-main);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.log-subject { max-width: 200px; }
.log-time { font-size: 12px; color: var(--text-sub); flex-shrink: 0; }

.panel-empty {
  padding: 40px 0;
  text-align: center;
  color: var(--text-sub);
  font-size: 14px;
}

/* ===== 搜索结果下拉 ===== */
.search-results {
  position: absolute;
  top: 100%;
  left: 0;
  right: 0;
  margin-top: 4px;
  background: #fff;
  border-radius: 10px;
  box-shadow: var(--shadow-md);
  z-index: 100;
  overflow: hidden;
}
.search-result-item {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 10px 16px;
  cursor: pointer;
  transition: background 0.12s;
}
.search-result-item:hover { background: var(--brand-50); }
.search-result-icon {
  width: 32px;
  height: 32px;
  border-radius: 8px;
  background: var(--brand-gradient);
  color: #fff;
  font-size: 14px;
  font-weight: 600;
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
}
.search-result-title { font-size: 14px; color: var(--text-main); }

/* ===== 功能分组 ===== */
.home-groups { display: flex; flex-direction: column; gap: 28px; }
.home-group-header {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 14px;
  padding-left: 2px;
  line-height: 28px;
}
.home-group-icon {
  width: 28px;
  height: 28px;
  border-radius: 8px;
  background: var(--brand-50);
  color: var(--brand);
  display: inline-flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
}
.home-group-title {
  font-size: 16px;
  font-weight: 600;
  color: var(--text-main);
  line-height: 28px;
}
.home-group-count {
  font-size: 12px;
  line-height: 18px;
  color: var(--text-sub);
  background: var(--brand-50);
  padding: 0 8px;
  border-radius: 10px;
  height: 18px;
  display: inline-flex;
  align-items: center;
  flex-shrink: 0;
}

/* ===== 功能卡片网格 ===== */
.home-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(160px, 1fr));
  gap: 14px;
}
.home-card {
  background: #fff;
  border: 1px solid var(--border);
  border-radius: 14px;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.04);
  padding: 22px 16px;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 12px;
  cursor: pointer;
  transition: transform 0.18s ease, box-shadow 0.18s ease, border-color 0.18s ease;
  position: relative;
}
.home-card:hover {
  transform: translateY(-3px);
  border-color: var(--brand);
  box-shadow: 0 8px 22px rgba(59, 130, 246, 0.15);
}
.home-card-icon {
  width: 52px;
  height: 52px;
  border-radius: 14px;
  color: #fff;
  font-size: 22px;
  font-weight: 600;
  display: flex;
  align-items: center;
  justify-content: center;
  box-shadow: 0 6px 16px rgba(16, 24, 40, 0.12);
}
.home-card:nth-child(6n + 1) .home-card-icon { background: linear-gradient(135deg, #60a5fa, #3b82f6); }
.home-card:nth-child(6n + 2) .home-card-icon { background: linear-gradient(135deg, #38bdf8, #2563eb); }
.home-card:nth-child(6n + 3) .home-card-icon { background: linear-gradient(135deg, #22d3ee, #0891b2); }
.home-card:nth-child(6n + 4) .home-card-icon { background: linear-gradient(135deg, #818cf8, #4f46e5); }
.home-card:nth-child(6n + 5) .home-card-icon { background: linear-gradient(135deg, #0ea5e9, #0284c7); }
.home-card:nth-child(6n + 6) .home-card-icon { background: linear-gradient(135deg, #3b82f6, #1d4ed8); }
.home-card-body { text-align: center; }
.home-card-title { font-size: 14px; font-weight: 500; color: var(--text-main); line-height: 1.4; }
.home-card-badge {
  position: absolute;
  top: 8px;
  right: 8px;
  font-size: 10px;
  color: var(--text-sub);
  background: #f0f2f5;
  padding: 1px 6px;
  border-radius: 6px;
}

/* ===== 空状态 ===== */
.home-empty { text-align: center; padding: 80px 20px; color: var(--text-sub); }
.home-empty p { margin-top: 16px; font-size: 15px; }

/* ===== 响应式 ===== */
@media (max-width: 768px) {
  .hero { flex-direction: column; align-items: stretch; text-align: center; gap: 12px; }
  .hero-right { text-align: center; }
  .hero-search { max-width: none; }
  .panels-row { flex-direction: column; }
}
</style>

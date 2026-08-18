<script setup lang="ts">
import { ref, shallowRef, computed, watch, onMounted, onBeforeUnmount, nextTick, h, type Component } from 'vue'
import { ElMessage, ElCheckbox, ElButton, ElIcon, TableV2SortOrder, type FormInstance, type FormRules } from 'element-plus'
import { CopyDocument, Refresh, Search, Document, Picture, VideoCamera, Headset, Box, Setting, Cpu, Tickets, Delete } from '@element-plus/icons-vue'
import { httpGet, httpPost, httpDelete } from '@/api/request'
import { formatDate } from '@/common/formatDate'
import CommonDataTable, { type DataTableColumn } from '@/common/components/CommonDataTable.vue'
import { confirmAndRun } from '@/common/utils/confirm'
import BaseChart from '@/common/components/BaseChart.vue'
import CommonTooltip from '@/common/components/CommonTooltip.vue'
import CommonDialog from '@/common/components/CommonDialog.vue'
import { isHostAvailable, hostOpenLocation, hostOpenRecycleBin } from '@/common/hostFileBridge'
import type { EChartsCoreOption } from 'echarts/core'
import { usePermission } from '@/common/composables/usePermission'

const { has } = usePermission()

// ── 类型定义（与后端 HostMonitorModels.cs 对应） ──
interface MonitorTarget {
  id: number
  name: string
  metricType: string          // DISK/MEM/CPU/SVC/HOST
  hostAddress: string | null  // 目标电脑 IP/主机名（空=本机，仅 HOST）
  isLocal: boolean            // 目标是否实际为本机（后端识别：空地址/环回/计算机名/本机网卡 IP）
  authAccount: string | null  // 远程采集账号（不含密码）
  metricsJson: string | null  // 整机概览最近指标快照 JSON（仅 HOST）
  driveLetter: string | null
  serviceNames: string | null
  thresholdPercent: number | null
  timeoutSeconds: number
  intervalMinutes: number
  enabled: boolean
  notifyEmail: boolean
  lastStatus: number | null   // null=未探测 1=正常 2=异常
  lastValue: number | null    // 磁盘已用%/内存使用率%/CPU 使用率%；SVC 为运行中服务数
  lastErrorMsg: string | null
  lastCheckAt: string | null
  remark: string | null
}

/** 整机概览指标快照（解析自 metricsJson / Metrics 接口 latest） */
interface HostMetrics {
  cpuPercent: number | null
  cpuCores: number | null
  memoryPercent: number | null
  memoryTotalGb: number | null
  memoryUsedGb: number | null
  osName: string | null
  uptimeHours: number | null
  processCount: number | null
  netInKbps: number | null
  netOutKbps: number | null
  diskReadMbPerSec: number | null
  diskWriteMbPerSec: number | null
  disks: { drive: string; usedPercent: number; totalGb: number; freeGb: number }[]
  checkedAt: string
}

/** 时间序列历史点（图表用） */
interface HostMetricsPoint {
  checkAt: string
  cpuPercent: number | null
  memoryPercent: number | null
  memoryUsedGb: number | null
  netInKbps: number | null
  netOutKbps: number | null
  diskReadMbPerSec: number | null
  diskWriteMbPerSec: number | null
}

interface HostMetricsData {
  latest: HostMetrics | null
  history: HostMetricsPoint[]
}

interface MonitorLog {
  id: number
  status: number
  value: number | null
  errorMsg: string | null
  checkAt: string
}

const METRIC_OPTIONS = [
  { label: '整机概览（推荐，支持远程 IP）', value: 'HOST' },
  { label: '磁盘已用率', value: 'DISK' },
  { label: '内存使用率', value: 'MEM' },
  { label: 'CPU 使用率', value: 'CPU' },
  { label: 'Windows 服务', value: 'SVC' },
]

function metricLabel(type: string): string {
  return METRIC_OPTIONS.find(m => m.value === type)?.label ?? type
}

/** 监控对象描述：IP/盘符 / 服务名列表 */
function targetDesc(row: MonitorTarget): string {
  if (row.metricType === 'HOST') return row.isLocal ? '本机' : (row.hostAddress ?? '本机')
  if (row.metricType === 'DISK') return row.driveLetter ? `${row.driveLetter} 盘` : '所有固定磁盘'
  if (row.metricType === 'SVC') return row.serviceNames ?? ''
  return ''
}

/** 探测值展示：百分比指标带 %，服务指标为运行中服务数 */
function fmtValue(metricType: string, value: number | null): string {
  if (value == null) return '—'
  return metricType === 'SVC' ? `${value} 个运行中` : `${value}%`
}

// ── 监控目标列表（右侧抽屉弹出） ──
const listVisible = ref(false)
const targets = ref<MonitorTarget[]>([])
const loading = ref(false)

async function load() {
  loading.value = true
  try {
    targets.value = await httpGet<MonitorTarget[]>('/api/Common/HostMonitor/List')
  } catch {
    targets.value = []
  } finally {
    loading.value = false
  }
}
load()

// ── 机器 Dashboard（Grafana 风格多维度面板） ──
const hostTargets = computed(() => targets.value.filter(t => t.metricType === 'HOST'))

const dashTargetId = ref<number | null>(null)
const dashHours = ref(6)
const dashLoading = ref(false)
const dash = ref<HostMetricsData | null>(null)
const dashTarget = computed(() => hostTargets.value.find(t => t.id === dashTargetId.value) ?? null)

// 目标列表加载后默认选中本机（后端识别 isLocal 的 HOST 目标），无本机目标时退回第一台机器
watch(hostTargets, (list) => {
  if (list.length > 0 && (dashTargetId.value == null || !list.some(t => t.id === dashTargetId.value))) {
    dashTargetId.value = (list.find(t => t.isLocal) ?? list[0]).id
  }
}, { immediate: true })
watch([dashTargetId, dashHours], () => loadDashboard())

/** 是否已有请求在途：轮询时若上次请求未完成则跳过，避免慢请求叠加 */
let dashFetching = false
async function loadDashboard(silent = false) {
  if (dashTargetId.value == null) {
    dash.value = null
    return
  }
  if (dashFetching) return
  dashFetching = true
  // 静默刷新不弹 loading 遮罩：只更新数据，避免图表区域闪烁看起来像整页刷新；
  // silent 请求同时不弹错误提示、不跳错误页，后端重启期间轮询失败静默跳过
  if (!silent) dashLoading.value = true
  try {
    dash.value = await httpGet<HostMetricsData>('/api/Common/HostMonitor/Metrics', {
      targetId: dashTargetId.value, hours: dashHours.value,
    }, undefined, { silent })
  } catch {
    if (!silent) dash.value = null
  } finally {
    dashFetching = false
    if (!silent) dashLoading.value = false
  }
}

// 实时刷新：每 30 秒静默拉取最新监控数据（后端最短 1 分钟采集一次），无需手动刷新页面；
// 页面切到后台时跳过请求，回到前台后由下一轮轮询自然补上
let dashTimer: ReturnType<typeof setInterval> | null = null
onMounted(() => {
  dashTimer = setInterval(() => {
    if (document.hidden) return
    loadDashboard(true)
  }, 30_000)
})
onBeforeUnmount(() => {
  if (dashTimer) {
    clearInterval(dashTimer)
    dashTimer = null
  }
})

/** 使用率进度条颜色：≥90 红 / ≥75 橙 / 其余绿 */
function percentColor(v: number): string {
  if (v >= 90) return '#f56c6c'
  if (v >= 75) return '#e6a23c'
  return '#67c23a'
}

/** 开机时长展示：<48 小时按小时，否则按天 */
function uptimeText(h: number): string {
  return h < 48 ? `${h.toFixed(1)} 小时` : `${(h / 24).toFixed(1)} 天`
}

// ── 设备规格与网络信息（ipconfig /all）──
interface HostSystemInfo {
  deviceName: string; model: string; processor: string; ram: string; gpu: string
  storage: string; deviceId: string; productId: string; systemType: string; penTouch: string
  networkText: string
}
const sysInfo = ref<HostSystemInfo | null>(null)
// 设备规格/网络信息基本不变：首次请求后按目标缓存，后续打开弹窗直接读缓存不再请求；
// 如需强制重采，点弹窗内“重新加载”图标（force=true 绕过缓存并回写）
const sysInfoCache = new Map<number, HostSystemInfo>()

/** 拉取设备规格与网络信息：优先读缓存，force 时跳过缓存重新请求
 *  （不另加局部 v-loading，httpGet 的全局遮罩已会就近覆盖弹窗，避免双重遮罩） */
async function loadSystemInfo(force = false) {
  const id = dashTargetId.value
  if (id == null) {
    sysInfo.value = null
    return
  }
  if (!force) {
    const cached = sysInfoCache.get(id)
    if (cached) {
      sysInfo.value = cached
      return
    }
  }
  try {
    const info = await httpGet<HostSystemInfo>('/api/Common/HostMonitor/SystemInfo', { id }, 60_000)
    sysInfoCache.set(id, info)
    sysInfo.value = info
  } catch {
    sysInfo.value = null
  }
}
const sysInfoVisible = ref(false)
const winrmHelpVisible = ref(false)
/** 点击操作系统面板：弹窗展示设备规格与网络信息 */
function openSysInfo() {
  sysInfoVisible.value = true
  loadSystemInfo()
}

/** 复制网络信息（ipconfig /all）到剪贴板 */
function copyNetworkInfo() {
  const text = sysInfo.value?.networkText
  if (!text) return
  navigator.clipboard.writeText(text).then(() => {
    ElMessage.success('网络信息已复制到剪贴板')
  }).catch(() => {
    ElMessage.error('复制失败，请手动选择文本复制')
  })
}

/** 复制设备规格（全部条目逐行“键：值”）到剪贴板 */
function copySpecInfo() {
  const s = sysInfo.value
  if (!s) return
  const text = [
    `设备名：${s.deviceName || '—'}`,
    `机型：${s.model || '—'}`,
    `处理器：${s.processor || '—'}`,
    `机带 RAM：${s.ram || '—'}`,
    `显卡：${s.gpu || '—'}`,
    `存储：${s.storage || '—'}`,
    `设备 ID：${s.deviceId || '—'}`,
    `产品 ID：${s.productId || '—'}`,
    `系统类型：${s.systemType || '—'}`,
    `笔和触控：${s.penTouch || '—'}`,
  ].join('\n')
  navigator.clipboard.writeText(text).then(() => {
    ElMessage.success('设备规格已复制到剪贴板')
  }).catch(() => {
    ElMessage.error('复制失败，请手动选择文本复制')
  })
}

// ===== 时间序列图表配置 =====
const dashTimes = computed(() => (dash.value?.history ?? []).map(p => p.checkAt.slice(5, 16).replace('T', ' ')))

/** 通用折线图配置（多条系列、统一 tooltip/网格） */
function lineOption(series: { name: string; color: string; data: (number | null)[] }[], max100 = false): EChartsCoreOption {
  return {
    tooltip: { trigger: 'axis' },
    legend: { data: series.map(s => s.name), top: 0, textStyle: { fontSize: 11 } },
    grid: { left: 44, right: 12, top: 26, bottom: 24 },
    xAxis: { type: 'category', data: dashTimes.value, boundaryGap: false },
    yAxis: { type: 'value', max: max100 ? 100 : undefined, minInterval: max100 ? undefined : 0.1 },
    series: series.map(s => ({
      name: s.name, type: 'line', smooth: true, showSymbol: false,
      areaStyle: { opacity: 0.08 }, itemStyle: { color: s.color }, data: s.data,
    })),
  }
}

const cpuOption = computed<EChartsCoreOption>(() => lineOption([
  { name: 'CPU 使用率', color: '#f56c6c', data: (dash.value?.history ?? []).map(p => p.cpuPercent) },
], true))
const memOption = computed<EChartsCoreOption>(() => ({
  tooltip: { trigger: 'axis' },
  legend: { data: ['内存使用率', '已用 GB'], top: 0, textStyle: { fontSize: 11 } },
  grid: { left: 44, right: 44, top: 26, bottom: 24 },
  xAxis: { type: 'category', data: dashTimes.value, boundaryGap: false },
  yAxis: [
    { type: 'value', max: 100 },
    { type: 'value', splitLine: { show: false } },
  ],
  series: [
    {
      name: '内存使用率', type: 'line', smooth: true, showSymbol: false,
      areaStyle: { opacity: 0.08 }, itemStyle: { color: '#67c23a' },
      data: (dash.value?.history ?? []).map(p => p.memoryPercent),
    },
    {
      name: '已用 GB', type: 'line', smooth: true, showSymbol: false, yAxisIndex: 1,
      itemStyle: { color: '#409eff' },
      data: (dash.value?.history ?? []).map(p => p.memoryUsedGb),
    },
  ],
}))
const ioOption = computed<EChartsCoreOption>(() => lineOption([
  { name: '磁盘读', color: '#e6a23c', data: (dash.value?.history ?? []).map(p => p.diskReadMbPerSec) },
  { name: '磁盘写', color: '#7c3aed', data: (dash.value?.history ?? []).map(p => p.diskWriteMbPerSec) },
]))
const netOption = computed<EChartsCoreOption>(() => lineOption([
  { name: '接收', color: '#22c55e', data: (dash.value?.history ?? []).map(p => p.netInKbps) },
  { name: '发送', color: '#f59e0b', data: (dash.value?.history ?? []).map(p => p.netOutKbps) },
]))

const columns = computed<DataTableColumn<MonitorTarget>[]>(() => [
  { type: 'index', label: '#', width: 50, align: 'center' },
  { prop: 'status', label: '状态', width: 90, align: 'center', custom: true },
  { prop: 'name', label: '目标名称', width: 200 },
  { prop: 'metricType', label: '监控指标', width: 300, align: 'center', formatter: (row) => metricLabel(row.metricType) },
  {
    prop: 'target', label: '监控对象', minWidth: 150, showOverflowTooltip: true,
    formatter: (row) => targetDesc(row) || '—',
  },
  {
    prop: 'thresholdPercent', label: '阈值', width: 80, align: 'center',
    formatter: (row) => row.thresholdPercent != null ? `${row.thresholdPercent}%` : '—',
  },
  {
    prop: 'lastValue', label: '当前值', width: 110, align: 'center',
    formatter: (row) => fmtValue(row.metricType, row.lastValue),
  },
  { prop: 'intervalMinutes', label: '间隔(分)', width: 90, align: 'center' },
  {
    prop: 'lastCheckAt', label: '最近探测', width: 170, className: 'cell-nowrap',
    formatter: (row) => row.lastCheckAt ? formatDate(row.lastCheckAt) : '未探测',
  },
  { prop: 'enabled', label: '启用', width: 70, align: 'center', custom: true },
])

/** 状态标签：未探测 / 正常 / 异常（异常悬浮显示原因） */
function statusTag(row: MonitorTarget) {
  if (row.lastStatus == null) return { text: '未探测', type: 'info' as const }
  if (row.lastStatus === 1) return { text: '正常', type: 'success' as const }
  return { text: '异常', type: 'danger' as const }
}

// ── 新增 / 编辑 ──
const editVisible = ref(false)
const saving = ref(false)
const editFormRef = ref<FormInstance>()
interface EditForm {
  id: number | null
  name: string
  metricType: string
  hostAddress: string
  authAccount: string
  authPassword: string
  driveLetter: string
  serviceNames: string
  thresholdPercent: number | null
  timeoutSeconds: number
  intervalMinutes: number
  enabled: boolean
  notifyEmail: boolean
  remark: string
}
const editForm = ref<EditForm>(emptyForm())

function emptyForm(): EditForm {
  return {
    id: null, name: '', metricType: 'HOST', hostAddress: '', authAccount: 'Administrator', authPassword: '',
    driveLetter: '', serviceNames: '',
    thresholdPercent: 90, timeoutSeconds: 30, intervalMinutes: 10,
    enabled: true, notifyEmail: true, remark: '',
  }
}

const editRules = computed<FormRules>(() => ({
  name: [{ required: true, message: '请输入目标名称', trigger: 'blur' }],
  serviceNames: editForm.value.metricType === 'SVC'
    ? [{ required: true, message: '请输入服务名列表', trigger: 'blur' }]
    : [],
  authAccount: editForm.value.metricType === 'HOST' && editForm.value.hostAddress.trim()
    ? [{ required: true, message: '远程目标必须填写采集账号', trigger: 'blur' }]
    : [],
  authPassword: editForm.value.metricType === 'HOST' && editForm.value.hostAddress.trim() && !editForm.value.id
    ? [{ required: true, message: '远程目标必须填写采集密码', trigger: 'blur' }]
    : [],
}))

function openAdd() {
  editForm.value = emptyForm()
  editVisible.value = true
}

function openEdit(row: MonitorTarget) {
  editForm.value = {
    id: row.id, name: row.name, metricType: row.metricType,
    hostAddress: row.hostAddress ?? '', authAccount: row.authAccount ?? '', authPassword: '',
    driveLetter: row.driveLetter ?? '', serviceNames: row.serviceNames ?? '',
    thresholdPercent: row.thresholdPercent, timeoutSeconds: row.timeoutSeconds,
    intervalMinutes: row.intervalMinutes,
    enabled: row.enabled, notifyEmail: row.notifyEmail, remark: row.remark ?? '',
  }
  editVisible.value = true
}

async function save() {
  if (!editFormRef.value) return
  try {
    await editFormRef.value.validate()
  } catch (e: unknown) {
    // Element Plus 表单校验失败时抛出的是包含字段错误的对象，非 Error 实例
    // 直接返回，让表单高亮错误字段，不弹出错误提示
    return
  }
  saving.value = true
  try {
    await httpPost<number>('/api/Common/HostMonitor/Save', {
      id: editForm.value.id,
      name: editForm.value.name,
      metricType: editForm.value.metricType,
      hostAddress: editForm.value.hostAddress || null,
      authAccount: editForm.value.authAccount || null,
      authPassword: editForm.value.authPassword || null,
      driveLetter: editForm.value.driveLetter || null,
      serviceNames: editForm.value.serviceNames || null,
      thresholdPercent: editForm.value.thresholdPercent ?? null,
      timeoutSeconds: editForm.value.timeoutSeconds,
      intervalMinutes: editForm.value.intervalMinutes,
      enabled: editForm.value.enabled,
      notifyEmail: editForm.value.notifyEmail,
      remark: editForm.value.remark || null,
    })
    ElMessage.success(editForm.value.id ? '已保存' : '已添加')
    editVisible.value = false
    load()
  } finally {
    saving.value = false
  }
}

// ── 删除 ──
async function remove(row: MonitorTarget) {
  const ok = await confirmAndRun(
    `确定删除监控目标「${row.name}」及其探测日志吗？`,
    () => httpDelete(`/api/Common/HostMonitor/Delete?id=${row.id}`),
    { title: '确认删除', confirmButtonText: '删除' }
  )
  if (ok) load()
}

// ── 立即检测 ──
const checkingId = ref<number | null>(null)
async function checkNow(row: MonitorTarget) {
  checkingId.value = row.id
  try {
    const log = await httpPost<MonitorLog>(`/api/Common/HostMonitor/Check?id=${row.id}`, null)
    if (log.status === 1) {
      ElMessage.success(`「${row.name}」探测正常（${fmtValue(row.metricType, log.value)}）`)
    } else {
      ElMessage.warning(`「${row.name}」探测异常：${log.errorMsg ?? '未知错误'}`)
    }
    load()
    if (row.metricType === 'HOST') loadDashboard()
  } finally {
    checkingId.value = null
  }
}

// ── 磁盘清理（仅整机概览目标：选盘符后先扫描候选文件，再勾选后清理） ──
interface HostDiskClean {
  freedMb: number
  freeGbAfter: number
  deletedFiles: number
  files: string[]
  filesTruncated: boolean
  items: { path: string; ok: boolean; reason: string }[]   // 勾选逐项清理结果（选择性清理/回收站勾选时返回）
}
interface HostDiskFile { category: string; name: string; path: string; originalPath?: string; sizeKb: number; lastWriteTime: string }
interface HostDiskScan { files: HostDiskFile[]; recycleFiles: HostDiskFile[]; recycleCount: number; recycleSizeKb: number; truncated: boolean }
// 清理选项随盘符过滤：系统缓存类清理项只存在于系统盘（C），选 C 盘展示全部；
// 非系统盘只展示该盘可用的“磁盘垃圾”（垃圾扩展名文件）；回收站不作为选项，扫描默认列出全部盘数据
const CLEAN_OPTIONS = computed(() => cleanDrive.value === 'C' ? [
  { value: 'userTemp', label: '用户临时目录（%TEMP%）' },
  { value: 'winTemp', label: 'Windows 临时目录（Windows\\Temp）' },
  { value: 'prefetch', label: 'Prefetch 预读缓存' },
  { value: 'updateCache', label: 'Windows Update 下载缓存' },
  { value: 'browserCache', label: '浏览器缓存（Chrome/Edge/Firefox）' },
  { value: 'thumbnailCache', label: '缩略图缓存（Explorer）' },
  { value: 'logFiles', label: '日志文件（*.log）' },
  { value: 'oldDownloads', label: '旧下载文件（超过 30 天未访问）' },
] : [
  { value: 'driveJunk', label: `${cleanDrive.value} 盘磁盘垃圾（*.tmp / *.temp / *.log / *.bak / *.chk / *.old）` },
])
const CLEAN_CATEGORY_LABEL: Record<string, string> = {
  USER_TEMP: '用户临时', WIN_TEMP: 'Windows 临时', PREFETCH: 'Prefetch', UPDATE_CACHE: '更新缓存',
  BROWSER_CACHE: '浏览器缓存', THUMBNAIL_CACHE: '缩略图', LOG_FILE: '日志', OLD_DOWNLOAD: '旧下载',
  DRV_JUNK: '磁盘垃圾',
}
/** 清理弹窗配色：不同盘符与不同清理类别用不同颜色区分 */
const CLEAN_PALETTE = ['#409eff', '#67c23a', '#e6a23c', '#f56c6c', '#9b59b6', '#16a085', '#f39c12', '#3498db', '#e84393']
const CATEGORY_COLOR: Record<string, string> = {
  userTemp: '#409eff', winTemp: '#67c23a', prefetch: '#e6a23c', updateCache: '#f56c6c',
  browserCache: '#9b59b6', thumbnailCache: '#16a085', logFiles: '#f39c12', oldDownloads: '#3498db',
  recycleBin: '#e84393', driveJunk: '#7f8c8d',
}
/** 文件列表 rowData.category 为大写枚举（同 CLEAN_CATEGORY_LABEL），映射到 CATEGORY_COLOR 的 camelCase 键 */
const CATEGORY_COLOR_BY_FILE_CAT: Record<string, string> = {
  USER_TEMP: CATEGORY_COLOR.userTemp, WIN_TEMP: CATEGORY_COLOR.winTemp,
  PREFETCH: CATEGORY_COLOR.prefetch, UPDATE_CACHE: CATEGORY_COLOR.updateCache,
  BROWSER_CACHE: CATEGORY_COLOR.browserCache, THUMBNAIL_CACHE: CATEGORY_COLOR.thumbnailCache,
  LOG_FILE: CATEGORY_COLOR.logFiles, OLD_DOWNLOAD: CATEGORY_COLOR.oldDownloads,
  DRV_JUNK: CATEGORY_COLOR.driveJunk,
}
function driveColor(letter: string): string {
  const idx = cleanDriveOptions.value.indexOf(letter)
  return CLEAN_PALETTE[(idx < 0 ? 0 : idx) % CLEAN_PALETTE.length]
}
/** 分类卡片点击切换勾选（替代 el-checkbox-group，卡片化配色） */
function toggleCategory(v: string) {
  const i = cleanCategories.value.indexOf(v)
  if (i >= 0) cleanCategories.value.splice(i, 1)
  else cleanCategories.value.push(v)
}
const cleanVisible = ref(false)
const cleanStep = ref<'options' | 'files'>('options')
const cleaning = ref(false)
const scanning = ref(false)
const cleanCategories = ref<string[]>([])
// 所选清理盘符：默认 C，选项来自 Dashboard 分区表；系统缓存类清理项固定位于系统盘，
// 选非系统盘时后端自动改扫/清理该盘垃圾文件（DRV_JUNK），盘符同时决定回收站与可用空间统计
const cleanDrive = ref('C')
// 切换盘符：可用清理项变化，重置为该盘符下全部选项默认全选
watch(cleanDrive, () => {
  cleanCategories.value = CLEAN_OPTIONS.value.map(o => o.value)
})
const cleanDriveOptions = computed(() => {
  const letters = (dash.value?.latest?.disks ?? [])
    .map(d => d.drive.replace(/:$/, '').toUpperCase())
    .filter(l => /^[A-Z]$/.test(l))
  return letters.length > 0 ? letters : ['C']
})
const scanData = ref<HostDiskScan | null>(null)
/** 勾选状态用 path 集合全量管理（配虚拟滚动表格）：跨滚动不丢，筛选变化时同步为筛选结果，全选作用于筛选结果 */
const scanSelectedKeys = ref<Set<string>>(new Set())
const recycleSelectedKeys = ref<Set<string>>(new Set())
const selectedFiles = computed(() => (scanData.value?.files ?? []).filter(f => scanSelectedKeys.value.has(f.path)))
const selectedRecycle = computed(() => (scanData.value?.recycleFiles ?? []).filter(f => recycleSelectedKeys.value.has(f.path)))
const cleanResult = ref<HostDiskClean | null>(null)
const cleanResultVisible = ref(false)

/** 扫描表格高度自适应：弹窗几乎占满视口，扣除头部/汇总/底部按钮后剩余空间全给表格，避免弹窗内滚动条 */
const scanTableHeight = ref(360)
const recycleTableHeight = ref(240)
/** 清单折叠状态：文件列表与回收站均可折叠，两表共存时高度平分，折叠后空间让给另一侧 */
const recycleCollapsed = ref(true)
function toggleRecycleCollapse() {
  recycleCollapsed.value = !recycleCollapsed.value
  calcCleanTableHeights()   // 折叠/展开后重新分配表格高度
}
const fileCollapsed = ref(false)
function toggleFileCollapse() {
  fileCollapsed.value = !fileCollapsed.value
  calcCleanTableHeights()
}
function calcCleanTableHeights() {
  // 弹窗顶部留白(6vh，与 .clean-scan-dialog 的 --el-dialog-margin-top 对齐)
  // + 头部(~54) + 主体内边距(~40) + 底部按钮区(~64) + 弹窗底部边距(50) + 搜索工具栏(~46)
  // 额外预留 16px 余量：内容高度若刚好卡在 max-height 临界值，DPI 缩放下亚像素差异会让
  // 竖向滚动条反复出现/消失引发弹窗持续抖动，留出余量确保内容可靠装下
  const total = Math.max(300, window.innerHeight - Math.round(window.innerHeight * 0.06) - 272)
  const TITLE = 36   // 每个列表标题行高度（折叠后标题行仍占位）
  const showFile = hasFileCategories.value
  const showRecycle = true   // 回收站列表始终展示（扫描默认带出全部盘回收站数据），默认折叠
  const fileExpanded = showFile && !fileCollapsed.value
  const recycleExpanded = showRecycle && !recycleCollapsed.value
  // 剩余高度扣除标题行后由两个列表平分；某一侧折叠时另一侧独占全高，避免弹窗出现滚动条
  const free = Math.max(200, total - (showFile ? TITLE : 0) - (showRecycle ? TITLE : 0))
  if (fileExpanded && recycleExpanded) {
    scanTableHeight.value = Math.round(free / 2)
    recycleTableHeight.value = free - scanTableHeight.value
  } else if (fileExpanded) {
    scanTableHeight.value = free
    recycleTableHeight.value = 140
  } else if (recycleExpanded) {
    scanTableHeight.value = 140
    recycleTableHeight.value = free
  } else {
    scanTableHeight.value = 140
    recycleTableHeight.value = 140
  }
}
// 弹窗打开期间窗口尺寸变化时同步重算，保持无滚动条
function onCleanResize() {
  if (cleanVisible.value && scanData.value) calcCleanTableHeights()
}
onMounted(() => window.addEventListener('resize', onCleanResize))
onBeforeUnmount(() => window.removeEventListener('resize', onCleanResize))

// 第二步表格区宽度测量：替代 el-auto-resizer（其内部 RO 与 el-table-v2 渲染形成尺寸反馈循环，
// 导致弹窗持续抖动并产生 ResizeObserver loop 警告）。此处用自管 RO + rAF 节流 + 整数取整 +
// 数值不变不更新，从机制上掐断循环；高度由 calcCleanTableHeights 已知，无需测量
const cleanWidthEl = ref<HTMLDivElement | null>(null)
const cleanTableWidth = ref(0)
let cleanWidthRaf = 0
let cleanWidthRo: ResizeObserver | null = null
function setupCleanWidthRo() {
  cleanWidthRo?.disconnect()
  cleanWidthRo = null
  if (!cleanWidthEl.value) return
  cleanWidthRo = new ResizeObserver((entries) => {
    const w = Math.floor(entries[0]?.contentRect.width ?? 0)
    if (w <= 0 || w === cleanTableWidth.value) return   // 宽度未变则不触发重渲染，阻断反馈循环
    cancelAnimationFrame(cleanWidthRaf)
    cleanWidthRaf = requestAnimationFrame(() => { cleanTableWidth.value = w })
  })
  cleanWidthRo.observe(cleanWidthEl.value)
}
// 进入第二步（文件清单/仅回收站两个分支的容器元素不同）后挂载观察
watch(cleanStep, (v) => {
  if (v === 'files') nextTick(setupCleanWidthRo)
})
onBeforeUnmount(() => {
  cleanWidthRo?.disconnect()
  cancelAnimationFrame(cleanWidthRaf)
})

const scanTotalKb = computed(() => (scanData.value?.files ?? []).reduce((s, f) => s + f.sizeKb, 0))
/** 文件列表搜索关键字：按路径与分类名称过滤（表格配 row-key 保留已勾选状态） */
const scanKeyword = ref('')
/** 分类筛选：空为全部；选项由扫描结果中实际存在的分类聚合而来（带条数） */
const scanCatFilter = ref('')
const scanCatOptions = computed(() => {
  const files = scanData.value?.files ?? []
  const counts = new Map<string, number>()
  files.forEach(f => counts.set(f.category, (counts.get(f.category) ?? 0) + 1))
  return [...counts.entries()].map(([value, count]) => ({
    value,
    count,
    label: CLEAN_CATEGORY_LABEL[value] ?? value,
  }))
})
/** 文件列表排序状态：表头点击循环 降序 → 升序 → 取消（大小/时间默认先降序更实用）；
 *  order 必须用 EP 的 TableV2SortOrder 枚举（字符串字面量不可赋值给 enum） */
const scanSortBy = ref<{ key: string; order: TableV2SortOrder } | null>(null)
function onScanSort({ key }: { key: string | number | symbol }) {
  const k = String(key)
  const cur = scanSortBy.value
  if (cur?.key !== k) scanSortBy.value = { key: k, order: TableV2SortOrder.DESC }
  else if (cur.order === TableV2SortOrder.DESC) scanSortBy.value = { key: k, order: TableV2SortOrder.ASC }
  else scanSortBy.value = null   // 第三次点击取消排序，恢复扫描原顺序
}
const filteredScanFiles = computed(() => {
  let files = scanData.value?.files ?? []
  if (scanCatFilter.value) files = files.filter(f => f.category === scanCatFilter.value)
  const kw = scanKeyword.value.trim().toLowerCase()
  if (kw) {
    files = files.filter(f =>
      f.path.toLowerCase().includes(kw)
      || f.name.toLowerCase().includes(kw)
      || (CLEAN_CATEGORY_LABEL[f.category] ?? f.category).toLowerCase().includes(kw))
  }
  const sb = scanSortBy.value
  if (sb) {
    const dir = sb.order === TableV2SortOrder.ASC ? 1 : -1
    files = [...files].sort((a, b) => (sb.key === 'size'
      ? a.sizeKb - b.sizeKb
      : String(a.lastWriteTime).localeCompare(String(b.lastWriteTime))) * dir)
  }
  return files
})
/** 虚拟滚动表格（el-table-v2）：3000+ 行只渲染可视区域，无分页，勾选状态以 Set 全量维护 */
function toggleScanSel(path: string) {
  const s = new Set(scanSelectedKeys.value)
  if (s.has(path)) s.delete(path); else s.add(path)
  scanSelectedKeys.value = s
}
/** 搜索/分类筛选变化后，初始勾选同步为筛选结果：
 * 否则扫描后默认全量全选会让"搜索结果未全选"，点全选会把未匹配的整库数据也勾上 */
watch([scanKeyword, scanCatFilter], ([kw, cat]) => {
  if (!scanData.value) return   // 扫描未完成时无数据
  scanSelectedKeys.value = new Set(filteredScanFiles.value.map(f => f.path))
})
/** 全选/全不选：作用于当前筛选（搜索+分类）后的全部行 */
function toggleScanAll() {
  const paths = filteredScanFiles.value.map(f => f.path)
  const allSelected = paths.length > 0 && paths.every(p => scanSelectedKeys.value.has(p))
  const s = new Set(scanSelectedKeys.value)
  paths.forEach(p => { if (allSelected) s.delete(p); else s.add(p) })
  scanSelectedKeys.value = s
}
function toggleRecycleSel(path: string) {
  const s = new Set(recycleSelectedKeys.value)
  if (s.has(path)) s.delete(path); else s.add(path)
  recycleSelectedKeys.value = s
}
function toggleRecycleAll() {
  const paths = (scanData.value?.recycleFiles ?? []).map(f => f.path)
  const allSelected = paths.length > 0 && paths.every(p => recycleSelectedKeys.value.has(p))
  const s = new Set(recycleSelectedKeys.value)
  paths.forEach(p => { if (allSelected) s.delete(p); else s.add(p) })
  recycleSelectedKeys.value = s
}

/** 列宽随内容自适应：canvas 测量列内最长文本像素宽，保证单行完整展示；
 *  限制在 [min,max] 内，总列宽超表格宽时由 el-table-v2 提供横向滚动 */
let measureCtx: CanvasRenderingContext2D | null = null
function fitColumnWidth<T>(rows: T[], text: (row: T) => string, title: string, min: number, max: number): number {
  if (!measureCtx) measureCtx = document.createElement('canvas').getContext('2d')
  let w: number
  if (measureCtx) {
    // 与单元格字体一致（.clean-cat / 行内单元格均为 12px）
    measureCtx.font = '12px "Microsoft YaHei", sans-serif'
    w = measureCtx.measureText(title).width
    for (const row of rows) {
      if (w >= max) break   // 已达上限无需继续测量
      const tw = measureCtx.measureText(text(row)).width
      if (tw > w) w = tw
    }
  } else {
    w = Math.max(title.length, 20) * 12
  }
  return Math.max(min, Math.min(max, Math.ceil(w) + 30))   // 30 = 单元格左右内边距 + 余量
}

/** 按扩展名匹配文件类型图标：图标 + 颜色类名，未匹配时返回 null 用默认 Document 图标 */
const FILE_ICON_MAP: { exts: string[]; icon: Component; cls: string }[] = [
  { exts: ['.png', '.jpg', '.jpeg', '.gif', '.bmp', '.ico', '.webp', '.svg'], icon: Picture, cls: 'clean-ficon-green' },
  { exts: ['.mp4', '.avi', '.mkv', '.mov', '.wmv', '.flv', '.webm'], icon: VideoCamera, cls: 'clean-ficon-purple' },
  { exts: ['.mp3', '.wav', '.wma', '.flac', '.aac', '.ogg', '.m4a'], icon: Headset, cls: 'clean-ficon-cyan' },
  { exts: ['.zip', '.rar', '.7z', '.gz', '.tar', '.cab', '.iso'], icon: Box, cls: 'clean-ficon-yellow' },
  { exts: ['.exe', '.dll', '.msi', '.sys', '.drv', '.ocx'], icon: Setting, cls: 'clean-ficon-blue' },
  { exts: ['.js', '.ts', '.vue', '.css', '.html', '.htm', '.json', '.xml', '.cs', '.java', '.py', '.sql', '.cmd', '.ps1', '.bat'], icon: Cpu, cls: 'clean-ficon-indigo' },
  { exts: ['.log', '.txt', '.ini', '.cfg', '.conf'], icon: Tickets, cls: 'clean-ficon-gray' },
  { exts: ['.tmp', '.temp', '.bak', '.chk', '.old', '.dmp', '.etl'], icon: Delete, cls: 'clean-ficon-red' },
]
/** 文件名单元格：真实系统图标（后端提取，与资源管理器一致）；未取到/提取失败回退通用类型图标 */
function renderFileNameCell(name: string, icons: Record<string, string>) {
  const ext = name.includes('.') ? name.slice(name.lastIndexOf('.')).toLowerCase() : ''
  const url = icons[ext]
  const iconNode = url
    ? h('img', { src: url, class: 'clean-ficon-img', draggable: false })
    : h(ElIcon, { class: ['clean-ficon', FILE_ICON_MAP.find(x => x.exts.includes(ext))?.cls ?? 'clean-ficon-default'] },
        () => h(FILE_ICON_MAP.find(x => x.exts.includes(ext))?.icon ?? Document))
  return h('span', { class: 'clean-fname', title: name }, [iconNode, h('span', { class: 'clean-fname-text' }, name)])
}

/** 真实系统图标映射：ext → data URL（整体替换触发列重渲染，图标异步到达后列表自动补上图标） */
const fileIconMap = shallowRef<Record<string, string>>({})
/** 已请求过/提取失败的扩展名集合，避免重复扫描反复请求 */
const iconRequestedExts = new Set<string>()

/** 扫描完成后批量拉取结果中出现扩展名的真实系统图标（静默请求，失败降级通用图标不影响主流程） */
async function loadFileIcons() {
  if (dashTargetId.value == null || !scanData.value) return
  const exts = new Set<string>()
  for (const f of [...scanData.value.files, ...(scanData.value.recycleFiles ?? [])]) {
    const i = f.name.lastIndexOf('.')
    if (i >= 0) exts.add(f.name.slice(i).toLowerCase())
  }
  const missing = [...exts].filter(e => !iconRequestedExts.has(e))
  if (missing.length === 0) return
  missing.forEach(e => iconRequestedExts.add(e))
  try {
    const res = await httpGet<Record<string, string>>('/api/Common/HostMonitor/FileIcons',
      { id: dashTargetId.value, exts: missing.join(',') }, undefined, { silent: true })
    if (res && Object.keys(res).length > 0) fileIconMap.value = { ...fileIconMap.value, ...res }
  } catch { /* 图标获取失败静默降级通用图标 */ }
}

/** 候选文件虚拟表列定义：复选框/行号/分类/名称（左侧固定）+ 路径 + 大小/修改时间（可排序）+ 操作（右侧固定，仅本机目标展示） */
const scanColumns = computed<any[]>(() => {
  const list = filteredScanFiles.value
  const all = scanData.value?.files ?? []   // 列宽按全量数据测量，筛选切换时列宽保持稳定
  const icons = fileIconMap.value   // 读取图标映射建立依赖：图标异步到达后列定义重算、表格补上图标
  const selCount = list.reduce((n, f) => n + (scanSelectedKeys.value.has(f.path) ? 1 : 0), 0)
  const allSelected = list.length > 0 && selCount === list.length
  return [
    {
      key: 'sel', width: 45, align: 'center', fixed: 'left',
      headerCellRenderer: () => h(ElCheckbox, {
        modelValue: allSelected,
        indeterminate: selCount > 0 && !allSelected,
        onChange: toggleScanAll,
      }),
      cellRenderer: ({ rowData }: { rowData: HostDiskFile }) => h(ElCheckbox, {
        modelValue: scanSelectedKeys.value.has(rowData.path),
        onChange: () => toggleScanSel(rowData.path),
      }),
    },
    { key: 'idx', title: '#', width: 50, align: 'center', fixed: 'left', cellRenderer: ({ rowIndex }: { rowIndex: number }) => h('span', String(rowIndex + 1)) },
    {
      // 分类列：最前面左侧固定；额外预留 16px 给前缀同色圆点
      key: 'cat', title: '分类', width: fitColumnWidth(all, f => CLEAN_CATEGORY_LABEL[f.category] ?? f.category, '分类', 100, 150) + 16, align: 'center', fixed: 'left',
      // 与第一步分类卡片同一套 CATEGORY_COLOR 颜色区分：同色圆点 + 彩色文字
      cellRenderer: ({ rowData }: { rowData: HostDiskFile }) => {
        const c = CATEGORY_COLOR_BY_FILE_CAT[rowData.category] ?? '#909399'
        return h('span', { class: 'clean-cat', style: { color: c } }, [
          h('span', {
            style: { display: 'inline-block', width: '8px', height: '8px', borderRadius: '50%', background: c, marginRight: '5px', verticalAlign: 'middle' },
          }),
          CLEAN_CATEGORY_LABEL[rowData.category] ?? rowData.category,
        ])
      },
    },
    {
      // 文件名称列左侧固定：分类在其前，横向滚动时分类/名称始终可见
      key: 'name', dataKey: 'name', title: '文件名称', width: fitColumnWidth(all, f => f.name, '文件名称', 160, 360) + 20,    // +20 预留图标宽度
      cellRenderer: ({ rowData }: { rowData: HostDiskFile }) => renderFileNameCell(rowData.name, icons),
    },
    {
      key: 'path', dataKey: 'path', title: '文件路径', width: fitColumnWidth(all, f => f.path, '文件路径', 260, 800), flexGrow: 2,
      cellRenderer: ({ rowData }: { rowData: HostDiskFile }) =>
        h('span', { class: 'clean-cell-ellipsis', title: rowData.path }, rowData.path),
    },
    { key: 'size', title: '大小', width: fitColumnWidth(all, f => fmtSize(f.sizeKb), '大小', 90, 160), align: 'center', sortable: true, cellRenderer: ({ rowData }: { rowData: HostDiskFile }) => h('span', fmtSize(rowData.sizeKb)) },
    { key: 'mtime', title: '最后修改', width: fitColumnWidth(all, f => formatDate(f.lastWriteTime), '最后修改', 140, 180), align: 'center', sortable: true, cellRenderer: ({ rowData }: { rowData: HostDiskFile }) => h('span', formatDate(rowData.lastWriteTime)) },
    // 打开文件夹仅本机目标有意义：远程主机整列不展示
    ...(dashTarget.value && !dashTarget.value.isLocal ? [] : [{
      key: 'op', title: '操作', width: 90, align: 'center', fixed: 'right',
      cellRenderer: ({ rowData }: { rowData: HostDiskFile }) =>
        h(ElButton, { link: true, type: 'primary', size: 'small', onClick: () => openFolder(rowData) }, () => '打开文件夹'),
    }]),
  ]
})

/** 回收站虚拟表列定义：复选框/行号/名称（左侧固定）+ 原位置 + 大小 + 删除时间（无操作列，回收站由标题行按钮统一打开） */
const recycleColumns = computed<any[]>(() => {
  const list = scanData.value?.recycleFiles ?? []
  const icons = fileIconMap.value   // 同 scanColumns：图标到达后重渲染
  const selCount = list.reduce((n, f) => n + (recycleSelectedKeys.value.has(f.path) ? 1 : 0), 0)
  const allSelected = list.length > 0 && selCount === list.length
  return [
    {
      key: 'sel', width: 45, align: 'center', fixed: 'left',
      headerCellRenderer: () => h(ElCheckbox, {
        modelValue: allSelected,
        indeterminate: selCount > 0 && !allSelected,
        onChange: toggleRecycleAll,
      }),
      cellRenderer: ({ rowData }: { rowData: HostDiskFile }) => h(ElCheckbox, {
        modelValue: recycleSelectedKeys.value.has(rowData.path),
        onChange: () => toggleRecycleSel(rowData.path),
      }),
    },
    { key: 'idx', title: '#', width: 50, align: 'center', fixed: 'left', cellRenderer: ({ rowIndex }: { rowIndex: number }) => h('span', String(rowIndex + 1)) },
    {
      key: 'name', dataKey: 'name', title: '名称', width: fitColumnWidth(list, f => f.name, '名称', 180, 400) + 20, flexGrow: 1, fixed: 'left',   // +20 预留图标宽度
      cellRenderer: ({ rowData }: { rowData: HostDiskFile }) => renderFileNameCell(rowData.name, icons),
    },
    {
      key: 'orig', title: '原位置', width: fitColumnWidth(list, f => f.originalPath || f.path, '原位置', 200, 800), flexGrow: 1,
      cellRenderer: ({ rowData }: { rowData: HostDiskFile }) => {
        const text = rowData.originalPath || rowData.path
        return h('span', { class: 'clean-cell-ellipsis', title: text }, text)
      },
    },
    { key: 'size', title: '大小', width: fitColumnWidth(list, f => fmtSize(f.sizeKb), '大小', 90, 160), align: 'center', cellRenderer: ({ rowData }: { rowData: HostDiskFile }) => h('span', fmtSize(rowData.sizeKb)) },
    { key: 'dtime', title: '删除时间', width: fitColumnWidth(list, f => fmtDeleted(f.lastWriteTime), '删除时间', 140, 180), align: 'center', cellRenderer: ({ rowData }: { rowData: HostDiskFile }) => h('span', fmtDeleted(rowData.lastWriteTime)) },
  ]
})
const selectedKb = computed(() => selectedFiles.value.reduce((s, f) => s + f.sizeKb, 0))
// 文件类清理项（区别于回收站）：只勾回收站时第二步不展示文件表，改展示回收站确认视图
const FILE_CATEGORY_VALUES = ['userTemp', 'winTemp', 'prefetch', 'updateCache', 'browserCache', 'thumbnailCache', 'logFiles', 'oldDownloads', 'driveJunk']
const hasFileCategories = computed(() => FILE_CATEGORY_VALUES.some(v => cleanCategories.value.includes(v)))
/** 清理按钮文案：随勾选/选中状态动态变化，避免固定显示"清理选中项"旧措辞 */
const cleanButtonText = computed(() => {
  const parts: string[] = []
  if (selectedFiles.value.length > 0) parts.push(`${selectedFiles.value.length} 个文件 / ${fmtSize(selectedKb.value)}`)
  if (selectedRecycle.value.length > 0) parts.push(`${selectedRecycle.value.length} 项回收站`)
  return parts.length > 0 ? `清理选中项（${parts.join('，')}）` : '清理选中项'
})

/** 扫描结果饼图：按类别展示文件占用比例 */
const scanPieOption = computed(() => {
  const files = scanData.value?.files ?? []
  const byCategory = new Map<string, { count: number; sizeKb: number }>()
  for (const f of files) {
    const label = CLEAN_CATEGORY_LABEL[f.category] ?? f.category
    const entry = byCategory.get(label) ?? { count: 0, sizeKb: 0 }
    entry.count++
    entry.sizeKb += f.sizeKb
    byCategory.set(label, entry)
  }
  const data = Array.from(byCategory.entries()).map(([name, v]) => ({
    name: `${name} (${v.count} 个)`,
    value: Math.round(v.sizeKb),
  }))
  return {
    tooltip: {
      trigger: 'item' as const,
      // 弹窗内图表：悬浮提示限制在图表区域内，避免被弹窗边界裁剪/遮挡
      confine: true,
      formatter: (p: { name: string; value: number; percent: number }) =>
        `${p.name}<br/>大小：${fmtSize(p.value)} KB<br/>占比：${p.percent}%`,
    },
    legend: {
      // 图例全部展示不分页：横向自然换行，饼图上移缩小给多行图例留出底部空间
      orient: 'horizontal' as const,
      left: 'center' as const,
      bottom: 0,
      itemWidth: 10,
      itemHeight: 10,
      itemGap: 8,
      textStyle: { fontSize: 11 },
    },
    series: [{
      name: '文件占用',
      type: 'pie' as const,
      radius: ['26%', '45%'],
      center: ['50%', '32%'],
      avoidLabelOverlap: true,
      itemStyle: { borderRadius: 6, borderColor: '#fff', borderWidth: 2 },
      label: { show: false },
      emphasis: {
        label: { show: true, fontSize: 14, fontWeight: 'bold' as const },
      },
      data,
    }],
  }
})

/** 文件大小展示：KB / MB / GB */
function fmtSize(kb: number): string {
  if (kb >= 1024 * 1024) return `${(kb / 1024 / 1024).toFixed(2)} GB`
  if (kb >= 1024) return `${(kb / 1024).toFixed(1)} MB`
  return `${Math.round(kb)} KB`
}

/** 回收站删除时间展示：后端取不到删除时间时返回 0001 年默认值，显示为 — */
function fmtDeleted(t: string): string {
  return t && !t.startsWith('0001') ? formatDate(t) : '—'
}

/** 回收站条目物理路径 → 原位置映射：结果清单中把 $Recycle.Bin 物理路径还原为可读的原位置 */
const recycleOriginalMap = computed(() => {
  const m = new Map<string, string>()
  for (const f of scanData.value?.recycleFiles ?? []) {
    if (f.path) m.set(f.path.toLowerCase(), f.originalPath || f.path)
  }
  return m
})

function recycleDisplayPath(p: string): string {
  return recycleOriginalMap.value.get(p.toLowerCase()) ?? p
}

function openCleanDisk() {
  if (dashTargetId.value == null) return
  cleanDrive.value = cleanDriveOptions.value.includes('C') ? 'C' : cleanDriveOptions.value[0]
  cleanCategories.value = CLEAN_OPTIONS.value.map(o => o.value)   // 默认全选
  cleanStep.value = 'options'
  scanData.value = null
  scanKeyword.value = ''
  recycleCollapsed.value = true   // 回收站列表默认折叠，用户点击标题行展开
  fileCollapsed.value = false
  scanSelectedKeys.value = new Set()
  recycleSelectedKeys.value = new Set()
  cleanVisible.value = true
}

/** 扫描任务进度（jobId 异步扫描：后端逐百条上报，前端轮询实时展示） */
interface ScanJob {
  jobId: string
  done: boolean
  scannedCount: number
  foundKb: number
  error?: string | null
  result?: HostDiskScan | null
}
const scanProgress = ref<{ count: number; kb: number } | null>(null)

/** 重新扫描：按当前盘符与分类重扫；先清空旧筛选/排序/勾选避免扫描期间残留 */
async function rescan() {
  if (scanning.value || cleaning.value) return
  scanKeyword.value = ''
  scanCatFilter.value = ''
  scanSortBy.value = null
  await scanFiles()
}

/** 第一步 → 第二步：启动后台扫描任务并轮询进度（仅读取不删除），完成后进入勾选视图 */
async function scanFiles() {
  if (dashTargetId.value == null || cleanCategories.value.length === 0) return
  scanning.value = true
  scanProgress.value = { count: 0, kb: 0 }
  try {
    // 启动扫描任务：静默请求不弹全屏遮罩（避免看起来像另开窗口），进度加载条直接显示在本弹窗内
    let start: ScanJob
    try {
      start = await httpGet<ScanJob>('/api/Common/HostMonitor/ScanDiskStart', {
        id: dashTargetId.value, categories: cleanCategories.value.join(','), drive: cleanDrive.value,
      }, undefined, { silent: true })
    } catch {
      ElMessage.error('扫描启动失败，请重试')
      return
    }
    // 轮询进度：静默请求不弹遮罩/错误提示，避免扫描期间页面反复闪烁；弹窗关闭则中止轮询
    while (cleanVisible.value) {
      await new Promise(r => setTimeout(r, 600))
      let p: ScanJob
      try {
        p = await httpGet<ScanJob>('/api/Common/HostMonitor/ScanProgress', { jobId: start.jobId }, undefined, { silent: true })
      } catch {
        ElMessage.error('扫描进度获取失败，请重试')
        return
      }
      scanProgress.value = { count: p.scannedCount, kb: p.foundKb }
      if (!p.done) continue
      if (p.error) {
        ElMessage.error(p.error)
        return
      }
      scanData.value = p.result ?? null
      break
    }
    if (!scanData.value) return   // 扫描中弹窗被关闭，丢弃结果
    scanKeyword.value = ''   // 新扫描结果不带旧搜索关键字
    scanCatFilter.value = ''   // 不带旧分类筛选
    scanSortBy.value = null   // 不带旧排序状态
    recycleCollapsed.value = true   // 回收站列表默认折叠，用户点击标题行展开
    fileCollapsed.value = false
    scanSelectedKeys.value = new Set((scanData.value?.files ?? []).map(f => f.path))   // 候选文件默认全量全选（回收站默认不勾选，由用户按需勾选）
    recycleSelectedKeys.value = new Set()
    loadFileIcons()   // 异步拉取真实系统图标，到达后列表自动补上（不阻塞进入第二步）
    calcCleanTableHeights()   // 先按当前视口算好表格高度，再切换到第二步避免首帧出现滚动条
    cleanStep.value = 'files'
  } finally {
    scanning.value = false
    scanProgress.value = null
  }
}

/** 在资源管理器中打开候选文件的所在文件夹（仅本机目标）：
 * 桌面壳内优先走 Shell API 桥接（不创建进程，不被 360 拦截），
 * 独立浏览器部署时兜底调后端接口 */
async function openFolderAt(filePath: string) {
  if (dashTargetId.value == null) return
  if (isHostAvailable()) {
    hostOpenLocation(filePath)
    return
  }
  await httpPost(`/api/Common/HostMonitor/OpenFolder?id=${dashTargetId.value}&path=${encodeURIComponent(filePath)}`, {})
}
async function openFolder(row: HostDiskFile) {
  await openFolderAt(row.path)
}
/** 打开系统回收站（仅本机目标）：桌面壳内优先走 Shell 桥接，独立浏览器部署时兜底调后端接口 */
async function openRecycleBin() {
  if (dashTargetId.value == null) return
  if (isHostAvailable()) {
    hostOpenRecycleBin()
    return
  }
  await httpPost(`/api/Common/HostMonitor/OpenRecycleBin?id=${dashTargetId.value}`, {})
}

/** 清理任务进度（jobId 异步清理：后端逐 20 条上报，前端轮询实时展示） */
interface CleanJob {
  jobId: string
  done: boolean
  totalCount: number
  deletedCount: number
  freedMb: number
  error?: string | null
  result?: HostDiskClean | null
}
const cleanProgress = ref<{ deleted: number; total: number; freedMb: number } | null>(null)
const cleanPercent = computed(() => {
  const p = cleanProgress.value
  if (!p || p.total <= 0) return 0
  return Math.min(100, Math.round((p.deleted / p.total) * 100))
})

/** 第二步：启动后台清理任务并轮询进度，完成后弹出结果（勾选回收站时同时清理勾选条目） */
async function doCleanDisk() {
  if (dashTargetId.value == null) return
  const has = (v: string) => cleanCategories.value.includes(v)
  cleaning.value = true
  cleanProgress.value = {
    deleted: 0,
    total: (hasFileCategories.value ? selectedFiles.value.length : 0) + selectedRecycle.value.length,
    freedMb: 0,
  }
  try {
    // 启动清理任务：静默请求不弹全屏遮罩（避免看起来像另开窗口），进度加载条直接显示在本弹窗内
    let start: CleanJob
    try {
      start = await httpPost<CleanJob>(`/api/Common/HostMonitor/CleanDiskStart?id=${dashTargetId.value}`, {
        drive: cleanDrive.value,
        // 全部分类标志均需随请求携带：后端按标志拼“允许删除目录根”做防越界校验，
        // 漏传的分类其文件会被判为“不在勾选分类目录下”而跳过
        userTemp: has('userTemp'),
        windowsTemp: has('winTemp'),
        prefetch: has('prefetch'),
        updateCache: has('updateCache'),
        browserCache: has('browserCache'),
        thumbnailCache: has('thumbnailCache'),
        logFiles: has('logFiles'),
        oldDownloads: has('oldDownloads'),
        driveJunk: has('driveJunk'),
        // 回收站无需勾选选项：勾选了回收站条目即携带路径与标志，仅删除勾选项
        recycleBin: selectedRecycle.value.length > 0,
        // 未勾选文件类清理项时不携带路径（后端会校验路径必须属于勾选分类）
        paths: hasFileCategories.value ? selectedFiles.value.map(f => f.path) : [],
        // 每个勾选文件携带自身扫描分类：后端按分类推导允许目录根，
        // 不再依赖分类布尔标志重传，避免合法文件被误判越界跳过
        pathCategories: Object.fromEntries(selectedFiles.value.map(f => [f.path, f.category])),
        recyclePaths: selectedRecycle.value.map(f => f.path),
      }, undefined, undefined, { silent: true })
    } catch {
      ElMessage.error('清理启动失败，请重试')
      return
    }
    // 轮询进度：静默请求不弹遮罩/错误提示；弹窗关闭则中止轮询（后台清理继续执行）
    while (cleanVisible.value) {
      await new Promise(r => setTimeout(r, 600))
      let p: CleanJob
      try {
        p = await httpGet<CleanJob>('/api/Common/HostMonitor/CleanProgress', { jobId: start.jobId }, undefined, { silent: true })
      } catch {
        ElMessage.error('清理进度获取失败，请重试')
        return
      }
      cleanProgress.value = {
        deleted: p.deletedCount,
        total: p.totalCount > 0 ? p.totalCount : (cleanProgress.value?.total ?? 0),
        freedMb: p.freedMb,
      }
      if (!p.done) continue
      if (p.error) {
        ElMessage.error(p.error)
        return
      }
      cleanVisible.value = false
      cleanResult.value = p.result ?? null
      cleanResultVisible.value = true
      loadDashboard()
      break
    }
  } finally {
    cleaning.value = false
    cleanProgress.value = null
  }
}

// ── 探测日志弹窗 ──
const logVisible = ref(false)
const logTarget = ref<MonitorTarget | null>(null)
const logs = ref<MonitorLog[]>([])
const logTotal = ref(0)
const logPage = ref(1)
const logSize = ref(20)
const logLoading = ref(false)

const logColumns = computed<DataTableColumn<MonitorLog>[]>(() => [
  { type: 'index', label: '#', width: 50, align: 'center' },
  { prop: 'status', label: '结果', width: 80, align: 'center', custom: true },
  {
    prop: 'value', label: '探测值', width: 120, align: 'center',
    formatter: (row) => fmtValue(logTarget.value?.metricType ?? '', row.value),
  },
  { prop: 'errorMsg', label: '异常原因', minWidth: 200, showOverflowTooltip: true, formatter: (row) => row.errorMsg ?? '—' },
  { prop: 'checkAt', label: '探测时间', width: 170, className: 'cell-nowrap', formatter: (row) => formatDate(row.checkAt) },
])

function openLogs(row: MonitorTarget) {
  logTarget.value = row
  logPage.value = 1
  logVisible.value = true
  loadLogs()
}

async function loadLogs() {
  if (!logTarget.value) return
  logLoading.value = true
  try {
    const res = await httpGet<{ total: number; list: MonitorLog[] }>('/api/Common/HostMonitor/Logs', {
      targetId: logTarget.value.id, page: logPage.value, size: logSize.value,
    })
    logs.value = res.list
    logTotal.value = res.total
  } catch {
    logs.value = []
    logTotal.value = 0
  } finally {
    logLoading.value = false
  }
}
</script>

<template>
  <div class="monitor-page">
    <!-- ===== 机器概览 Dashboard（标题栏吸顶） ===== -->
    <div class="host-dashboard">
      <div class="host-dashboard-bar">
        <span class="host-dashboard-title">机器概览 Dashboard</span>
        <template v-if="hostTargets.length > 0">
          <el-select v-model="dashTargetId" size="small" style="width: 220px">
            <el-option v-for="t in hostTargets" :key="t.id" :label="`${t.name}（${t.isLocal ? '本机' : t.hostAddress}）`" :value="t.id" />
          </el-select>
          <el-radio-group v-model="dashHours" size="small">
            <el-radio-button :value="1">近1小时</el-radio-button>
            <el-radio-button :value="6">近6小时</el-radio-button>
            <el-radio-button :value="24">近24小时</el-radio-button>
          </el-radio-group>
          <el-button size="small" :loading="dashLoading" @click="loadDashboard()">刷新</el-button>
          <el-button
            v-if="dashTarget && $has('host-monitor:check')"
            size="small"
            type="primary"
            :loading="checkingId === dashTarget.id"
            @click="checkNow(dashTarget)"
          >立即检测</el-button>
        </template>
        <el-button size="small" type="primary" plain @click="listVisible = true">监控列表</el-button>
        <span class="host-dashboard-hint host-dashboard-link" @click="winrmHelpVisible = true">远程电脑需开启 WinRM</span>
      </div>

      <template v-if="hostTargets.length > 0">
        <template v-if="dash">
        <!-- 统计卡 -->
        <div class="host-stat-row">
          <div class="host-stat">
            <div class="host-stat-value" :style="{ color: dashTarget && dashTarget.lastStatus === 2 ? '#f56c6c' : '#67c23a' }">
              {{ dashTarget ? statusTag(dashTarget).text : '—' }}
            </div>
            <div class="host-stat-label">探测状态</div>
          </div>
          <div class="host-stat">
            <div class="host-stat-value">{{ dash.latest?.uptimeHours != null ? uptimeText(dash.latest.uptimeHours) : '—' }}</div>
            <div class="host-stat-label">运行时间</div>
          </div>
          <div class="host-stat">
            <div class="host-stat-value">{{ dash.latest?.cpuCores ?? '—' }}</div>
            <div class="host-stat-label">CPU 核数</div>
          </div>
          <div class="host-stat">
            <div class="host-stat-value">{{ dash.latest?.memoryTotalGb != null ? `${dash.latest.memoryTotalGb} GB` : '—' }}</div>
            <div class="host-stat-label">总内存</div>
          </div>
          <div class="host-stat">
            <div class="host-stat-value">{{ dash.latest?.processCount ?? '—' }}</div>
            <div class="host-stat-label">进程数</div>
          </div>
          <div
            class="host-stat host-stat-wide host-stat-clickable"
            title="点击查看设备规格与网络信息"
            @click="openSysInfo"
          >
            <div class="host-stat-value host-stat-os" :title="dash.latest?.osName ?? ''">{{ dash.latest?.osName || '—' }}</div>
            <div class="host-stat-label">操作系统（点击查看设备规格 / 网络信息）</div>
          </div>
        </div>

        <!-- 磁盘分区表（头部右侧提供磁盘清理入口） -->
        <div class="host-disk-panel">
          <div class="host-disk-header">
            <div class="host-chart-title">各分区可用空间</div>
            <el-button v-if="$has('host-monitor:clean-disk')" size="small" :loading="cleaning" @click="openCleanDisk">清理磁盘</el-button>
          </div>
          <el-table :data="dash.latest?.disks ?? []" size="small" border>
            <el-table-column prop="drive" label="分区" width="90" />
            <el-table-column label="总空间" width="110">
              <template #default="{ row }">{{ (row as HostMetrics['disks'][number]).totalGb }} GB</template>
            </el-table-column>
            <el-table-column label="可用空间" width="110">
              <template #default="{ row }">{{ (row as HostMetrics['disks'][number]).freeGb }} GB</template>
            </el-table-column>
            <el-table-column label="使用率" min-width="240">
              <template #default="{ row }">
                <el-progress
                  :percentage="(row as HostMetrics['disks'][number]).usedPercent"
                  :color="percentColor((row as HostMetrics['disks'][number]).usedPercent)"
                  :stroke-width="12"
                />
              </template>
            </el-table-column>
          </el-table>
        </div>

        <!-- 时间序列图表 -->
        <div v-loading="dashLoading" class="host-charts">
          <div class="host-chart-panel">
            <div class="host-chart-title">CPU 使用率（%）</div>
            <BaseChart :option="cpuOption" height="180px" />
          </div>
          <div class="host-chart-panel">
            <div class="host-chart-title">内存信息</div>
            <BaseChart :option="memOption" height="180px" />
          </div>
          <div class="host-chart-panel">
            <div class="host-chart-title">磁盘读写速率（MB/s）</div>
            <BaseChart :option="ioOption" height="180px" />
          </div>
          <div class="host-chart-panel">
            <div class="host-chart-title">网络收发速率（KB/s）</div>
            <BaseChart :option="netOption" height="180px" />
          </div>
        </div>

        </template>
        <div v-else class="host-card-empty">暂无指标数据，在“监控列表”中点击“检测”立即采集，或等待定时探测积累历史</div>
      </template>
      <div v-else class="host-card-empty">暂无监控目标，点击右上角“监控列表”打开列表并新增</div>
    </div>

    <!-- 设备规格与网络信息弹窗（点击操作系统面板打开） -->
    <CommonDialog v-model="sysInfoVisible" :title="`设备规格与网络信息${dashTarget ? '（' + dashTarget.name + '）' : ''}`" width="960px" destroy-on-close>
      <div class="sysinfo-dialog-body">
        <div class="sysinfo-net-header sysinfo-spec-header">
          <div class="host-chart-title">设备规格</div>
          <div class="sysinfo-header-actions">
            <CommonTooltip content="重新加载" :copyable="false">
              <el-icon class="netinfo-copy" @click="loadSystemInfo(true)"><Refresh /></el-icon>
            </CommonTooltip>
            <CommonTooltip content="复制设备规格" :copyable="false">
              <el-icon
                class="netinfo-copy"
                :class="{ disabled: !sysInfo }"
                @click="copySpecInfo"
              ><CopyDocument /></el-icon>
            </CommonTooltip>
          </div>
        </div>
        <div class="sysinfo-grid">
          <div class="sysinfo-item"><span class="sysinfo-label">设备名</span><span class="sysinfo-value">{{ sysInfo?.deviceName || '—' }}</span></div>
          <div class="sysinfo-item"><span class="sysinfo-label">机型</span><span class="sysinfo-value">{{ sysInfo?.model || '—' }}</span></div>
          <div class="sysinfo-item"><span class="sysinfo-label">处理器</span><span class="sysinfo-value">{{ sysInfo?.processor || '—' }}</span></div>
          <div class="sysinfo-item"><span class="sysinfo-label">机带 RAM</span><span class="sysinfo-value">{{ sysInfo?.ram || '—' }}</span></div>
          <div class="sysinfo-item"><span class="sysinfo-label">显卡</span><span class="sysinfo-value">{{ sysInfo?.gpu || '—' }}</span></div>
          <div class="sysinfo-item"><span class="sysinfo-label">存储</span><span class="sysinfo-value">{{ sysInfo?.storage || '—' }}</span></div>
          <div class="sysinfo-item"><span class="sysinfo-label">设备 ID</span><span class="sysinfo-value">{{ sysInfo?.deviceId || '—' }}</span></div>
          <div class="sysinfo-item"><span class="sysinfo-label">产品 ID</span><span class="sysinfo-value">{{ sysInfo?.productId || '—' }}</span></div>
          <div class="sysinfo-item"><span class="sysinfo-label">系统类型</span><span class="sysinfo-value">{{ sysInfo?.systemType || '—' }}</span></div>
          <div class="sysinfo-item"><span class="sysinfo-label">笔和触控</span><span class="sysinfo-value">{{ sysInfo?.penTouch || '—' }}</span></div>
        </div>
        <div class="sysinfo-net-header">
          <div class="host-chart-title">网络信息（ipconfig /all）</div>
          <CommonTooltip content="复制网络信息" :copyable="false">
            <el-icon
              class="netinfo-copy"
              :class="{ disabled: !sysInfo?.networkText }"
              @click="copyNetworkInfo"
            ><CopyDocument /></el-icon>
          </CommonTooltip>
        </div>
        <pre class="netinfo-pre">{{ sysInfo?.networkText || '—' }}</pre>
      </div>
    </CommonDialog>

    <!-- WinRM 配置教程弹窗 -->
    <CommonDialog v-model="winrmHelpVisible" title="远程监控 WinRM 配置教程" width="680px" destroy-on-close>
      <ol class="remote-prereq-list" style="padding-left: 20px; font-size: 14px; line-height: 2;">
        <li>目标电脑以管理员打开 PowerShell，执行：<code>Enable-PSRemoting -Force</code></li>
        <li>目标电脑防火墙放行 WinRM 端口 5985（HTTP）；上一步通常已自动放行</li>
        <li>若本机与目标电脑不在同一域，需在本系统所在电脑执行：<br /><code>Set-Item WSMan:\localhost\Client\TrustedHosts -Value "目标IP地址" -Force</code><br /><span style="color: #909399; font-size: 12px;">将“目标IP地址”替换为实际的远程电脑 IP</span></li>
        <li>采集账号需为目标电脑管理员（如 <code>.\Administrator</code>）</li>
      </ol>
      <template #footer>
        <el-button type="primary" @click="winrmHelpVisible = false">知道了</el-button>
      </template>
    </CommonDialog>

    <!-- 监控列表（右侧抽屉弹出） -->
    <el-drawer v-model="listVisible" title="主机监控列表" direction="rtl" size="60%" destroy-on-close>
      <CommonDataTable
      :columns="columns"
      :data="targets"
      :loading="loading"
      :show-pagination="false"
      :actions-width="230"
      @load="load"
    >
      <template #toolbar>
        <el-button v-if="$has('host-monitor:create')" type="primary" size="small" @click="openAdd">新增监控</el-button>
        <el-button size="small" @click="load">刷新</el-button>
      </template>

      <template #cell-status="{ row }">
        <CommonTooltip
          v-if="(row as MonitorTarget).lastStatus === 2 && (row as MonitorTarget).lastErrorMsg"
          :content="(row as MonitorTarget).lastErrorMsg!"
        >
          <el-tag :type="statusTag(row as MonitorTarget).type" size="small" effect="dark">
            {{ statusTag(row as MonitorTarget).text }}
          </el-tag>
        </CommonTooltip>
        <el-tag v-else :type="statusTag(row as MonitorTarget).type" size="small" effect="dark">
          {{ statusTag(row as MonitorTarget).text }}
        </el-tag>
      </template>

      <template #cell-enabled="{ row }">
        <el-tag v-if="(row as MonitorTarget).enabled" type="success" size="small" effect="plain">启用</el-tag>
        <el-tag v-else type="info" size="small" effect="plain">停用</el-tag>
      </template>

      <template #actions="{ row }">
        <el-button
          v-if="$has('host-monitor:check')"
          link type="primary" size="small"
          :loading="checkingId === (row as MonitorTarget).id"
          @click="checkNow(row as MonitorTarget)"
        >检测</el-button>
        <el-button link type="primary" size="small" @click="openLogs(row as MonitorTarget)">日志</el-button>
        <el-button v-if="$has('host-monitor:edit')" link type="primary" size="small" @click="openEdit(row as MonitorTarget)">编辑</el-button>
        <el-button v-if="$has('host-monitor:delete')" link type="danger" size="small" @click="remove(row as MonitorTarget)">删除</el-button>
      </template>

      <template #empty>暂无监控目标，点击"新增监控"添加</template>
    </CommonDataTable>
    </el-drawer>

    <!-- 新增 / 编辑弹窗 -->
    <CommonDialog
      v-model="editVisible"
      :title="editForm.id ? '编辑监控目标' : '新增监控目标'"
      width="720px"
      destroy-on-close
    >
      <el-form ref="editFormRef" :model="editForm" :rules="editRules" label-width="110px">
        <el-form-item label="目标名称" prop="name">
          <el-input v-model="editForm.name" maxlength="100" placeholder="如：本机" />
        </el-form-item>
        <el-form-item label="监控指标">
          <el-select v-model="editForm.metricType" style="width: 340px">
            <el-option v-for="m in METRIC_OPTIONS" :key="m.value" :label="m.label" :value="m.value" />
          </el-select>
        </el-form-item>
        <el-form-item v-if="editForm.metricType === 'HOST'" label="目标电脑 IP">
          <el-input v-model="editForm.hostAddress" maxlength="100" placeholder="选填：如 192.168.1.10；留空监控本机" style="width: 340px" />
        </el-form-item>
        <!-- 远程监控使用前提（填了目标 IP 时提醒） -->
        <el-alert
          v-if="editForm.metricType === 'HOST' && editForm.hostAddress.trim()"
          type="info" :closable="false" class="remote-prereq"
        >
          <template #title>远程监控使用前提（仅需首次配置）</template>
          <ol class="remote-prereq-list">
            <li>目标电脑以管理员打开 PowerShell，执行：<code>Enable-PSRemoting -Force</code></li>
            <li>目标电脑防火墙放行 WinRM 端口 5985（HTTP）；上一步通常已自动放行</li>
            <li>若本机与目标电脑不在同一域，需在本系统所在电脑执行：<br /><code>Set-Item WSMan:\localhost\Client\TrustedHosts -Value "{{ editForm.hostAddress.trim() }}" -Force</code></li>
            <li>采集账号需为目标电脑管理员（如 <code>.\Administrator</code>）</li>
          </ol>
        </el-alert>
        <el-form-item
          v-if="editForm.metricType === 'HOST' && editForm.hostAddress.trim()"
          label="采集账号" prop="authAccount"
        >
          <el-input v-model="editForm.authAccount" maxlength="100" placeholder="如 .\Administrator 或 主机名\账号" style="width: 340px" />
        </el-form-item>
        <el-form-item
          v-if="editForm.metricType === 'HOST' && editForm.hostAddress.trim()"
          label="采集密码" prop="authPassword"
        >
          <el-input
            v-model="editForm.authPassword" type="password" show-password maxlength="200"
            :placeholder="editForm.id ? '留空表示不修改原密码' : '目标电脑登录密码'"
            style="width: 340px"
          />
        </el-form-item>
        <el-form-item v-if="editForm.metricType === 'DISK'" label="磁盘盘符">
          <el-input v-model="editForm.driveLetter" maxlength="2" placeholder="选填：如 C，留空监控所有固定磁盘" style="width: 340px" />
        </el-form-item>
        <el-form-item v-if="editForm.metricType === 'SVC'" label="服务名列表" prop="serviceNames">
          <el-input v-model="editForm.serviceNames" maxlength="500" placeholder="逗号分隔，如：W32Time,Spooler" />
        </el-form-item>
        <el-form-item v-if="editForm.metricType !== 'SVC'" label="告警阈值">
          <el-input-number
            v-model="editForm.thresholdPercent"
            :min="0" :max="100" :step="1" :precision="1"
            :controls="false" style="width: 120px"
          />
          <span class="unit-text">%（超过即异常；留空不做阈值判定）</span>
        </el-form-item>
        <el-form-item label="探测超时">
          <el-input-number v-model="editForm.timeoutSeconds" :min="5" :max="120" />
          <span class="unit-text">秒</span>
        </el-form-item>
        <el-form-item label="探测间隔">
          <el-input-number v-model="editForm.intervalMinutes" :min="1" :max="1440" />
          <span class="unit-text">分钟</span>
        </el-form-item>
        <el-form-item label="启用监控">
          <el-switch v-model="editForm.enabled" />
        </el-form-item>
        <el-form-item label="邮件告警">
          <el-switch v-model="editForm.notifyEmail" />
          <span class="unit-text">状态变化（正常↔异常）时邮件通知有主机监控权限的用户</span>
        </el-form-item>
        <el-form-item label="备注">
          <el-input v-model="editForm.remark" maxlength="200" placeholder="选填" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="editVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="save">保存</el-button>
      </template>
    </CommonDialog>

    <!-- 探测日志弹窗 -->
    <CommonDialog
      v-model="logVisible"
      :title="`探测日志 · ${logTarget?.name ?? ''}`"
      width="760px"
      destroy-on-close
    >
      <CommonDataTable
        v-model:page="logPage"
        v-model:pageSize="logSize"
        :columns="logColumns"
        :data="logs"
        :loading="logLoading"
        :total="logTotal"
        :page-sizes="[20, 50, 100]"
        max-height="52vh"
        compact
        pagination-layout="total, sizes, prev, pager, next"
        @load="loadLogs"
      >
        <template #cell-status="{ row }">
          <el-tag v-if="(row as MonitorLog).status === 1" type="success" size="small" effect="dark">正常</el-tag>
          <el-tag v-else type="danger" size="small" effect="dark">异常</el-tag>
        </template>

        <template #empty>暂无探测记录</template>
      </CommonDataTable>
    </CommonDialog>

    <!-- 磁盘清理：第一步选盘符并勾选清理分类 → 第二步勾选候选文件 -->
    <CommonDialog
      v-model="cleanVisible"
      :title="cleanStep === 'options' ? '清理磁盘' : '清理磁盘 · 勾选要删除的文件'"
      :width="cleanStep === 'options' ? '680px' : 'calc(100vw - 120px)'"
      class="clean-scan-dialog"
      destroy-on-close
    >
      <!-- 第一步：选择盘符与扫描范围；扫描中在原内容上叠加半透明加载遮罩（不关闭/不替换弹窗） -->
      <template v-if="cleanStep === 'options'">
        <!-- 步骤包裹层：作为加载遮罩的定位基准（不依赖弹窗内部结构，Teleport 场景下更可靠） -->
        <div class="clean-step-wrap">
        <el-alert type="info" :closable="false" show-icon>
          扫描将列出勾选清理类别下的全部文件（不按修改时间过滤），不会删除任何文件，下一步可勾选要删除哪些；
          清理项按所选盘符展示（系统缓存类仅系统盘有，非系统盘为该盘磁盘垃圾）；回收站无需勾选，扫描默认列出全部盘数据，删除勾选项
        </el-alert>
        <div class="clean-sec-title">清理盘符</div>
        <!-- 盘符卡片：不同盘符不同颜色区分 -->
        <div class="clean-drive-cards">
          <div
            v-for="l in cleanDriveOptions" :key="l"
            class="clean-drive-card" :class="{ active: cleanDrive === l }"
            :style="{ '--c': driveColor(l) }"
            @click="cleanDrive = l"
          >
            <span class="clean-drive-letter">{{ l }}</span>
            <span class="clean-drive-sub">{{ l }} 盘</span>
          </div>
        </div>
        <!-- 非系统盘说明：清理项已按盘符过滤，仅展示该盘可用项 -->
        <div v-if="cleanDrive !== 'C'" class="clean-drive-hint">
          {{ cleanDrive }} 盘为非系统盘：系统缓存类清理项只存在于系统盘，这里仅展示该盘可用的清理项（磁盘垃圾）；回收站无需勾选，扫描默认列出全部盘数据
        </div>
        <div class="clean-sec-title">清理内容（可多选）</div>
        <!-- 分类卡片：不同类别不同颜色区分，选中后边框/底色/✓ 高亮 -->
        <div class="clean-cat-cards">
          <div
            v-for="opt in CLEAN_OPTIONS" :key="opt.value"
            class="clean-cat-card" :class="{ active: cleanCategories.includes(opt.value) }"
            :style="{ '--c': CATEGORY_COLOR[opt.value] ?? '#409eff' }"
            @click="toggleCategory(opt.value)"
          >
            <span class="clean-cat-dot"></span>
            <span class="clean-cat-name">{{ opt.label }}</span>
            <span class="clean-cat-check">✓</span>
          </div>
        </div>
        <!-- 扫描中：弹窗内半透明遮罩 + 原进度条样式，叠加在原选项内容上，后端逐百条上报进度 -->
        <div v-if="scanning" class="clean-dialog-mask">
          <div class="clean-mask-panel">
            <el-progress :percentage="100" :indeterminate="true" :show-text="false" :stroke-width="10" />
            <div class="clean-mask-text">
              正在扫描：已扫描 <b>{{ scanProgress?.count ?? 0 }}</b> 个文件，已发现 <b>{{ fmtSize(scanProgress?.kb ?? 0) }}</b>…
            </div>
          </div>
        </div>
        </div>
      </template>
      <!-- 第二步：勾选文件类清理项时展示候选文件清单；仅勾回收站时展示回收站确认视图 -->
      <template v-else-if="scanData">
        <!-- 步骤包裹层：作为清理遮罩的定位基准 -->
        <div class="clean-step-wrap">
        <!-- 文件类：左侧汇总+饼图侧栏，右侧文件表格，表格高度随视口自适应避免弹窗内滚动条 -->
        <div v-if="hasFileCategories" class="clean-scan-body">
          <div class="clean-scan-side">
            <div class="clean-summary">
              扫描到 <b>{{ scanData.files.length }}</b> 个文件，共 <b>{{ fmtSize(scanTotalKb) }}</b>
              ；<br/>回收站（全部盘） <b>{{ scanData.recycleCount }}</b> 项（约 {{ fmtSize(scanData.recycleSizeKb) }}）
              <span v-if="scanData.truncated" class="clean-trunc">（仅展示前 3000 条）</span>
            </div>
            <div v-if="scanData.files.length > 0" class="scan-pie-chart">
              <BaseChart :option="scanPieOption" height="330px" />
            </div>
          </div>
          <div ref="cleanWidthEl" class="clean-scan-tables">
            <div class="clean-scan-toolbar">
              <el-input
                v-model="scanKeyword" size="small" clearable
                placeholder="搜索文件路径 / 名称 / 分类" :prefix-icon="Search"
                class="clean-scan-search"
              />
              <!-- 分类筛选：选项为扫描结果中实际存在的分类，带同色圆点与条数 -->
              <el-select v-model="scanCatFilter" size="small" clearable placeholder="全部分类" class="clean-scan-cat-filter">
                <el-option v-for="c in scanCatOptions" :key="c.value" :value="c.value" :label="`${c.label}（${c.count} 个）`">
                  <span
                    class="clean-cat-filter-dot"
                    :style="{ background: CATEGORY_COLOR_BY_FILE_CAT[c.value] ?? '#909399' }"
                  ></span>{{ c.label }}（{{ c.count }} 个）
                </el-option>
              </el-select>
              <span v-if="scanKeyword.trim() || scanCatFilter" class="clean-scan-toolbar-hint">
                匹配 {{ filteredScanFiles.length }} / {{ scanData.files.length }} 条
              </span>
              <!-- 重新扫描：按当前盘符与分类重扫，靠右对齐；扫描/清理进行中禁用 -->
              <el-button size="small" :icon="Refresh" :disabled="scanning || cleaning" class="clean-rescan-btn" @click="rescan">重新扫描</el-button>
            </div>
            <!-- 文件列表标题行：可折叠，与回收站标题行同构 -->
            <div class="clean-recycle-title clean-recycle-toggle" @click="toggleFileCollapse">
              <span class="clean-recycle-arrow">{{ fileCollapsed ? '▶' : '▼' }}</span>
              候选文件（删除勾选项）：{{ filteredScanFiles.length }} 个
              <span class="clean-recycle-hint">{{ fileCollapsed ? '点击展开' : '点击折叠' }}</span>
            </div>
            <!-- 虚拟滚动表格（el-table-v2）：3000+ 行只渲染可视区域，无分页；勾选由 Set 全量管理不丢失 -->
            <!-- 宽度由自管 RO 测量（替代 el-auto-resizer 避免尺寸反馈循环抖动），高度已知直接传入 -->
            <div v-show="!fileCollapsed" :style="{ height: scanTableHeight + 'px', overflow: 'hidden' }">
              <el-table-v2
                v-if="cleanTableWidth > 0"
                :columns="scanColumns"
                :data="filteredScanFiles"
                :width="cleanTableWidth"
                :height="scanTableHeight"
                :row-height="36"
                row-key="path"
                fixed
                :sort-by="scanSortBy ?? undefined"
                class="clean-virtual-table"
                empty-text="勾选项中没有可清理的文件"
                @column-sort="onScanSort"
              />
            </div>
            <!-- 回收站实时全量清单（全部盘，不做有效期过滤）：始终展示、默认折叠，删除勾选项；
                 标题行右侧【回收站】按钮直接打开系统回收站（仅本机目标），click.stop 避免触发折叠 -->
              <div class="clean-recycle-title clean-recycle-toggle" @click="toggleRecycleCollapse">
                <span class="clean-recycle-arrow">{{ recycleCollapsed ? '▶' : '▼' }}</span>
                回收站内容（全部盘实时全量，删除勾选项）：{{ scanData.recycleCount }} 项
                <span class="clean-recycle-hint">{{ recycleCollapsed ? '点击展开' : '点击折叠' }}</span>
                <el-button v-if="dashTarget?.isLocal" size="small" class="clean-open-recycle-btn" @click.stop="openRecycleBin">回收站</el-button>
              </div>
              <div v-show="!recycleCollapsed" :style="{ height: recycleTableHeight + 'px', overflow: 'hidden' }">
                <el-table-v2
                  v-if="cleanTableWidth > 0"
                  :columns="recycleColumns"
                  :data="scanData.recycleFiles"
                  :width="cleanTableWidth"
                  :height="recycleTableHeight"
                  :row-height="36"
                  row-key="path"
                  fixed
                  class="clean-virtual-table"
                  empty-text="回收站当前为空"
                />
              </div>
          </div>
        </div>
        <!-- 仅勾回收站：无文件表格，直接展示回收站确认视图 -->
        <template v-else>
          <div class="clean-summary">
            本次清理 <b>{{ cleanDrive }}</b> 盘回收站：共 <b>{{ scanData.recycleCount }}</b> 项
            （全部盘合计约 {{ fmtSize(scanData.recycleSizeKb) }}），删除勾选项
          </div>
          <div ref="cleanWidthEl" :style="{ height: recycleTableHeight + 'px', overflow: 'hidden' }">
            <el-table-v2
              v-if="cleanTableWidth > 0"
              :columns="recycleColumns"
              :data="scanData.recycleFiles"
              :width="cleanTableWidth"
              :height="recycleTableHeight"
              :row-height="36"
              row-key="path"
              fixed
              class="clean-virtual-table"
              empty-text="回收站当前为空"
            />
          </div>
        </template>
        <!-- 重新扫描：与第一步同款半透明扫描遮罩，叠加在列表内容上展示扫描进度 -->
        <div v-if="scanning" class="clean-dialog-mask">
          <div class="clean-mask-panel">
            <el-progress :percentage="100" :indeterminate="true" :show-text="false" :stroke-width="10" />
            <div class="clean-mask-text">
              正在扫描：已扫描 <b>{{ scanProgress?.count ?? 0 }}</b> 个文件，已发现 <b>{{ fmtSize(scanProgress?.kb ?? 0) }}</b>…
            </div>
          </div>
        </div>
        <!-- 清理中：弹窗内半透明遮罩 + 原进度条样式，叠加在表格内容上，实时显示删除进度 -->
        <div v-if="cleaning && cleanProgress" class="clean-dialog-mask">
          <div class="clean-mask-panel">
            <el-progress
              :percentage="cleanPercent"
              :indeterminate="cleanProgress.total === 0"
              :show-text="false"
              :stroke-width="10"
            />
            <div class="clean-mask-text">
              正在清理：已删除 <b>{{ cleanProgress.deleted }}</b><template v-if="cleanProgress.total > 0"> / {{ cleanProgress.total }}</template> 个
              · 已释放 <b>{{ cleanProgress.freedMb.toFixed(1) }}</b> MB…
            </div>
          </div>
        </div>
        </div>
      </template>
      <template #footer>
        <template v-if="cleanStep === 'options'">
          <el-button @click="cleanVisible = false">取消</el-button>
          <el-button type="primary" :loading="scanning" :disabled="cleanCategories.length === 0" @click="scanFiles">
            扫描文件
          </el-button>
        </template>
        <template v-else>
          <el-button :disabled="cleaning" @click="cleanStep = 'options'">上一步</el-button>
          <el-button
            type="primary" :loading="cleaning"
            :disabled="cleaning || (selectedFiles.length === 0 && selectedRecycle.length === 0)"
            @click="doCleanDisk"
          >
            {{ cleanButtonText }}
          </el-button>
        </template>
      </template>
    </CommonDialog>

    <!-- 清理结果：汇总 + 已删除文件清单（带进入动画） -->
    <CommonDialog v-model="cleanResultVisible" title="清理结果" width="620px" destroy-on-close>
      <template v-if="cleanResult">
        <div class="clean-result-header">
          <div class="clean-result-icon">✔</div>
          <div class="clean-result-stats">
            <div class="clean-stat-card">
              <span class="clean-stat-num">{{ cleanResult.freedMb }}</span>
              <span class="clean-stat-label">释放 MB</span>
            </div>
            <div class="clean-stat-card">
              <span class="clean-stat-num">{{ cleanResult.deletedFiles }}</span>
              <span class="clean-stat-label">删除文件数</span>
            </div>
            <div class="clean-stat-card">
              <span class="clean-stat-num">{{ cleanResult.freeGbAfter }}</span>
              <span class="clean-stat-label">{{ cleanDrive }} 盘剩余 GB</span>
            </div>
          </div>
        </div>
        <span v-if="cleanResult.filesTruncated" class="clean-trunc">文件清单仅展示前 2000 条</span>
        <!-- 勾选逐项结果（选择性清理/回收站勾选）：每个勾选项展示成功/失败原因；整类清理无 items 时退回已删除清单 -->
        <div v-if="cleanResult.items.length" class="clean-file-list">
          <div v-for="(it, i) in cleanResult.items" :key="i" class="clean-file-item clean-item-row">
            <span :class="it.ok ? 'clean-item-ok' : 'clean-item-fail'">{{ it.ok ? '✔ 已删除' : '✘ ' + it.reason }}</span>
            <span class="clean-item-path" :title="it.path">{{ recycleDisplayPath(it.path) }}</span>
          </div>
        </div>
        <div v-else-if="cleanResult.files.length" class="clean-file-list">
          <div v-for="(f, i) in cleanResult.files" :key="i" class="clean-file-item">{{ i + 1 }}. {{ recycleDisplayPath(f) }}</div>
        </div>
        <div v-else class="clean-file-empty">本次勾选项中没有可删除的文件</div>
      </template>
    </CommonDialog>
  </div>
</template>

<style scoped>
.monitor-page {
  height: 100%;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

/* ===== 机器概览 Dashboard（标题栏吸顶） ===== */
.host-dashboard {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  padding: 0 16px 16px;
}
.host-dashboard-bar {
  position: sticky;
  top: 0;
  z-index: 10;
  display: flex;
  align-items: center;
  gap: 10px;
  /* 负外边距撑满滚动容器，吸顶背景覆盖全宽，内容仍与下方卡片对齐 */
  margin: 0 -16px 14px;
  padding: 10px 16px;
  background: var(--page-bg, #f2f5f7);
  border-bottom: 1px solid #e4e7ed;
}
.host-dashboard-title {
  font-size: 14px;
  font-weight: 600;
  color: #303133;
}
.host-dashboard-hint {
  margin-left: auto;
  font-size: 12px;
  font-weight: 400;
  color: #909399;
}
.host-dashboard-link {
  cursor: pointer;
  transition: color 0.15s;
}
.host-dashboard-link:hover {
  color: var(--el-color-primary);
  text-decoration: underline;
}

/* 统计卡 */
.host-stat-row {
  display: flex;
  gap: 12px;
  margin-bottom: 14px;
}
.host-stat {
  flex: 0 0 auto;
  min-width: 110px;
  background: #fff;
  border: 1px solid #ebeef5;
  border-radius: 12px;
  padding: 12px 16px;
  text-align: center;
  box-shadow: 0 1px 4px rgba(0, 0, 0, 0.04);
}
.host-stat-wide {
  flex: 1;
  min-width: 0;
}
.host-stat-value {
  font-size: 20px;
  font-weight: 600;
  color: #2f9e44;
  line-height: 1.4;
}
.host-stat-os {
  font-size: 13px;
  font-weight: 500;
  color: #303133;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.host-stat-label {
  margin-top: 2px;
  font-size: 12px;
  color: #909399;
}

/* 时间序列图表 */
.host-charts {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 14px;
}
.sysinfo-dialog-body {
  min-height: 200px;
}
.sysinfo-net-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-top: 16px;
}
.sysinfo-header-actions {
  display: flex;
  align-items: center;
  gap: 12px;
}
.sysinfo-spec-header {
  margin-top: 0;
}
.netinfo-copy {
  font-size: 17px;
  color: #909399;
  cursor: pointer;
  transition: color 0.2s;
}
.netinfo-copy:hover {
  color: #409eff;
}
.netinfo-copy.disabled {
  color: #c0c4cc;
  cursor: not-allowed;
}
.host-stat-clickable {
  cursor: pointer;
  transition: border-color 0.2s, box-shadow 0.2s;
}
.host-stat-clickable:hover {
  border-color: #409eff;
  box-shadow: 0 2px 8px rgba(64, 158, 255, 0.18);
}
.sysinfo-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 6px 24px;
  padding: 4px 2px;
}
.sysinfo-item {
  display: flex;
  gap: 12px;
  font-size: 13px;
  line-height: 1.8;
}
.sysinfo-label {
  flex: 0 0 64px;
  color: #909399;
}
.sysinfo-value {
  color: #303133;
  word-break: break-all;
}
.netinfo-pre {
  margin: 0;
  padding: 10px 12px;
  max-height: 340px;
  overflow: auto;
  background: #f7f8fa;
  border-radius: 8px;
  font-size: 12px;
  line-height: 1.6;
  font-family: Consolas, 'Courier New', monospace;
  white-space: pre;
}
.host-chart-panel {
  background: #fff;
  border: 1px solid #ebeef5;
  border-radius: 12px;
  padding: 12px 14px;
  box-shadow: 0 1px 4px rgba(0, 0, 0, 0.04);
}
.host-chart-title {
  font-size: 13px;
  font-weight: 600;
  color: #606266;
  margin-bottom: 4px;
}

/* 磁盘分区表（位于统计卡与图表之间，上下均需留间隔） */
.host-disk-panel {
  margin-bottom: 14px;
  background: #fff;
  border: 1px solid #ebeef5;
  border-radius: 12px;
  padding: 12px 14px;
  box-shadow: 0 1px 4px rgba(0, 0, 0, 0.04);
}
.host-disk-panel .host-chart-title {
  margin-bottom: 0;
}
/* 分区表头部：标题居左、清理按钮居右 */
.host-disk-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 10px;
}
.host-card-empty {
  padding: 10px 0;
  font-size: 12px;
  color: #909399;
}

/* 远程监控使用前提提示：左边距缩进对齐表单输入框；宽度必须同步减去该缩进，
   否则 margin-left 叠加在 100% 宽度上会撑出弹窗横向滚动条 */
.remote-prereq {
  margin: 0 0 16px 110px;
  width: calc(100% - 110px);
  align-items: flex-start;
}
.remote-prereq-list {
  margin: 4px 0 0;
  padding-left: 18px;
  font-size: 12px;
  line-height: 1.9;
  color: #606266;
  overflow-wrap: anywhere;   /* 长命令允许断行，避免撑出弹窗横向滚动条 */
}
.remote-prereq-list code {
  padding: 1px 5px;
  border-radius: 4px;
  word-break: break-all;   /* TrustedHosts 等长命令强制换行 */
  background: rgba(64, 158, 255, 0.08);
  color: #409eff;
  font-family: Consolas, monospace;
  word-break: break-all;
}

/* 日期/时间列内容不换行，保证完整展示 */
:deep(.cell-nowrap .cell) {
  white-space: nowrap;
}

.unit-text {
  margin-left: 8px;
  color: #909399;
  font-size: 12px;
}

/* 磁盘清理弹窗 */
/* 扫描第二步弹窗几乎占满视口：缩小顶部留白，空间尽量留给文件表格 */
.clean-scan-dialog {
  --el-dialog-margin-top: 6vh;
}
.clean-scan-body {
  display: flex;
  gap: 16px;
  align-items: flex-start;
}
.clean-scan-side {
  flex: 0 0 300px;
}
.clean-scan-tables {
  flex: 1;
  min-width: 0;
}
/* 分类标签：轻量 span 替代 el-tag，大行数下渲染开销更低 */
.clean-virtual-table :deep(.clean-cat) {
  display: inline-block;
  padding: 1px 8px;
  font-size: 12px;
  color: #606266;
  background: #f0f2f5;
  border-radius: 4px;
}
/* 第一步选项区：分区标题 + 盘符卡片 + 分类卡片，不同盘符/类别用 --c 颜色变量区分 */
.clean-sec-title {
  margin: 16px 0 10px;
  font-size: 13px;
  font-weight: 600;
  color: #303133;
}
.clean-drive-cards {
  display: flex;
  gap: 12px;
  flex-wrap: wrap;
}
/* 非系统盘说明提示 */
.clean-drive-hint {
  margin-top: 6px;
  font-size: 12px;
  color: #e6a23c;
}
.clean-drive-card {
  width: 84px;
  padding: 12px 0;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 4px;
  border: 1px solid #e4e7ed;
  border-radius: 10px;
  background: #fff;
  cursor: pointer;
  transition: all 0.15s;
}
.clean-drive-card:hover {
  border-color: var(--c);
}
.clean-drive-card.active {
  border-color: var(--c);
  box-shadow: 0 0 0 1px var(--c) inset;
  background: #f8fafc;
  background: color-mix(in srgb, var(--c) 8%, #fff);
}
.clean-drive-letter {
  font-size: 22px;
  font-weight: 700;
  line-height: 1;
  color: var(--c);
}
.clean-drive-sub {
  font-size: 12px;
  color: #909399;
}
.clean-drive-card.active .clean-drive-sub {
  color: var(--c);
}
.clean-cat-cards {
  display: grid;
  grid-template-columns: repeat(2, 1fr);
  gap: 10px;
}
.clean-cat-card {
  position: relative;
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 11px 22px 11px 12px;
  border: 1px solid #e4e7ed;
  border-radius: 10px;
  background: #fff;
  cursor: pointer;
  font-size: 13px;
  color: #606266;
  transition: all 0.15s;
}
.clean-cat-card:hover {
  border-color: var(--c);
}
.clean-cat-card.active {
  border-color: var(--c);
  box-shadow: 0 0 0 1px var(--c) inset;
  background: #f8fafc;
  background: color-mix(in srgb, var(--c) 8%, #fff);
  color: #303133;
}
.clean-cat-dot {
  flex: none;
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: var(--c);
}
.clean-cat-name {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.clean-cat-check {
  position: absolute;
  top: 4px;
  right: 8px;
  font-size: 11px;
  color: var(--c);
  display: none;
}
.clean-cat-card.active .clean-cat-check {
  display: block;
}
/* 弹窗内半透明加载遮罩：保留原进度条样式，叠加在原内容上不关闭弹窗 */
.clean-step-wrap {
  position: relative;   /* 遮罩定位基准：用内容包裹层而非弹窗 body，避免 Teleport 弹窗下定位失效 */
}
.clean-dialog-mask {
  position: absolute;
  inset: 0;
  z-index: 10;
  display: flex;
  align-items: center;
  justify-content: center;
  background: rgba(255, 255, 255, 0.75);
}
.clean-mask-panel {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 14px;
  width: 80%;
  max-width: 480px;
}
/* 关键：flex 纵向布局 + align-items:center 会使子项宽度收缩为内容宽（0），
   横向进度条必须强制擑满面板宽度才能可见 */
.clean-mask-panel .el-progress {
  width: 100%;
  align-self: stretch;
}
.clean-mask-text {
  font-size: 12px;
  color: #606266;
}
.clean-recycle-title {
  margin: 6px 0;   /* 标题行总高需与 calcCleanTableHeights 的 TITLE 常量匹配，避免高度累计溢出出滚动条 */
  font-size: 13px;
  color: #606266;
}
/* 文件列表搜索工具栏 */
.clean-scan-toolbar {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-bottom: 8px;
}
.clean-scan-search {
  width: 280px;
}
/* 分类筛选下拉：选项内同色圆点 */
.clean-scan-cat-filter {
  width: 170px;
}
.clean-cat-filter-dot {
  display: inline-block;
  width: 8px;
  height: 8px;
  border-radius: 50%;
  margin-right: 6px;
  vertical-align: middle;
}
.clean-scan-toolbar-hint {
  font-size: 12px;
  color: #909399;
}
/* 重新扫描按钮靠右对齐 */
.clean-rescan-btn {
  margin-left: auto;
}
/* 虚拟滚动表格（el-table-v2）：细边框 + 表头底色对齐原 el-table 观感，单元格超长省略并原生 title 提示 */
.clean-virtual-table {
  border: 1px solid var(--el-border-color-lighter);
}
.clean-virtual-table :deep(.el-table-v2__header-cell) {
  background: #f5f7fa;
  color: #606266;
  font-weight: 600;
  font-size: 12px;
}
.clean-virtual-table :deep(.el-table-v2__row-cell) {
  font-size: 12px;
  border-bottom: 1px solid var(--el-border-color-lighter);
}
/* 以下单元格样式均作用于 cellRenderer 的 h() 节点：它们在 el-table-v2 内部渲染、拿不到本组件 scoped 属性，
   必须经 .clean-virtual-table :deep() 下发，否则不生效（图标原尺寸撑高行、文本换行溢出） */
.clean-virtual-table :deep(.clean-cell-ellipsis) {
  display: block;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  /* el-table-v2 单元格为 flex 布局：flex 项默认按内容撑宽，必须限制最大宽并允许收缩，
     否则 nowrap 失效、长路径溢出换行，无法单行省略 */
  max-width: 100%;
  min-width: 0;
}
/* 文件名单元格：真实系统图标 + 名称省略号（flex 布局：图标不缩、与文本垂直居中，文本收缩省略） */
.clean-virtual-table :deep(.clean-fname) {
  display: flex;
  align-items: center;
  gap: 5px;
  max-width: 100%;
  min-width: 0;
  overflow: hidden;
}
.clean-virtual-table :deep(.clean-fname-text) {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.clean-virtual-table :deep(.clean-ficon-img) {
  width: 15px;
  height: 15px;
  flex-shrink: 0;
}
.clean-virtual-table :deep(.clean-ficon) {
  font-size: 14px;
  flex-shrink: 0;
}
.clean-virtual-table :deep(.clean-ficon-default) { color: #909399; }
.clean-virtual-table :deep(.clean-ficon-green) { color: #67c23a; }
.clean-virtual-table :deep(.clean-ficon-purple) { color: #9b59b6; }
.clean-virtual-table :deep(.clean-ficon-cyan) { color: #06b6d4; }
.clean-virtual-table :deep(.clean-ficon-yellow) { color: #e6a23c; }
.clean-virtual-table :deep(.clean-ficon-blue) { color: #409eff; }
.clean-virtual-table :deep(.clean-ficon-indigo) { color: #5c6bc0; }
.clean-virtual-table :deep(.clean-ficon-gray) { color: #909399; }
.clean-virtual-table :deep(.clean-ficon-red) { color: #f56c6c; }
/* 回收站标题可点击折叠/展开 */
.clean-recycle-toggle {
  display: flex;
  align-items: center;
  gap: 6px;
  cursor: pointer;
  user-select: none;
}
/* 标题行右侧【回收站】按钮：靠右对齐；small 尺寸(24px)+上下 margin 12px 恰好等于 TITLE 常量 36px，不撑高行 */
.clean-open-recycle-btn {
  margin-left: auto;
}
.clean-recycle-toggle:hover {
  color: #409eff;
}
.clean-recycle-arrow {
  font-size: 10px;
  color: #909399;
}
.clean-recycle-hint {
  font-size: 12px;
  color: #c0c4cc;
}
.clean-summary {
  margin-bottom: 10px;
  font-size: 13px;
  color: #303133;
}
.scan-pie-chart {
  margin-top: 8px;
  padding: 4px 0;
}
.clean-trunc {
  margin-left: 6px;
  font-size: 12px;
  color: #909399;
}
.clean-file-list {
  max-height: 320px;
  overflow-y: auto;
  padding: 6px 10px;
  border: 1px solid #ebeef5;
  border-radius: 6px;
  background: #fafafa;
}
.clean-file-item {
  font-family: Consolas, monospace;
  font-size: 12px;
  line-height: 1.8;
  color: #606266;
  word-break: break-all;
}
/* 勾选逐项清理结果：状态标签不缩 + 路径单行省略 */
.clean-item-row {
  display: flex;
  align-items: center;
  gap: 8px;
}
.clean-item-ok { color: #67c23a; flex-shrink: 0; }
.clean-item-fail { color: #f56c6c; flex-shrink: 0; }
.clean-item-path {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.clean-file-empty {
  font-size: 12px;
  color: #909399;
}

/* 清理结果头部：成功图标 + 统计卡片，带进入动画 */
.clean-result-header {
  display: flex;
  align-items: center;
  gap: 20px;
  margin-bottom: 16px;
  animation: cleanResultFadeIn 0.4s ease-out;
}
@keyframes cleanResultFadeIn {
  from {
    opacity: 0;
    transform: translateY(10px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}
.clean-result-icon {
  flex-shrink: 0;
  width: 48px;
  height: 48px;
  border-radius: 50%;
  background: #f0f9eb;
  color: #67c23a;
  font-size: 24px;
  display: flex;
  align-items: center;
  justify-content: center;
  animation: cleanResultPulse 0.6s ease-out 0.3s both;
}
@keyframes cleanResultPulse {
  0% {
    transform: scale(0);
    opacity: 0;
  }
  70% {
    transform: scale(1.15);
  }
  100% {
    transform: scale(1);
    opacity: 1;
  }
}
.clean-result-stats {
  display: flex;
  gap: 12px;
  flex-wrap: wrap;
}
.clean-stat-card {
  flex: 1;
  min-width: 90px;
  padding: 10px 14px;
  background: #f5f7fa;
  border-radius: 8px;
  text-align: center;
  transition: transform 0.2s;
}
.clean-stat-card:hover {
  transform: translateY(-2px);
}
.clean-stat-num {
  display: block;
  font-size: 20px;
  font-weight: 700;
  color: #303133;
  margin-bottom: 4px;
}
.clean-stat-label {
  font-size: 11px;
  color: #909399;
}
</style>

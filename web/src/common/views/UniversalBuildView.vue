<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, reactive, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox, ElNotification } from 'element-plus'
import {
  CircleCheck,
  CircleClose,
  Monitor,
  Folder,
  FolderOpened,
  DocumentCopy,
  Plus,
  Delete,
  InfoFilled,
  UploadFilled,
  VideoPlay,
  Link,
  ArrowDown,
  RefreshRight,
  RefreshLeft,
  Download,
  Upload,
  Clock,
  Promotion,
  Collection,
  AlarmClock,
  Brush,
} from '@element-plus/icons-vue'
import {
  checkUniversalEnvironment,
  checkUniversalEnvironmentForType,
  startUniversalBuild,
  getUniversalBuildProgress,
  cancelUniversalBuild,
  getUniversalDefaultOutputDir,
  startDeploy,
  getDeployProgress,
  cancelDeploy,
  startRollback,
  checkSiteExists,
  getDeployHistory,
  getScheduleList,
  setSchedule,
  removeSchedule,
  type UniversalBuildType,
  type UniversalBuildStatus,
  type UniversalEnvironmentInfo,
  type UniversalBuildJobDto,
  type DeployTargetOS,
  type DeployStatus,
  type DeployJobDto,
  type SiteExistsResult,
  type DeployHistoryItem,
  type ScheduleItem,
  saveSshCredential,
  getSshCredential,
  getDeployLog,
  getArtifactUsage,
  cleanArtifact,
  type DeployLogResult,
} from '@/common/api/universalBuild'
import { selectFolder, openOutputFolder } from '@/common/api/universalBuild'
import { loadUiStateString, saveUiStateString } from '@/common/api/uiState'

/** 部署配置快照：手动部署成功启动时记录，构建成功后自动部署复用 */
/** 部署模版：一套完整部署配置（不含密码），存 exe 目录 ui-state.json（清缓存/重装不丢）；弹窗填充与自动部署复用 */
interface DeployTemplate {
  id: string
  name: string
  targetOS: DeployTargetOS
  siteName: string
  serviceName: string
  remoteDir: string
  archiveName: string
  deployPath: string
  verifyHealth: boolean
  keepDatabase: boolean
  host: string
  userName: string
}

interface DeployConfigSnapshot {
  targetOS: DeployTargetOS
  siteName: string
  serviceName: string
  remoteDir: string
  archiveName: string
  deployPath: string
  verifyHealth: boolean
  keepDatabase: boolean
  host: string
  userName: string
}

interface BuildCard {
  id: string
  name: string
  type: UniversalBuildType
  projectDir: string
  outputDir: string
  jobId: string | null
  status: UniversalBuildStatus | null
  progress: number
  /** 排队位置（Waiting 时后端返回，1 起） */
  queuePosition: number
  log: string
  /** 构建前环境检测输出（前端生成，与后端构建日志拼接显示） */
  preLog: string
  deployJobId: string | null
  deployStatus: DeployStatus | null
  deployLog: string
  /** 部署整体进度（0-100），上传段为字节级真实进度 */
  deployProgress: number
  /** 部署当前步骤文本（如 [5/7] 构建 Docker 镜像，后端返回拼好） */
  deployStepText: string
  /** 部署进度显示值：档位间缓慢爬行的视觉值，真实值存 deployProgress */
  deployDisplayProgress: number
  outputDirCustom: boolean
  /** 构建前先 git pull 拉取远端最新代码 */
  prePull: boolean
  /** 构建成功后把产物目录打包成 zip（落在输出目录的父目录，时间戳命名） */
  packArtifact: boolean
  /** 构建成功后自动用上次部署配置部署 */
  autoDeploy: boolean
  /** 上次成功启动部署的参数快照（自动部署用，不持久化） */
  lastDeployConfig: DeployConfigSnapshot | null
  /** 自动部署绑定的部署模版 id（空 = 用 lastDeployConfig 快照，随卡片持久化） */
  deployTemplateId: string
  /** 构建产物总大小（字节，构建成功后由后端统计） */
  artifactSize: number | null
  /** 构建成功后打包的 zip 路径/大小（勾选打压缩包且成功时有值，运行态不持久化） */
  artifactArchivePath: string
  artifactArchiveSize: number | null
  /** 本次构建开始/结束时间戳（毫秒，进行中实时计时） */
  startTime: number | null
  endTime: number | null
  /** 本次部署开始/结束时间戳 */
  deployStartTime: number | null
  deployEndTime: number | null
  /** 部署目标（成功后“打开站点”用） */
  deployHost: string
  deployServiceName: string
}

const typeOptions: { label: string; value: UniversalBuildType; icon?: string }[] = [
  { label: 'Web 前端', value: 'Web' },
  { label: 'Node 项目', value: 'Node' },
  { label: 'C# (.NET)', value: 'DotNet' },
  { label: 'Java Maven', value: 'JavaMaven' },
  { label: 'Java Gradle', value: 'JavaGradle' },
  { label: '安装包打包', value: 'Installer' },
]

const typeLabelMap = Object.fromEntries(typeOptions.map((x) => [x.value, x.label]))

/** 跳转流水线页面（多阶段构建→部署顺序执行） */
const router = useRouter()
function gotoPipeline() {
  router.push('/pipeline')
}

/** 构建类型对应的颜色（用于卡片左边框） */
const typeColorMap: Record<UniversalBuildType, string> = {
  Web: '#42b883',
  Node: '#0ea5e9',
  DotNet: '#8b5cf6',
  JavaMaven: '#f97316',
  JavaGradle: '#f59e0b',
  Installer: '#ef4444',
}

/** 获取构建类型对应的颜色 */
function typeColor(type: UniversalBuildType) {
  return typeColorMap[type] || '#909399'
}

/** 构建类型短标签（用于任务名称前缀） */
const typeShortLabelMap: Record<UniversalBuildType, string> = {
  Web: 'Web', Node: 'Node', DotNet: 'DotNet',
  JavaMaven: 'Maven', JavaGradle: 'Gradle', Installer: 'Installer',
}

/** 默认任务名称：类型前缀 + 序号 */
function defaultCardName(type: UniversalBuildType, index: number) {
  return `[${typeShortLabelMap[type] || type}] 构建任务 ${index}`
}

/** 去掉名称中的类型前缀（兼容 [Web] 新格式和 Web 旧格式） */
function stripTypeName(name: string): string {
  const m = name.match(/^\[([^\]]+)\]\s*(.+)$/)
  if (m) return m[2]
  for (const short of Object.values(typeShortLabelMap)) {
    if (name.startsWith(short + ' ')) return name.slice(short.length + 1)
  }
  return name
}

/** 各环境检测命令映射，悬浮显示 */
const envCommandMap: Record<string, string> = {
  node: 'cmd /c node --version',
  npm: 'cmd /c npm --version',
  dotnet: 'cmd /c dotnet --version',
  java: 'cmd /c java -version',
  mvn: 'cmd /c mvn -version',
  gradle: 'cmd /c gradle -v',
  iscc: 'cmd /c iscc /?',
}

const envInfo = ref<UniversalEnvironmentInfo[]>([])
const envLoading = ref(false)
/** 环境检测弹窗：页面打开时默认弹出，工具栏可随时唤起 */
const envVisible = ref(false)
const cards = reactive<BuildCard[]>([])
const selectedCardId = ref<string | null>(null)
const logTerminalRef = ref<HTMLDivElement | null>(null)

const selectedCard = computed(() => cards.find((c) => c.id === selectedCardId.value) ?? cards[0])

function createCard(): BuildCard {
  const id = crypto.randomUUID()
  return {
    id,
    name: defaultCardName('Web', cards.length + 1),
    type: 'Web',
    projectDir: '',
    outputDir: '',
    jobId: null,
    status: null,
    progress: 0,
    queuePosition: 0,
    log: '',
    preLog: '',
    deployJobId: null,
    deployStatus: null,
    deployLog: '',
    deployProgress: 0,
    deployStepText: '',
    deployDisplayProgress: 0,
    outputDirCustom: false,
    prePull: false,
    packArtifact: false,
    autoDeploy: false,
    lastDeployConfig: null,
    deployTemplateId: '',
    artifactSize: null,
    artifactArchivePath: '',
    artifactArchiveSize: null,
    startTime: null,
    endTime: null,
    deployStartTime: null,
    deployEndTime: null,
    deployHost: '',
    deployServiceName: '',
  }
}

function addCard() {
  const card = createCard()
  cards.push(card)
  selectedCardId.value = card.id
  updateDefaultOutputDir(card)
}

function removeCard(id: string) {
  const idx = cards.findIndex((c) => c.id === id)
  if (idx < 0) return
  // 同步移除关联的定时构建（避免孤儿调度空转）
  const schedule = schedules.value.find((s) => s.cardId === id)
  if (schedule?.id) {
    removeSchedule(schedule.id).then(
      () => { schedules.value = schedules.value.filter((s) => s.id !== schedule.id) },
      () => { /* 删除失败不影响卡片删除 */ },
    )
  }
  cards.splice(idx, 1)
  if (selectedCardId.value === id) {
    selectedCardId.value = cards[0]?.id ?? null
  }
}

async function loadEnvironment() {
  envLoading.value = true
  try {
    envInfo.value = await checkUniversalEnvironment()
  } catch {
    envInfo.value = []
  } finally {
    envLoading.value = false
  }
}

/** 工具栏唤起环境检测弹窗：每次打开都重新检测一次 */
function openEnvDialog() {
  envVisible.value = true
  loadEnvironment()
}

async function pickProjectDir(card: BuildCard) {
  try {
    const path = await selectFolder()
    if (path) {
      card.projectDir = path
      await updateDefaultOutputDir(card)
      selectedCardId.value = card.id
    }
  } catch {
    /* ignore */
  }
}

/** 在资源管理器中打开输出目录（目录不存在时由后端返回提示） */
async function openOutputDir(card: BuildCard) {
  const dir = card.outputDir.trim()
  if (!dir) {
    ElMessage.warning('未设置输出目录')
    return
  }
  try {
    await openOutputFolder(dir)
  } catch {
    // 失败提示由请求拦截器统一弹出
  }
}

async function pickOutputDir(card: BuildCard) {
  try {
    const path = await selectFolder()
    if (path) {
      card.outputDir = path
      card.outputDirCustom = true
      selectedCardId.value = card.id
    }
  } catch {
    /* ignore */
  }
}

async function updateDefaultOutputDir(card: BuildCard) {
  try {
    card.outputDir = await getUniversalDefaultOutputDir({ type: card.type, name: card.name })
  } catch {
    card.outputDir = ''
  }
}

async function onTypeChange(card: BuildCard) {
  // 切换类型时自动更新名称中的类型前缀
  const short = typeShortLabelMap[card.type] || card.type
  const base = stripTypeName(card.name)
  const idxMatch = /^构建任务\s*(\d+)$/.exec(base)
  card.name = idxMatch
    ? defaultCardName(card.type, parseInt(idxMatch[1]))
    : `[${short}] ${base}`
  await updateDefaultOutputDir(card)
}

async function onNameChange(card: BuildCard) {
  // 卡片名称变化时，重新生成默认输出目录（末级文件夹跟名称走）
  // 手动选择了输出目录的则不覆盖
  if (!card.outputDirCustom) {
    await updateDefaultOutputDir(card)
  }
}

/** 构建前环境检测：结果写入 preLog（与后端构建日志拼接显示；缺失不拦截，由构建本身暴露问题） */
async function logEnvironmentCheck(card: BuildCard) {
  try {
    // 后台静默检测：不弹全局 loading（启动阶段的 loading 由卡片内部 v-loading 展示），
    // 检测失败也不阻塞构建，结果异步写入日志开头
    const infos = await checkUniversalEnvironmentForType({ type: card.type }, { silent: true })
    if (infos.length === 0) return
    const lines = ['', '===== 环境检测 =====']
    for (const info of infos) {
      lines.push(info.installed ? `✓ ${info.name} ${info.version}`.trim() : `❌ ${info.name} 未安装`)
    }
    const missing = infos.filter((x) => !x.installed)
    if (missing.length > 0) {
      lines.push(`⚠ 缺少 ${missing.map((x) => x.name).join('、')}，构建可能失败`)
    }
    lines.push('==================')
    card.preLog = lines.join('\n')
  } catch {
    /* 检测接口失败不阻塞构建 */
  }
}

/** 启动构建中的卡片 id：构建启动请求期间在对应卡片内部显示局部 loading（哪个卡片触发遮哪个） */
const startingCardId = ref<string | null>(null)

async function startBuild(card: BuildCard) {
  if (!card.projectDir.trim()) {
    ElMessage.warning('请选择项目目录')
    return
  }

  card.jobId = null
  card.status = 'Running'
  card.progress = 0
  card.log = ''
  card.preLog = ''
  card.startTime = Date.now()
  card.endTime = null
  card.artifactArchivePath = ''
  card.artifactArchiveSize = null
  selectedCardId.value = card.id
  ensureTick()

  // 启动阶段（环境检测 + 入队请求）只在触发的卡片内部转 loading，不遮罩全局
  startingCardId.value = card.id
  try {
    // 构建前先做环境检测，把结果写到日志开头
    await logEnvironmentCheck(card)

    const dto = await startUniversalBuild({
      type: card.type,
      projectDir: card.projectDir.trim(),
      outputDir: card.outputDir.trim(),
      name: card.name.trim(),
      prePull: card.prePull,
      packArtifact: card.packArtifact,
    }, { silent: true })
    card.jobId = dto.id
    // 后端并发已满时任务先排队（Waiting），获得构建槽位后才转 Running
    card.status = dto.status || 'Running'
    card.outputDir = dto.outputDir
    card.log = dto.log || card.log
    startPolling()
  } catch (err: any) {
    card.status = 'Failed'
    card.log = `>> 启动失败：${err?.message || '未知错误'}`
    // 请求已 silent（不弹全局 loading），启动失败需要手动提示；卡片日志同时记录错误详情
    ElMessage.error(`任务「${card.name || '未命名'}」启动失败：${err?.message || '未知错误'}`)
  } finally {
    startingCardId.value = null
  }
}

let pollTimer: number | null = null

function startPolling() {
  if (pollTimer) return
  pollTimer = window.setInterval(async () => {
    // Waiting = 并发已满排队等待槽位，同样需要轮询直到转为 Running 或终态
    const runningCards = cards.filter((c) => (c.status === 'Running' || c.status === 'Waiting') && c.jobId)
    if (runningCards.length === 0) {
      stopPolling()
      return
    }
    for (const card of runningCards) {
      if (!card.jobId) continue
      try {
        const dto = await getUniversalBuildProgress({ id: card.jobId })
        if (dto) {
          const prevStatus = card.status
          card.status = dto.status
          card.log = dto.log
          card.outputDir = dto.outputDir
          card.progress = dto.progress
          card.queuePosition = dto.queuePosition ?? 0
          card.artifactSize = dto.artifactSize ?? null
          card.artifactArchivePath = dto.artifactArchivePath ?? ''
          card.artifactArchiveSize = dto.artifactArchiveSize ?? null
          const wasActive = prevStatus === 'Running' || prevStatus === 'Waiting'
          if (wasActive && dto.status !== 'Running' && dto.status !== 'Waiting') {
            card.endTime = Date.now()
            notifyDone(dto.status === 'Success' ? '构建成功' : `构建${statusText(dto.status)}`, card.name, dto.status === 'Success' ? 'success' : 'error')
            // 勾选了打压缩包：打包结果单独提示（成功带大小与路径，失败引导看日志）
            if (dto.status === 'Success' && card.packArtifact) {
              if (card.artifactArchiveSize != null) {
                notifyDone('压缩包已生成', `${card.name} · ${formatSize(card.artifactArchiveSize)}\n${card.artifactArchivePath}`, 'success')
              } else {
                notifyDone('压缩包打包失败', `${card.name}：详见构建日志末尾的 ⚠ 提示`, 'error')
              }
            }
            stopTickIfIdle()
            // 构建成功且卡片勾选“成功后自动部署”：用上次部署配置链式部署
            if (dto.status === 'Success' && card.autoDeploy) {
              autoDeployAfterBuild(card)
            }
          }
        }
      } catch {
        /* ignore */
      }
    }
  }, 1500)
}

function stopPolling() {
  if (pollTimer) {
    clearInterval(pollTimer)
    pollTimer = null
  }
}

/** 取消正在运行的构建任务 */
async function cancelBuild(card: BuildCard) {
  if (!card.jobId) return
  try {
    await cancelUniversalBuild({ id: card.jobId })
    ElMessage.success('已发送取消信号')
  } catch {
    /* 错误已由请求拦截器统一提示 */
  }
}

function selectCard(card: BuildCard) {
  selectedCardId.value = card.id
}

function isRunning(card: BuildCard) {
  return card.status === 'Running' || card.status === 'Waiting'
}

function statusText(status: UniversalBuildStatus | null) {
  switch (status) {
    case 'Waiting':
      return '排队中...'
    case 'Running':
      return '构建中...'
    case 'Success':
      return '构建成功'
    case 'Failed':
      return '构建失败'
    case 'Cancelled':
      return '已取消'
    case 'Pending':
      return '等待中'
    default:
      return '等待构建'
  }
}

function statusType(status: UniversalBuildStatus | null) {
  switch (status) {
    case 'Waiting':
      return 'info'
    case 'Running':
      return 'primary'
    case 'Success':
      return 'success'
    case 'Failed':
      return 'danger'
    case 'Cancelled':
      return 'warning'
    case 'Pending':
      return 'info'
    default:
      return 'info'
  }
}

/** HTML 转义（防 XSS） */
function escapeHtml(text: string): string {
  return text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
}

/** ANSI SGR 前景色码 → CSS 颜色（One Dark 风格，与日志终端背景搭配） */
const ansiColorMap: Record<string, string> = {
  '30': '#5c6370', '31': '#e06c75', '32': '#98c379', '33': '#e5c07b',
  '34': '#61afef', '35': '#c678dd', '36': '#56b6c2', '37': '#abb2bf',
  '90': '#7f848e', '91': '#ff6b6b', '92': '#7ee787', '93': '#ffd479',
  '94': '#79c0ff', '95': '#d2a8ff', '96': '#67dbec', '97': '#f0f6fc',
}

/** 把带 ANSI SGR 颜色码的一行文本还原为带 span 的 HTML；其它转义码（光标控制等）剔除 */
function ansiLineToHtml(line: string): string {
  if (!line.includes('\u001b')) return escapeHtml(line)
  let html = ''
  let color = ''
  let bold = false
  let last = 0
  // 匹配 SGR 码（\x1b[...m）与其它转义码（整体丢弃）
  const re = /\u001b\[([0-9;]*)m|\u001b\[[0-9;]*[a-zA-Z]/g
  const flush = (end: number) => {
    if (end <= last) return
    let text = escapeHtml(line.slice(last, end))
    if (color || bold) {
      const style = `${color ? `color:${color};` : ''}${bold ? 'font-weight:600;' : ''}`
      text = `<span style="${style}">${text}</span>`
    }
    html += text
  }
  let m: RegExpExecArray | null
  while ((m = re.exec(line))) {
    flush(m.index)
    last = re.lastIndex
    if (m[1] !== undefined) {
      const params = m[1].length === 0 ? ['0'] : m[1].split(';')
      for (const p of params) {
        if (p === '0') { color = ''; bold = false }
        else if (p === '1') bold = true
        else if (p === '22' || p === '39') { bold = false; color = '' }
        else if (ansiColorMap[p]) color = ansiColorMap[p]
        // 38;5;N / 38;2;R;G;B 等扩展色暂不细分，保持当前色
      }
    }
  }
  flush(line.length)
  return html
}

/** 根据日志行内容返回 CSS 类名（结构化标记优先，关键字兑底） */
function logLineClass(line: string): string {
  const t = line.trim()
  const lower = t.toLowerCase()
  if (t.startsWith('=====')) {
    if (t.includes('⛔') || t.includes('取消')) return 'log-cancel'
    if (t.includes('↩') || t.includes('还原')) return 'log-cancel'
    if (t.includes('✅') || t.includes('完成')) return 'log-success'
    if (t.includes('❌') || t.includes('失败')) return 'log-error'
    return 'log-info'
  }
  if (/^\[\d+\/\d+\]/.test(t)) return 'log-step'
  if (t.startsWith('$ ')) return 'log-cmd'
  if (t.includes('↩') || t.includes('⛔')) return 'log-cancel'
  if (/^✓/.test(t)) return 'log-success'
  if (/^❌/.test(t)) return 'log-error'
  if (t.startsWith('>>')) return 'log-info'
  if (t.startsWith('🌐')) return 'log-info'
  if (t.includes('⚠')) return 'log-warn'
  if (lower.includes('[err]') || lower.includes('error') || lower.includes('failed') || lower.includes('失败') || lower.includes('异常') || lower.includes('exception') || lower.includes('fatal'))
    return 'log-error'
  if (lower.includes('warn') || lower.includes('警告'))
    return 'log-warn'
  if (lower.includes('成功') || lower.includes('succeed') || lower.includes('built in') || lower.includes('build succeeded'))
    return 'log-success'
  return ''
}

// ============================ 日志过滤 ============================

/** 日志过滤：关键字（不区分大小写包含）+ 行类型（错误/警告/成功），可叠加 */
const logFilter = ref('')
const logFilterType = ref<'' | 'error' | 'warn' | 'success'>('')

/** 按当前过滤条件筛选日志行（无过滤时原样返回） */
function filterLogLines(lines: string[]): string[] {
  const kw = logFilter.value.trim().toLowerCase()
  const type = logFilterType.value
  if (!kw && !type) return lines
  return lines.filter((line) => {
    if (type) {
      const cls = logLineClass(line)
      if (type === 'error' && cls !== 'log-error') return false
      if (type === 'warn' && cls !== 'log-warn') return false
      if (type === 'success' && cls !== 'log-success') return false
    }
    if (kw && !line.toLowerCase().includes(kw)) return false
    return true
  })
}

/** 过滤统计：当前标签页 显示行数/总行数（有过滤条件时展示） */
const logFilterStat = computed(() => {
  const card = selectedCard.value
  if (!card) return null
  const text = activeLogTab.value === 'build' ? ((card.preLog || '') + (card.log || '')) : (card.deployLog || '')
  if (!text) return null
  const lines = text.split('\n')
  return { shown: filterLogLines(lines).length, total: lines.length }
})

/** 格式化日志为带颜色的 HTML（先按过滤条件筛行）：优先还原 ANSI 原生色，其次按结构化标记着色 */
function formatLogHtml(log: string): string {
  return filterLogLines(log.split('\n')).map(line => {
    if (line.includes('\u001b')) return ansiLineToHtml(line)
    const escaped = escapeHtml(line)
    const cls = logLineClass(line)
    return cls ? `<span class="${cls}">${escaped}</span>` : escaped
  }).join('\n')
}

async function copyPath(path: string) {
  if (!path) {
    ElMessage.warning('没有可复制的路径')
    return
  }
  try {
    await navigator.clipboard.writeText(path)
    ElMessage.success('已复制到剪贴板')
  } catch {
    const textarea = document.createElement('textarea')
    textarea.value = path
    textarea.style.position = 'fixed'
    textarea.style.opacity = '0'
    document.body.appendChild(textarea)
    textarea.select()
    try {
      document.execCommand('copy')
      ElMessage.success('已复制到剪贴板')
    } catch {
      ElMessage.error('复制失败')
    }
    document.body.removeChild(textarea)
  }
}

function openDownload(url: string) {
  window.open(url, '_blank')
}

// ============================ 部署功能 ============================

/** 部署弹窗状态 */
const deployDialogVisible = ref(false)
const deployForm = reactive({
  cardId: '',
  buildName: '',
  buildType: 'Web' as UniversalBuildType,
  outputDir: '',
  serviceName: '',
  remoteDir: '',
  archiveName: '',
  targetOS: 'Linux' as DeployTargetOS,
  siteName: 'convenient',
  host: '123.56.68.132',
  userName: 'root',
  // 密码走本机 DPAPI 加密存储：勾选“记住密码”时保存并在下次部署自动回填
  password: '',
  rememberPassword: true,
  deployPath: '/opt/convenient',
  verifyHealth: true,
  keepDatabase: true,
})
const deployLoading = ref(false)
/** 部署表单验证（el-form 自带）：必填字段显示红 *，未通过时错误信息显示在字段下方 */
const deployFormRef = ref()
const deployFormRules = {
  outputDir: [{ required: true, message: '请选择部署目录', trigger: ['blur', 'change'] }],
  siteName: [{ required: true, message: '请填写站点名称', trigger: ['blur', 'change'] }],
  host: [{ required: true, message: '请填写服务器地址', trigger: ['blur', 'change'] }],
  userName: [{ required: true, message: '请填写 SSH 用户名', trigger: ['blur', 'change'] }],
  password: [{ required: true, message: '请填写 SSH 密码', trigger: ['blur', 'change'] }],
}
/** 部署目录是否只读：从卡片打开时只读，从工具栏打开可选 */
const deployReadOnlyDir = ref(true)
/** 站点存在性检查结果 */
const siteExistsInfo = ref<SiteExistsResult | null>(null)

/** 当前日志标签：build 或 deploy */
const activeLogTab = ref<'build' | 'deploy'>('build')

/** 剥离 ANSI 转义码（复制到剪贴板时避免乱码字符，与 ansiLineToHtml 同一正则） */
function stripAnsiCodes(text: string): string {
  return text.replace(/\u001b\[[0-9;]*[a-zA-Z]/g, '')
}

/** 复制当前日志标签页的完整纯文本日志（构建/部署随标签切换） */
async function copyLog() {
  const card = selectedCard.value
  if (!card) return
  const text = activeLogTab.value === 'build' ? (card.preLog + card.log) : card.deployLog
  if (!text) {
    ElMessage.warning('暂无日志可复制')
    return
  }
  try {
    await navigator.clipboard.writeText(stripAnsiCodes(text))
    ElMessage.success(activeLogTab.value === 'build' ? '构建日志已复制' : '部署日志已复制')
  } catch {
    ElMessage.error('复制失败，请手动选择复制')
  }
}

/** 根据构建类型和目标系统预填服务名 */
function defaultServiceName(type: UniversalBuildType, targetOS: DeployTargetOS): string {
  if (targetOS === 'Windows') {
    switch (type) {
      case 'DotNet':
        return 'ConvenientSystem.Api'
      default:
        return ''
    }
  }
  switch (type) {
    case 'Web':
    case 'Node':
      return 'web'
    case 'DotNet':
      return 'api'
    default:
      return ''
  }
}

/** 根据目标系统返回默认部署路径 */
function defaultDeployPath(targetOS: DeployTargetOS, siteName: string): string {
  const site = (siteName || 'convenient').trim()
  return targetOS === 'Windows' ? `D:\\apps\\${site}` : `/opt/${site}`
}

/** 根据目标系统返回压缩包扩展名 */
function archiveExtension(targetOS: DeployTargetOS): string {
  return targetOS === 'Windows' ? '.zip' : '.tar.gz'
}

/** 检查站点是否存在 */
async function doCheckSiteExists() {
  if (!deployForm.host.trim() || !deployForm.userName.trim() || !deployForm.password) {
    siteExistsInfo.value = null
    return
  }
  try {
    const result = await checkSiteExists({
      targetOS: deployForm.targetOS,
      siteName: deployForm.siteName || 'convenient',
      serviceName: deployForm.serviceName || defaultServiceName(deployForm.buildType, deployForm.targetOS),
      host: deployForm.host.trim(),
      userName: deployForm.userName.trim(),
      password: deployForm.password,
      buildType: deployForm.buildType,
    })
    siteExistsInfo.value = result
  } catch {
    siteExistsInfo.value = null
  }
}

/** 回填本机已保存的 SSH 密码（DPAPI 解密；仅在密码为空时填充，不覆盖手动输入） */
async function fillSavedPassword() {
  if (deployForm.password || !deployForm.host.trim() || !deployForm.userName.trim()) return
  try {
    const result = await getSshCredential(deployForm.host.trim(), deployForm.userName.trim())
    if (result?.password) {
      deployForm.password = result.password
      // 回填后密码已就绪，补一次站点存在性检查
      doCheckSiteExists()
      // 程序赋值不触发验证，手动清掉密码字段的必填错误提示
      deployFormRef.value?.clearValidate('password')
    }
  } catch {
    /* 未保存过或后端不可用时静默，不影响手动输入 */
  }
}

/** 打开部署弹窗（从构建卡片进入，部署目录只读） */
function openDeployDialog(card: BuildCard) {
  deployForm.cardId = card.id
  deployForm.buildName = card.name
  deployForm.buildType = card.type
  deployForm.outputDir = card.outputDir
  deployForm.serviceName = defaultServiceName(card.type, deployForm.targetOS)
  deployForm.remoteDir = ''
  // archiveName 由 watch 联动，不需手动设值
  deployReadOnlyDir.value = true
  deployDialogVisible.value = true
  // 表单被重新填充，清掉上一次遗留的验证错误提示
  nextTick(() => deployFormRef.value?.clearValidate())
  doCheckSiteExists()
  fillSavedPassword()
}

/** 打开部署弹窗（从工具栏进入，自由选择部署目录） */
function openDeployOnlyDialog() {
  deployForm.cardId = ''
  deployForm.buildName = '独立部署'
  deployForm.buildType = 'Installer'
  deployForm.outputDir = ''
  deployForm.serviceName = ''
  deployForm.remoteDir = ''
  // archiveName 由 watch 联动，不需手动设值
  deployReadOnlyDir.value = false
  deployDialogVisible.value = true
  nextTick(() => deployFormRef.value?.clearValidate())
  doCheckSiteExists()
  fillSavedPassword()
}

/** 模版填充中标志：填充期间跳过压缩包名/目标系统联动 watch，模版保存的值原样生效 */
let applyingTemplate = false

/** 压缩包名联动：站点名、服务名或目标系统变化时自动生成 {站点名}-{服务名}.{扩展名} */
watch(
  () => [deployForm.siteName, deployForm.serviceName, deployForm.targetOS],
  () => {
    if (applyingTemplate) return
    const site = (deployForm.siteName || 'convenient').trim()
    const svc = (deployForm.serviceName || defaultServiceName(deployForm.buildType, deployForm.targetOS)).trim()
    const ext = archiveExtension(deployForm.targetOS)
    deployForm.archiveName = `${site}-${svc}${ext}`
  },
  { immediate: true },
)

/** 目标系统切换联动：部署路径、服务名、压缩包扩展名 */
watch(
  () => deployForm.targetOS,
  (newOS, oldOS) => {
    if (newOS === oldOS || applyingTemplate) return
    deployForm.deployPath = defaultDeployPath(newOS, deployForm.siteName)
    deployForm.serviceName = defaultServiceName(deployForm.buildType, newOS)
  },
)

/** 站点信息变化时重新检查站点是否存在 */
let siteCheckTimer: number | null = null
watch(
  () => [deployForm.siteName, deployForm.serviceName, deployForm.host, deployForm.userName],
  () => {
    if (siteCheckTimer) window.clearTimeout(siteCheckTimer)
    siteCheckTimer = window.setTimeout(() => {
      if (deployDialogVisible.value) {
        doCheckSiteExists()
        fillSavedPassword()
      }
    }, 500)
  },
)

/** 选择部署产物目录 */
async function pickDeployOutputDir() {
  try {
    const path = await selectFolder()
    if (path) {
      deployForm.outputDir = path
    }
  } catch {
    /* ignore */
  }
}

/** 在资源管理器中打开部署产物目录（目录不存在时由后端返回提示） */
async function openDeployOutputDir() {
  const dir = deployForm.outputDir.trim()
  if (!dir) {
    ElMessage.warning('未设置部署产物目录')
    return
  }
  try {
    await openOutputFolder(dir)
  } catch {
    // 失败提示由请求拦截器统一弹出
  }
}

/** 启动部署 */
async function startDeployAction() {
  // el-form 自带验证：必填项未填时红字提示在字段下方，全部通过才继续
  if (!deployFormRef.value) return
  const valid = await deployFormRef.value.validate().catch(() => false)
  if (!valid) return

  // 找关联卡片，没有则创建临时卡片跟踪部署状态
  let card = cards.find(c => c.id === deployForm.cardId)
  if (!card) {
    card = reactive(createCard())
    card.name = deployForm.buildName || '独立部署'
    card.type = deployForm.buildType
    card.outputDir = deployForm.outputDir
    cards.push(card)
    selectedCardId.value = card.id
  }

  // 记录部署目标与计时（耗时显示、成功后“打开站点”用）
  card.deployStartTime = Date.now()
  card.deployEndTime = null
  card.deployProgress = 0
  card.deployStepText = ''
  card.deployDisplayProgress = 0
  card.deployHost = deployForm.host.trim()
  card.deployServiceName = deployForm.serviceName.trim()
  ensureTick()
  persistDeployRemember()

  deployLoading.value = true
  try {
    const dto = await startDeploy({
      outputDir: deployForm.outputDir,
      buildName: deployForm.buildName,
      buildType: deployForm.buildType,
      serviceName: deployForm.serviceName.trim(),
      remoteDir: deployForm.remoteDir.trim(),
      archiveName: deployForm.archiveName.trim(),
      targetOS: deployForm.targetOS,
      siteName: deployForm.siteName || 'convenient',
      host: deployForm.host.trim(),
      userName: deployForm.userName.trim(),
      password: deployForm.password,
      deployPath: deployForm.deployPath.trim(),
      verifyHealth: deployForm.verifyHealth,
      keepDatabase: deployForm.keepDatabase,
    })
    card.deployJobId = dto.id
    card.deployStatus = dto.status
    card.deployProgress = dto.progress ?? 0
    if (dto.stepTitle) card.deployStepText = deployStepTextOf(dto)
    card.deployLog = dto.log || ''
    deployDialogVisible.value = false
    activeLogTab.value = 'deploy'
    selectedCardId.value = card.id
    startDeployPolling()
    ElMessage.success('部署任务已启动')
    // 记录部署配置快照（构建成功后自动部署复用；不含密码）
    card.lastDeployConfig = {
      targetOS: deployForm.targetOS,
      siteName: deployForm.siteName || 'convenient',
      serviceName: deployForm.serviceName.trim(),
      remoteDir: deployForm.remoteDir.trim(),
      archiveName: deployForm.archiveName.trim(),
      deployPath: deployForm.deployPath.trim(),
      verifyHealth: deployForm.verifyHealth,
      keepDatabase: deployForm.keepDatabase,
      host: deployForm.host.trim(),
      userName: deployForm.userName.trim(),
    }
    // 勾选“记住密码”时以 DPAPI 加密保存到本机（保存失败不影响部署）
    if (deployForm.rememberPassword && deployForm.password) {
      saveSshCredential({
        host: deployForm.host.trim(),
        userName: deployForm.userName.trim(),
        password: deployForm.password,
      }).catch(() => { /* ignore */ })
    }
  } catch (err: any) {
    card.deployEndTime = Date.now()
    stopTickIfIdle()
    ElMessage.error(`启动部署失败：${err?.message || '未知错误'}`)
  } finally {
    deployLoading.value = false
  }
}

/** 构建成功后自动部署：优先用卡片绑定的部署模版，未绑定时回退上次部署配置；密码从本机 DPAPI 凭据读取 */
async function autoDeployAfterBuild(card: BuildCard) {
  const tpl = deployTemplates.value.find((t) => t.id === card.deployTemplateId)
  const cfg = tpl
    ? {
        targetOS: tpl.targetOS,
        siteName: tpl.siteName,
        serviceName: tpl.serviceName,
        remoteDir: tpl.remoteDir,
        archiveName: tpl.archiveName,
        deployPath: tpl.deployPath,
        verifyHealth: tpl.verifyHealth,
        keepDatabase: tpl.keepDatabase,
        host: tpl.host,
        userName: tpl.userName,
      }
    : card.lastDeployConfig
  if (!cfg) {
    ElMessage.warning(`【${card.name}】未选择部署模版且未记录部署配置，自动部署已跳过（先手动部署一次或在卡片上选择模版）`)
    return
  }
  let password = ''
  try {
    const cred = await getSshCredential(cfg.host, cfg.userName)
    password = cred?.password || ''
  } catch {
    /* 取不到密码则跳过自动部署 */
  }
  if (!password) {
    ElMessage.warning(`【${card.name}】本机未保存 SSH 密码，自动部署已跳过（部署时勾选“记住密码”）`)
    return
  }
  card.deployJobId = null
  card.deployStatus = 'Running'
  card.deployLog = '>> 构建成功，自动部署开始...\n'
  card.deployStartTime = Date.now()
  card.deployEndTime = null
  card.deployProgress = 0
  card.deployStepText = ''
  card.deployDisplayProgress = 0
  card.deployHost = cfg.host
  card.deployServiceName = cfg.serviceName
  activeLogTab.value = 'deploy'
  selectedCardId.value = card.id
  ensureTick()
  try {
    const dto = await startDeploy({
      outputDir: card.outputDir,
      buildName: card.name,
      buildType: card.type,
      serviceName: cfg.serviceName,
      remoteDir: cfg.remoteDir,
      archiveName: cfg.archiveName,
      targetOS: cfg.targetOS,
      siteName: cfg.siteName,
      host: cfg.host,
      userName: cfg.userName,
      password,
      deployPath: cfg.deployPath,
      verifyHealth: cfg.verifyHealth,
      keepDatabase: cfg.keepDatabase,
    })
    card.deployJobId = dto.id
    card.deployStatus = dto.status
    card.deployProgress = dto.progress ?? 0
    if (dto.stepTitle) card.deployStepText = deployStepTextOf(dto)
    card.deployLog = dto.log || card.deployLog
    startDeployPolling()
    ElMessage.success(`【${card.name}】已自动开始部署`)
  } catch (err: any) {
    card.deployStatus = 'Failed'
    card.deployEndTime = Date.now()
    card.deployLog += `>> 自动部署启动失败：${err?.message || '未知错误'}`
    stopTickIfIdle()
    notifyDone('自动部署失败', card.name, 'error')
  }
}

/** 部署轮询 */
let deployPollTimer: number | null = null

function startDeployPolling() {
  if (deployPollTimer) return
  deployPollTimer = window.setInterval(async () => {
    const runningCards = cards.filter(c => c.deployStatus === 'Running' && c.deployJobId)
    if (runningCards.length === 0) {
      stopDeployPolling()
      return
    }
    for (const card of runningCards) {
      if (!card.deployJobId) continue
      try {
        const dto = await getDeployProgress({ id: card.deployJobId })
        if (dto) {
          const prevStatus = card.deployStatus
          card.deployStatus = dto.status
          card.deployProgress = dto.progress ?? card.deployProgress
          if (dto.stepTitle) card.deployStepText = deployStepTextOf(dto)
          card.deployLog = dto.log
          if (prevStatus === 'Running' && dto.status !== 'Running') {
            card.deployEndTime = Date.now()
            notifyDone(dto.status === 'Success' ? '部署成功' : `部署${deployStatusText(dto.status)}`, card.name, dto.status === 'Success' ? 'success' : 'error')
            stopTickIfIdle()
          }
        }
      } catch {
        /* ignore */
      }
    }
  }, 1500)
}

function stopDeployPolling() {
  if (deployPollTimer) {
    clearInterval(deployPollTimer)
    deployPollTimer = null
  }
}

/** 取消正在运行的部署任务（后端自动还原服务器到部署前状态） */
async function cancelDeployAction(card: BuildCard) {
  if (!card.deployJobId) return
  try {
    await ElMessageBox.confirm(
      '取消部署将中断当前操作并自动还原服务器到部署前状态，确定取消吗？',
      '确认取消部署',
      { confirmButtonText: '取消部署', cancelButtonText: '继续部署', type: 'warning', confirmButtonClass: 'el-button--danger' },
    )
  } catch {
    return
  }
  try {
    const result = await cancelDeploy({ id: card.deployJobId })
    if (result?.message) ElMessage.info(result.message)
  } catch {
    /* 错误已由请求拦截器统一提示（如临界区拒绝取消） */
  }
}

/** 部署状态文本 */
function deployStatusText(status: DeployStatus | null) {
  switch (status) {
    case 'Running': return '部署中...'
    case 'Success': return '部署成功'
    case 'Failed': return '部署失败'
    case 'Cancelled': return '已取消'
    default: return '未部署'
  }
}

/** 部署状态标签类型（显式标注联合字面量，供模板 el-tag type 属性直接使用） */
function deployStatusType(status: DeployStatus | null): 'success' | 'warning' | 'danger' | 'info' {
  switch (status) {
    case 'Running': return 'warning'
    case 'Success': return 'success'
    case 'Failed': return 'danger'
    case 'Cancelled': return 'info'
    default: return 'info'
  }
}

/** 部署成功后打开站点首页 */
function openSite(card: BuildCard) {
  if (!card.deployHost) {
    ElMessage.warning('未记录部署目标地址')
    return
  }
  window.open(`http://${card.deployHost}`, '_blank')
}

/** 一键构建全部已配置项目目录的卡片（并发由后端信号量控制） */
async function buildAll() {
  const ready = cards.filter((c) => c.projectDir.trim() && c.status !== 'Running' && c.status !== 'Waiting')
  if (ready.length === 0) {
    ElMessage.warning('没有可构建的任务（卡片需先选择项目目录）')
    return
  }
  const autoDeployCount = ready.filter((c) => c.autoDeploy).length
  const deployHint = autoDeployCount > 0 ? `\n其中 ${autoDeployCount} 个勾选了自动部署，构建成功后将自动部署到服务器。` : ''
  try {
    await ElMessageBox.confirm(
      `将构建 ${ready.length} 个任务：${ready.map((c) => c.name).join('、')}。${deployHint}`,
      '全部构建',
      { confirmButtonText: '开始', cancelButtonText: '取消', type: 'info' },
    )
  } catch {
    return
  }
  for (const card of ready) {
    await startBuild(card)
  }
}

// ============================ 卡片与部署配置持久化 ============================

const CARDS_STORAGE_KEY = 'universal-build-cards-v1'
const DEPLOY_REMEMBER_KEY = 'universal-deploy-remember-v1'

/** 保存卡片配置（exe 目录 ui-state.json，清缓存/重装不丢；运行状态/日志/任务号不持久化，重开页面后从头开始） */
function persistCards() {
  const data = cards.map((c) => ({
    id: c.id,
    name: c.name,
    type: c.type,
    projectDir: c.projectDir,
    outputDir: c.outputDir,
    outputDirCustom: c.outputDirCustom,
    prePull: c.prePull,
    packArtifact: c.packArtifact,
    autoDeploy: c.autoDeploy,
    deployTemplateId: c.deployTemplateId,
  }))
  saveUiStateString(CARDS_STORAGE_KEY, JSON.stringify(data))
}

/** 恢复上次的卡片配置（仅配置项，运行态清零）；返回是否有存档 */
async function restoreCards(): Promise<boolean> {
  try {
    const raw = await loadUiStateString(CARDS_STORAGE_KEY)
    if (!raw) return false
    const data = JSON.parse(raw) as Partial<BuildCard>[]
    if (!Array.isArray(data) || data.length === 0) return false
    for (const item of data) {
      const card = createCard()
      if (item.id) card.id = item.id // 保留 id：定时构建按 cardId 关联
      card.type = item.type || 'Web'
      // 恢复名称：默认格式自动补类型前缀，自定义名称前加 [Type]
      if (item.name) {
        const base = stripTypeName(item.name)
        const idxMatch = /^构建任务\s*(\d+)$/.exec(base)
        if (idxMatch) {
          card.name = defaultCardName(card.type, parseInt(idxMatch[1]))
        } else {
          const short = typeShortLabelMap[card.type] || card.type
          card.name = `[${short}] ${base}`
        }
      }
      card.projectDir = item.projectDir || ''
      card.outputDir = item.outputDir || ''
      card.outputDirCustom = !!item.outputDirCustom
      card.prePull = !!item.prePull
      card.packArtifact = !!item.packArtifact
      card.autoDeploy = !!item.autoDeploy
      card.deployTemplateId = item.deployTemplateId || ''
      cards.push(card)
    }
    selectedCardId.value = cards[0].id
    return true
  } catch {
    return false
  }
}

// 配置字段变化时延迟保存（避免输入过程中高频写存储；日志/状态变化不触发）
let persistTimer: number | null = null
watch(
  () => cards.map((c) => `${c.name}|${c.type}|${c.projectDir}|${c.outputDir}|${c.outputDirCustom}|${c.prePull}|${c.packArtifact}|${c.autoDeploy}|${c.deployTemplateId}`).join('\n'),
  () => {
    if (persistTimer) window.clearTimeout(persistTimer)
    persistTimer = window.setTimeout(persistCards, 500)
  },
)

/** 记住部署连接信息（只记非敏感项：主机/用户名/站点/目标系统，密码永不持久化） */
function persistDeployRemember() {
  saveUiStateString(DEPLOY_REMEMBER_KEY, JSON.stringify({
    host: deployForm.host,
    userName: deployForm.userName,
    siteName: deployForm.siteName,
    targetOS: deployForm.targetOS,
  }))
}

async function restoreDeployRemember() {
  try {
    const raw = await loadUiStateString(DEPLOY_REMEMBER_KEY)
    if (!raw) return
    const d = JSON.parse(raw)
    if (d.host) deployForm.host = d.host
    if (d.userName) deployForm.userName = d.userName
    if (d.siteName) deployForm.siteName = d.siteName
    if (d.targetOS === 'Linux' || d.targetOS === 'Windows') deployForm.targetOS = d.targetOS
  } catch { /* ignore */ }
}

// ============================ 配置导出 / 导入 ============================

/** 导出全部配置（卡片 + 构建模板 + 部署模板，不含密码）为 JSON 文件 */
function exportConfig() {
  const data = {
    version: 1,
    exportedAt: new Date().toISOString(),
    cards: cards.map((c) => ({
      name: c.name,
      type: c.type,
      projectDir: c.projectDir,
      outputDir: c.outputDir,
      outputDirCustom: c.outputDirCustom,
      prePull: c.prePull,
      packArtifact: c.packArtifact,
      autoDeploy: c.autoDeploy,
    })),
    templates: templates.value,
    deployTemplates: deployTemplates.value,
  }
  const blob = new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  const now = new Date()
  const p = (n: number) => String(n).padStart(2, '0')
  a.href = url
  a.download = `build-config-${now.getFullYear()}${p(now.getMonth() + 1)}${p(now.getDate())}.json`
  a.click()
  URL.revokeObjectURL(url)
  ElMessage.success(`已导出 ${data.cards.length} 张卡片、${templates.value.length} 个构建模板、${deployTemplates.value.length} 个部署模板`)
}

/** 隐藏的文件选择框（导入配置用） */
const importFileInput = ref<HTMLInputElement | null>(null)

function triggerImport() {
  importFileInput.value?.click()
}

/** 导入配置文件：卡片追加（重新生成 id 避免冲突），模板按 id 去重合并 */
async function importConfig(e: Event) {
  const input = e.target as HTMLInputElement
  const file = input.files?.[0]
  input.value = '' // 清空以允许重复选择同一文件
  if (!file) return
  try {
    const text = await file.text()
    const data = JSON.parse(text)
    if (typeof data !== 'object' || data === null) throw new Error('文件格式不正确')

    let cardCount = 0
    if (Array.isArray(data.cards)) {
      for (const item of data.cards) {
        if (!item || typeof item !== 'object') continue
        const card = createCard() // 新 id：不与现有卡片/其他机器冲突
        card.type = item.type || 'Web'
        if (item.name) card.name = String(item.name)
        card.projectDir = item.projectDir || ''
        card.outputDir = item.outputDir || ''
        card.outputDirCustom = !!item.outputDirCustom
        card.prePull = !!item.prePull
        card.packArtifact = !!item.packArtifact
        card.autoDeploy = !!item.autoDeploy
        cards.push(card)
        cardCount++
      }
    }

    let tplCount = 0
    if (Array.isArray(data.templates)) {
      for (const t of data.templates) {
        if (!t?.id || templates.value.some((x) => x.id === t.id)) continue
        templates.value.push(t)
        tplCount++
      }
      saveTemplates()
    }

    let deployTplCount = 0
    if (Array.isArray(data.deployTemplates)) {
      for (const t of data.deployTemplates) {
        if (!t?.id || deployTemplates.value.some((x) => x.id === t.id)) continue
        deployTemplates.value.push(t)
        deployTplCount++
      }
      saveDeployTemplates()
    }

    ElMessage.success(`导入完成：${cardCount} 张卡片、${tplCount} 个构建模板、${deployTplCount} 个部署模板`)
  } catch (err: any) {
    ElMessage.error(`导入失败：${err?.message || '文件格式不正确'}`)
  }
}

// ============================ 部署历史 ============================

const historyVisible = ref(false)
const historyItems = ref<DeployHistoryItem[]>([])

/** 打开部署历史抽屉并拉取最近记录 */
async function openHistory() {
  historyVisible.value = true
  try {
    historyItems.value = await getDeployHistory()
  } catch {
    historyItems.value = []
  }
}

/**
 * 启动手动回滚（部署历史行入口）：把 .old 备份换回正式目录。
 * 密码从本机 DPAPI 读取；确认框说明可再次回滚回到当前版本。
 */
async function rollbackFromHistory(row: DeployHistoryItem) {
  let password = ''
  try {
    const result = await getSshCredential(row.host, 'root')
    password = result?.password || ''
  } catch { /* ignore */ }
  if (!password) {
    // root 之外的用户名历史里没存，尝试再取一次 userName 需要用户提供；直接提示
    ElMessage.warning('本机未保存该服务器密码，无法自动回滚（可在部署弹窗保存密码后重试）')
    return
  }
  try {
    await ElMessageBox.confirm(
      `将把【${row.siteName}】回滚到上一版本（当前版本转为备份，可再次回滚回来）。站点 ${row.host}，确定继续吗？`,
      '回滚到上一版本',
      { confirmButtonText: '开始回滚', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }
  // 建临时卡片跟踪回滚任务状态（复用部署日志面板与轮询）
  const card = reactive(createCard())
  card.name = `[回滚] ${row.buildName}`
  card.type = row.buildType
  card.deployJobId = null
  card.deployStatus = 'Running'
  card.deployLog = ''
  card.deployProgress = 0
  card.deployStepText = ''
  card.deployDisplayProgress = 0
  card.deployStartTime = Date.now()
  card.deployEndTime = null
  card.deployHost = row.host
  card.deployServiceName = ''
  cards.push(card)
  selectedCardId.value = card.id
  activeLogTab.value = 'deploy'
  ensureTick()
  try {
    const dto = await startRollback({
      buildName: row.buildName,
      buildType: row.buildType,
      targetOS: row.targetOS,
      siteName: row.siteName,
      host: row.host,
      userName: 'root',
      password,
    })
    card.deployJobId = dto.id
    card.deployStatus = dto.status
    card.deployProgress = dto.progress ?? 0
    if (dto.stepTitle) card.deployStepText = deployStepTextOf(dto)
    card.deployLog = dto.log || ''
    startDeployPolling()
    ElMessage.success('回滚任务已启动')
  } catch (err: any) {
    card.deployStatus = 'Failed'
    card.deployEndTime = Date.now()
    card.deployLog += `>> 回滚启动失败：${err?.message || '未知错误'}`
    stopTickIfIdle()
    notifyDone('回滚失败', row.buildName, 'error')
  }
}

/**
 * 卡片回滚：用卡片上次部署配置（lastDeployConfig）回滚到上一版本。
 */
async function rollbackCard(card: BuildCard) {
  const cfg = card.lastDeployConfig
  if (!cfg) {
    ElMessage.warning(`【${card.name}】未记录部署配置，无法回滚（请先部署一次）`)
    return
  }
  let password = ''
  try {
    const result = await getSshCredential(cfg.host, cfg.userName)
    password = result?.password || ''
  } catch { /* ignore */ }
  if (!password) {
    ElMessage.warning(`【${card.name}】本机未保存 SSH 密码，无法自动回滚（部署时勾选“记住密码”）`)
    return
  }
  try {
    await ElMessageBox.confirm(
      `将把【${card.name}】回滚到上一版本（当前版本转为备份，可再次回滚回来），确定继续吗？`,
      '回滚到上一版本',
      { confirmButtonText: '开始回滚', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }
  card.deployJobId = null
  card.deployStatus = 'Running'
  card.deployLog = '>> 手动回滚开始...\n'
  card.deployStartTime = Date.now()
  card.deployEndTime = null
  card.deployProgress = 0
  card.deployStepText = ''
  card.deployDisplayProgress = 0
  activeLogTab.value = 'deploy'
  selectedCardId.value = card.id
  ensureTick()
  try {
    const dto = await startRollback({
      buildName: card.name,
      buildType: card.type,
      targetOS: cfg.targetOS,
      siteName: cfg.siteName,
      host: cfg.host,
      userName: cfg.userName,
      password,
      deployPath: cfg.deployPath,
      serviceName: cfg.serviceName,
      remoteDir: cfg.remoteDir,
      verifyHealth: cfg.verifyHealth,
    })
    card.deployJobId = dto.id
    card.deployStatus = dto.status
    card.deployProgress = dto.progress ?? 0
    if (dto.stepTitle) card.deployStepText = deployStepTextOf(dto)
    card.deployLog = dto.log || card.deployLog
    startDeployPolling()
    ElMessage.success(`【${card.name}】回滚任务已启动`)
  } catch (err: any) {
    card.deployStatus = 'Failed'
    card.deployEndTime = Date.now()
    card.deployLog += `>> 回滚启动失败：${err?.message || '未知错误'}`
    stopTickIfIdle()
    notifyDone('回滚失败', card.name, 'error')
  }
}

// ============================ 部署历史日志查看 ============================

const historyLogVisible = ref(false)
const historyLogLoading = ref(false)
const historyLog = ref<DeployLogResult | null>(null)

/** 查看部署历史行的完整日志（内存任务；程序重启后旧条目不可查） */
async function showHistoryLog(row: DeployHistoryItem) {
  if (!row.jobId) {
    ElMessage.warning('该记录没有关联任务日志（程序重启后旧日志会被清除）')
    return
  }
  historyLogVisible.value = true
  historyLogLoading.value = true
  historyLog.value = null
  try {
    historyLog.value = await getDeployLog(row.jobId)
  } catch {
    historyLog.value = null
    // 错误提示由请求拦截器统一弹出
  } finally {
    historyLogLoading.value = false
  }
}

/** 历史日志格式化：只做着色不做过滤（日志过滤条件只作用于选中卡片的实时日志） */
function formatHistoryLogHtml(log: string): string {
  return log.split('\n').map(line => {
    if (line.includes('\u001b')) return ansiLineToHtml(line)
    const escaped = escapeHtml(line)
    const cls = logLineClass(line)
    return cls ? `<span class="${cls}">${escaped}</span>` : escaped
  }).join('\n')
}

/** 历史日志弹层状态（后端返回字符串状态，映射中文与标签色） */
const historyLogStatusMeta = computed(() => {
  const s = (historyLog.value?.status as DeployStatus | undefined) ?? null
  return { text: deployStatusText(s), type: deployStatusType(s) }
})

// ============================ 产物占用清理 ============================

/** 产物清理弹窗行：卡片名 + 目录占用统计 */
interface ArtifactRow {
  cardName: string
  path: string
  exists: boolean
  sizeBytes: number
  fileCount: number
  lastWriteTime?: string | null
  selected: boolean
}

const artifactVisible = ref(false)
const artifactLoading = ref(false)
const artifactCleaning = ref(false)
const artifactRows = ref<ArtifactRow[]>([])

/** 收集去重后的卡片产物目录列表 */
function collectArtifactDirs(): string[] {
  return Array.from(new Set(cards.map(c => c.outputDir.trim()).filter(Boolean)))
}

/** 打开产物清理弹窗：统计各卡片产物目录占用 */
function openArtifactDialog() {
  if (collectArtifactDirs().length === 0) {
    ElMessage.warning('当前没有配置输出目录的构建卡片')
    return
  }
  artifactVisible.value = true
  void refreshArtifactUsage()
}

/** 拉取产物目录占用统计（一个目录可能被多张卡片引用，按卡片行展示） */
async function refreshArtifactUsage() {
  const dirs = collectArtifactDirs()
  if (dirs.length === 0) {
    artifactRows.value = []
    return
  }
  artifactLoading.value = true
  try {
    const items = await getArtifactUsage(dirs)
    const rows: ArtifactRow[] = []
    for (const card of cards) {
      const dir = card.outputDir.trim()
      if (!dir) continue
      const item = items.find(i => i.path.toLowerCase() === dir.toLowerCase())
      if (item) {
        rows.push({
          cardName: card.name,
          path: item.path,
          exists: item.exists,
          sizeBytes: item.sizeBytes,
          fileCount: item.fileCount,
          lastWriteTime: item.lastWriteTime,
          selected: false,
        })
      }
    }
    artifactRows.value = rows
  } catch {
    artifactRows.value = []
  } finally {
    artifactLoading.value = false
  }
}

/** 清理所选产物：二次强提示后逐个清空目录内容（保留目录本身） */
async function cleanSelectedArtifacts() {
  const selected = artifactRows.value.filter(r => r.selected && r.exists)
  if (selected.length === 0) {
    ElMessage.warning('请先勾选要清理的产物目录')
    return
  }
  const totalBytes = selected.reduce((s, r) => s + r.sizeBytes, 0)
  try {
    await ElMessageBox.confirm(
      `将清空 ${selected.length} 个产物目录的内容（约 ${formatBytes(totalBytes)}），删除后不可恢复！目录本身保留，下次构建重新生成，确定继续吗？`,
      '清理产物（不可恢复）',
      { confirmButtonText: '确认清理', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }
  artifactCleaning.value = true
  let okCount = 0
  for (const row of selected) {
    try {
      await cleanArtifact(row.path)
      okCount++
    } catch {
      /* 单个失败继续处理其余，错误提示由请求拦截器弹出 */
    }
  }
  artifactCleaning.value = false
  if (okCount > 0) ElMessage.success(`已清理 ${okCount} 个产物目录`)
  await refreshArtifactUsage()
}

/** 字节数格式化（产物占用展示） */
function formatBytes(bytes: number): string {
  if (bytes <= 0) return '0 B'
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  if (bytes < 1024 * 1024 * 1024) return `${(bytes / 1024 / 1024).toFixed(1)} MB`
  return `${(bytes / 1024 / 1024 / 1024).toFixed(2)} GB`
}

/** 时间格式化：MM-dd HH:mm（历史列表/定时提示用） */
function formatHistoryTime(iso: string): string {
  const d = new Date(iso)
  if (isNaN(d.getTime())) return iso
  const p = (n: number) => String(n).padStart(2, '0')
  return `${p(d.getMonth() + 1)}-${p(d.getDate())} ${p(d.getHours())}:${p(d.getMinutes())}`
}

// ============================ 模板库 ============================

interface BuildTemplate {
  id: string
  name: string
  type: UniversalBuildType
  projectDir: string
  outputDir: string
}

const TEMPLATES_KEY = 'universal-build-templates-v1'
const templates = ref<BuildTemplate[]>([])
const templateVisible = ref(false)
/** 模板库抽屉当前页签：build = 构建模板，deploy = 部署模板 */
const templateTab = ref('build')

/** 统计部署模板被多少张构建卡片引用（模板库展示用） */
function countTemplateUsage(id: string) {
  return cards.filter((c) => c.deployTemplateId === id).length
}

async function loadTemplates() {
  const raw = await loadUiStateString(TEMPLATES_KEY)
  try {
    templates.value = JSON.parse(raw || '[]')
  } catch {
    templates.value = []
  }
}

function saveTemplates() {
  saveUiStateString(TEMPLATES_KEY, JSON.stringify(templates.value))
}

/** 把卡片当前配置存为模板 */
function saveCardAsTemplate(card: BuildCard) {
  if (!card.projectDir.trim()) {
    ElMessage.warning('请先选择项目目录再存为模板')
    return
  }
  templates.value.push({
    id: crypto.randomUUID(),
    name: card.name,
    type: card.type,
    projectDir: card.projectDir,
    outputDir: card.outputDir,
  })
  saveTemplates()
  ElMessage.success('已保存为模板')
}

/** 从模板新建卡片（自动套用模板配置） */
function addCardFromTemplate(tpl: BuildTemplate) {
  const card = reactive(createCard())
  card.type = tpl.type
  card.name = `[${typeShortLabelMap[tpl.type] || tpl.type}] ${tpl.name}`
  card.projectDir = tpl.projectDir
  card.outputDir = tpl.outputDir
  card.outputDirCustom = !!tpl.outputDir
  cards.push(card)
  selectedCardId.value = card.id
  templateVisible.value = false
}

function removeTemplate(id: string) {
  templates.value = templates.value.filter((t) => t.id !== id)
  saveTemplates()
}

// ============================ 部署模版 ============================

const DEPLOY_TEMPLATES_KEY = 'universal-deploy-templates-v1'
const deployTemplates = ref<DeployTemplate[]>([])
/** 部署弹窗当前选中的模版（空 = 手动填写） */
const selectedDeployTemplateId = ref('')

async function loadDeployTemplates() {
  const raw = await loadUiStateString(DEPLOY_TEMPLATES_KEY)
  try {
    deployTemplates.value = JSON.parse(raw || '[]')
  } catch {
    deployTemplates.value = []
  }
}

function saveDeployTemplates() {
  saveUiStateString(DEPLOY_TEMPLATES_KEY, JSON.stringify(deployTemplates.value))
}

/** 选中模版：填充全部表单字段（填充不锁定，仍可修改），并回填本机保存的密码 */
async function onDeployTemplateChange(id: string) {
  if (!id) return // 清空选择 = 取消关联，不清空已填表单
  const tpl = deployTemplates.value.find((t) => t.id === id)
  if (!tpl) return
  applyingTemplate = true
  deployForm.targetOS = tpl.targetOS
  deployForm.siteName = tpl.siteName
  deployForm.serviceName = tpl.serviceName
  deployForm.remoteDir = tpl.remoteDir
  deployForm.archiveName = tpl.archiveName
  deployForm.deployPath = tpl.deployPath
  deployForm.verifyHealth = tpl.verifyHealth
  deployForm.keepDatabase = tpl.keepDatabase
  deployForm.host = tpl.host
  deployForm.userName = tpl.userName
  fillSavedPassword()
  // 联动 watch 在微任务中触发，nextTick 后再解除标志
  await nextTick()
  applyingTemplate = false
  // 模版已填齐必填项，清掉残留的验证错误提示
  deployFormRef.value?.clearValidate()
}

/** 把当前表单配置存为部署模版（不含密码） */
async function saveDeployTemplate() {
  if (!deployForm.host.trim()) {
    ElMessage.warning('请先填写服务器地址再存为模版')
    return
  }
  let name = ''
  try {
    const { value } = await ElMessageBox.prompt('输入部署模版名称', '存为部署模版', {
      confirmButtonText: '保存',
      cancelButtonText: '取消',
      inputValue: `${deployForm.targetOS === 'Linux' ? 'Linux' : 'Win'}-${deployForm.siteName.trim() || 'convenient'}`,
      inputPattern: /\S+/,
      inputErrorMessage: '名称不能为空',
    })
    name = value.trim()
  } catch {
    return // 用户取消
  }
  const tpl: DeployTemplate = {
    id: crypto.randomUUID(),
    name,
    targetOS: deployForm.targetOS,
    siteName: deployForm.siteName,
    serviceName: deployForm.serviceName.trim(),
    remoteDir: deployForm.remoteDir.trim(),
    archiveName: deployForm.archiveName.trim(),
    deployPath: deployForm.deployPath.trim(),
    verifyHealth: deployForm.verifyHealth,
    keepDatabase: deployForm.keepDatabase,
    host: deployForm.host.trim(),
    userName: deployForm.userName.trim(),
  }
  deployTemplates.value.push(tpl)
  saveDeployTemplates()
  selectedDeployTemplateId.value = tpl.id
  ElMessage.success(`部署模版「${name}」已保存`)
}

/** 删除选中的部署模版（引用它的卡片同步解绑，自动部署回退“上次部署配置”） */
async function removeDeployTemplateById(id: string) {
  const tpl = deployTemplates.value.find((t) => t.id === id)
  if (!tpl) return
  try {
    await ElMessageBox.confirm(
      `确定删除部署模版「${tpl.name}」吗？引用它的卡片将回退为“用上次部署配置”`,
      '删除模版',
      { confirmButtonText: '删除', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }
  deployTemplates.value = deployTemplates.value.filter((t) => t.id !== tpl.id)
  saveDeployTemplates()
  selectedDeployTemplateId.value = ''
  for (const c of cards) {
    if (c.deployTemplateId === tpl.id) c.deployTemplateId = ''
  }
  ElMessage.success('模版已删除')
}

// ============================ 模板详情 / 编辑 ============================

/** 模板详情弹窗（构建/部署模板共用：打开时预计算展示行，空值给占位说明并置灰） */
const templateDetailVisible = ref(false)
const templateDetailTitle = ref('')
const templateDetailRows = ref<{ label: string; value: string; isEmpty?: boolean }[]>([])

/** 打开模板详情：构建模板与部署模板字段不同，按类型拼展示行 */
function showTemplateDetail(tpl: BuildTemplate | DeployTemplate, kind: 'build' | 'deploy') {
  if (kind === 'build') {
    const t = tpl as BuildTemplate
    templateDetailTitle.value = `构建模板详情 - ${t.name}`
    templateDetailRows.value = [
      { label: '名称', value: t.name },
      { label: '构建类型', value: typeLabelMap[t.type] || t.type },
      { label: '项目目录', value: t.projectDir || '未填写', isEmpty: !t.projectDir.trim() },
      { label: '输出目录', value: t.outputDir || '默认输出路径', isEmpty: !t.outputDir.trim() },
    ]
  } else {
    const t = tpl as DeployTemplate
    templateDetailTitle.value = `部署模板详情 - ${t.name}`
    templateDetailRows.value = [
      { label: '名称', value: t.name },
      { label: '目标系统', value: t.targetOS === 'Linux' ? 'Linux (Docker)' : 'Windows (IIS/服务)' },
      { label: '站点名称', value: t.siteName || '未填写', isEmpty: !t.siteName.trim() },
      { label: '服务名', value: t.serviceName || '自动推断（按构建类型）', isEmpty: !t.serviceName.trim() },
      { label: '远程目录', value: t.remoteDir || '自动推断（按构建类型）', isEmpty: !t.remoteDir.trim() },
      { label: '压缩包名', value: t.archiveName || '自动生成 {站点名}-{服务名}.压缩包', isEmpty: !t.archiveName.trim() },
      { label: '部署路径', value: t.deployPath || '默认（Linux /opt/{站点名}，Windows D:\\apps\\{站点名}）', isEmpty: !t.deployPath.trim() },
      { label: '服务器地址', value: t.host || '未填写', isEmpty: !t.host.trim() },
      { label: 'SSH 用户名', value: t.userName || '未填写', isEmpty: !t.userName.trim() },
      { label: '验证健康', value: t.verifyHealth ? '开启' : '关闭' },
      { label: '保留数据库', value: t.keepDatabase ? '开启' : '关闭' },
      { label: '引用卡片', value: `${countTemplateUsage(t.id)} 张` },
    ]
  }
  templateDetailVisible.value = true
}

/** 模板编辑弹窗状态（构建/部署模板共用一个弹窗，内部按类型切换表单） */
const templateEditVisible = ref(false)
const templateEditKind = ref<'build' | 'deploy'>('build')
const templateEditId = ref('')
const buildTplForm = reactive({ name: '', type: 'Web' as UniversalBuildType, projectDir: '', outputDir: '' })
const deployTplForm = reactive({
  name: '',
  targetOS: 'Linux' as DeployTargetOS,
  siteName: '',
  serviceName: '',
  remoteDir: '',
  archiveName: '',
  deployPath: '',
  host: '',
  userName: '',
  verifyHealth: true,
  keepDatabase: true,
})
const buildTplEditRef = ref()
const deployTplEditRef = ref()
const buildTplRules = {
  name: [{ required: true, message: '名称不能为空', trigger: 'blur' }],
  projectDir: [{ required: true, message: '项目目录不能为空', trigger: 'blur' }],
}
const deployTplRules = {
  name: [{ required: true, message: '名称不能为空', trigger: 'blur' }],
  siteName: [{ required: true, message: '请填写站点名称', trigger: 'blur' }],
  host: [{ required: true, message: '请填写服务器地址', trigger: 'blur' }],
  userName: [{ required: true, message: '请填写 SSH 用户名', trigger: 'blur' }],
}

/** 打开模板编辑：表单字段拷贝填充（不直接绑模板对象，取消时不动原数据） */
function showTemplateEdit(tpl: BuildTemplate | DeployTemplate, kind: 'build' | 'deploy') {
  templateEditKind.value = kind
  templateEditId.value = tpl.id
  if (kind === 'build') {
    const t = tpl as BuildTemplate
    buildTplForm.name = t.name
    buildTplForm.type = t.type
    buildTplForm.projectDir = t.projectDir
    buildTplForm.outputDir = t.outputDir
  } else {
    const t = tpl as DeployTemplate
    deployTplForm.name = t.name
    deployTplForm.targetOS = t.targetOS
    deployTplForm.siteName = t.siteName
    deployTplForm.serviceName = t.serviceName
    deployTplForm.remoteDir = t.remoteDir
    deployTplForm.archiveName = t.archiveName
    deployTplForm.deployPath = t.deployPath
    deployTplForm.host = t.host
    deployTplForm.userName = t.userName
    deployTplForm.verifyHealth = t.verifyHealth
    deployTplForm.keepDatabase = t.keepDatabase
  }
  templateEditVisible.value = true
  nextTick(() => (templateEditKind.value === 'build' ? buildTplEditRef.value : deployTplEditRef.value)?.clearValidate())
}

/** 保存模板编辑：校验通过后写回模板数组并持久化（引用该部署模板的卡片自动部署时读取最新值） */
async function saveTemplateEdit() {
  try {
    await (templateEditKind.value === 'build' ? buildTplEditRef.value : deployTplEditRef.value)?.validate()
  } catch {
    return // 校验未通过，错误信息已显示在字段下方
  }
  if (templateEditKind.value === 'build') {
    const tpl = templates.value.find((t) => t.id === templateEditId.value)
    if (!tpl) return
    tpl.name = buildTplForm.name.trim()
    tpl.type = buildTplForm.type
    tpl.projectDir = buildTplForm.projectDir.trim()
    tpl.outputDir = buildTplForm.outputDir.trim()
    saveTemplates()
  } else {
    const tpl = deployTemplates.value.find((t) => t.id === templateEditId.value)
    if (!tpl) return
    tpl.name = deployTplForm.name.trim()
    tpl.targetOS = deployTplForm.targetOS
    tpl.siteName = deployTplForm.siteName.trim()
    tpl.serviceName = deployTplForm.serviceName.trim()
    tpl.remoteDir = deployTplForm.remoteDir.trim()
    tpl.archiveName = deployTplForm.archiveName.trim()
    tpl.deployPath = deployTplForm.deployPath.trim()
    tpl.host = deployTplForm.host.trim()
    tpl.userName = deployTplForm.userName.trim()
    tpl.verifyHealth = deployTplForm.verifyHealth
    tpl.keepDatabase = deployTplForm.keepDatabase
    saveDeployTemplates()
  }
  templateEditVisible.value = false
  ElMessage.success('模板已更新')
}

// ============================ 输入框聚焦悬浮加宽 ============================

let inputMeasureCtx: CanvasRenderingContext2D | null = null

/** 按输入框当前字体测量文本像素宽度（用于估算悬浮展开宽度） */
function measureInputText(text: string, font: string) {
  if (!inputMeasureCtx) {
    inputMeasureCtx = document.createElement('canvas').getContext('2d')
  }
  if (!inputMeasureCtx) return text.length * 8
  inputMeasureCtx.font = font
  return inputMeasureCtx.measureText(text).width
}

/**
 * 卡片内长文本输入框（任务名 / 项目目录 / 输出目录）聚焦时悬浮预览完整内容。
 * 方案：fixed 定位弹层直挂视口（z-index 3000）——脱离卡片嵌套的层叠上下文与滚动容器裁剪，
 * 在主窗口/独立窗口/窄屏下都不会被遮挡；纯展示不拦截鼠标（pointer-events: none）。
 * 输入框本身仍在原位可正常编辑，预览条只是“看全内容”。
 */
const inputPreview = reactive({
  visible: false,
  text: '',
  left: 0,
  top: 0,
  width: 0,
})

function showInputPreview(e: FocusEvent) {
  const inner = e.target as HTMLInputElement | null
  if (!inner) return
  const text = inner.value || inner.placeholder || ''
  if (!text.trim()) return
  const rect = inner.getBoundingClientRect()
  const font = getComputedStyle(inner).font || '12px sans-serif'
  const need = measureInputText(text, font) + 24
  const vw = window.innerWidth
  // 宽度：内容实际宽度起，不小于原输入框，不超出视口（两侧各留 16px）
  const width = Math.min(Math.max(need, rect.width), vw - 32)
  // 水平：默认与输入框左对齐，右缘越界时向左收
  let left = rect.left
  if (left + width > vw - 16) left = Math.max(16, vw - 16 - width)
  // 垂直：默认在输入框上方 6px，上方放不下改放下方
  let top = rect.top - 40
  if (top < 8) top = rect.bottom + 6
  inputPreview.visible = true
  inputPreview.text = text
  inputPreview.left = left
  inputPreview.top = top
  inputPreview.width = width
}

/** 失焦隐藏预览条 */
function hideInputPreview() {
  inputPreview.visible = false
}

/** 任务名等可编辑框输入过程中实时刷新预览文本（宽度/位置不变，超长自动换行） */
function refreshInputPreviewText(value: string) {
  if (inputPreview.visible) inputPreview.text = value || ''
}

/** 页面滚动时隐藏预览条（输入框已随内容移位，避免预览错位悬空） */
function onPageScroll() {
  hideInputPreview()
}

// ============================ 定时构建 ============================

const schedules = ref<ScheduleItem[]>([])
const scheduleVisible = ref(false)
const scheduleLoading = ref(false)
const scheduleCardId = ref('')
const scheduleForm = reactive({
  id: '',
  intervalMinutes: 60,
  enabled: true,
})

async function loadSchedules() {
  try {
    schedules.value = await getScheduleList()
  } catch {
    schedules.value = []
  }
}

/** 卡片关联的定时项（按 cardId 匹配） */
function getSchedule(card: BuildCard) {
  return schedules.value.find((s) => s.cardId === card.id)
}

/** 打开定时构建设置弹窗（已存在定时项则预填） */
function openScheduleDialog(card: BuildCard) {
  scheduleCardId.value = card.id
  scheduleForm.id = ''
  scheduleForm.intervalMinutes = 60
  scheduleForm.enabled = true
  const s = getSchedule(card)
  if (s) {
    scheduleForm.id = s.id || ''
    scheduleForm.intervalMinutes = s.intervalMinutes
    scheduleForm.enabled = s.enabled
  }
  scheduleVisible.value = true
}

async function saveSchedule() {
  const card = cards.find((c) => c.id === scheduleCardId.value)
  if (!card) return
  scheduleLoading.value = true
  try {
    const saved = await setSchedule({
      id: scheduleForm.id || undefined,
      cardId: card.id,
      name: card.name,
      type: card.type,
      projectDir: card.projectDir,
      outputDir: card.outputDir,
      intervalMinutes: scheduleForm.intervalMinutes,
      enabled: scheduleForm.enabled,
    })
    ElMessage.success(scheduleForm.enabled
      ? `定时构建已开启，下次触发：${saved.nextRunAt ? formatHistoryTime(saved.nextRunAt) : '稍后计算'}`
      : '定时构建已保存（未启用）')
    scheduleVisible.value = false
    await loadSchedules()
  } catch {
    /* 错误由拦截器统一提示 */
  } finally {
    scheduleLoading.value = false
  }
}

async function removeScheduleAction() {
  if (!scheduleForm.id) return
  try {
    await removeSchedule(scheduleForm.id)
    ElMessage.success('定时构建已删除')
    scheduleVisible.value = false
    await loadSchedules()
  } catch {
    /* 错误由拦截器统一提示 */
  }
}

// ============================ 耗时计时与完成通知 ============================

/** 部署步骤文本：[n/m] 标题（后端 DTO 拼好的展示用） */
function deployStepTextOf(dto: DeployJobDto): string {
  if (!dto.stepTitle) return ''
  return `[${dto.currentStep ?? '?'}/${dto.totalSteps ?? '?'}] ${dto.stepTitle}`
}

/**
 * 部署档位间进度爬行：后端进度只在阶段节点跳变（如 Docker 镜像构建 1-5 分钟纹丝不动），
 * 显示值在真实值基础上向「真实值 + 12」的虚拟目标缓慢逼近（封顶 99，100 由完成触发），
 * 真实值跳升时快速追上；SFTP 段真实值连续推进，显示值全程跟随。
 */
function advanceDeployCreep() {
  for (const card of cards) {
    if (card.deployStatus !== 'Running') {
      card.deployDisplayProgress = card.deployProgress
      continue
    }
    const real = card.deployProgress
    if (card.deployDisplayProgress < real) {
      card.deployDisplayProgress = Math.min(real, card.deployDisplayProgress + 2)
    } else {
      const ceiling = Math.min(real + 12, 99)
      if (card.deployDisplayProgress < ceiling) {
        card.deployDisplayProgress = Math.min(ceiling, card.deployDisplayProgress + 0.4)
      }
    }
  }
}

/** 秒级时钟：仅在存在运行中任务时跳动（驱动耗时实时显示 + 部署进度爬行） */
const nowTick = ref(Date.now())
let tickTimer: number | null = null

function ensureTick() {
  if (tickTimer) return
  tickTimer = window.setInterval(() => {
    nowTick.value = Date.now()
    advanceDeployCreep()
  }, 1000)
}

function stopTickIfIdle() {
  const busy = cards.some(c => c.status === 'Running' || c.status === 'Waiting' || c.deployStatus === 'Running')
  if (!busy && tickTimer) {
    clearInterval(tickTimer)
    tickTimer = null
  }
}

/** 毫秒 → mm:ss / h:mm:ss */
function formatElapsed(ms: number): string {
  const s = Math.max(0, Math.floor(ms / 1000))
  const h = Math.floor(s / 3600)
  const m = Math.floor((s % 3600) / 60)
  const sec = s % 60
  return h > 0
    ? `${h}:${String(m).padStart(2, '0')}:${String(sec).padStart(2, '0')}`
    : `${m}:${String(sec).padStart(2, '0')}`
}

/** 构建耗时：进行中实时跳动，结束后固定 */
function buildElapsed(card: BuildCard): string {
  if (!card.startTime) return ''
  const end = (card.status === 'Running' || card.status === 'Waiting') ? nowTick.value : (card.endTime ?? nowTick.value)
  return formatElapsed(end - card.startTime)
}

/** 部署耗时 */
function deployElapsed(card: BuildCard): string {
  if (!card.deployStartTime) return ''
  const end = card.deployStatus === 'Running' ? nowTick.value : (card.deployEndTime ?? nowTick.value)
  return formatElapsed(end - card.deployStartTime)
}

/** 字节数 → 可读大小 */
function formatSize(bytes: number): string {
  if (!bytes) return '0 B'
  const units = ['B', 'KB', 'MB', 'GB']
  let v = bytes
  let i = 0
  while (v >= 1024 && i < units.length - 1) { v /= 1024; i++ }
  return `${v >= 100 ? Math.round(v) : v.toFixed(1)} ${units[i]}`
}

/** 任务结束通知：页面内需弹通知；页面在后台时再尝试系统通知 */
function notifyDone(title: string, message: string, type: 'success' | 'error') {
  ElNotification({ title, message, type, duration: 5000 })
  if (document.hidden && 'Notification' in window && Notification.permission === 'granted') {
    try { new Notification(title, { body: message }) } catch { /* WebView2 不支持时静默 */ }
  }
}

// ============================ 日志智能滚动 ============================

/** 是否跟随日志滚动：用户上翻暂停，滚回底部恢复 */
const logAutoFollow = ref(true)

function onLogScroll() {
  const el = logTerminalRef.value
  if (!el) return
  logAutoFollow.value = el.scrollHeight - el.scrollTop - el.clientHeight < 40
}

function scrollToBottom() {
  logAutoFollow.value = true
  const el = logTerminalRef.value
  if (el) el.scrollTop = el.scrollHeight
}

// 自动滚动到底部（仅跟随模式下；切卡片/切标签时重置跟随并滚到底）
watch(
  () => [selectedCard.value?.id, activeLogTab.value],
  async () => {
    logAutoFollow.value = true
    await nextTick()
    const el = logTerminalRef.value
    if (el) el.scrollTop = el.scrollHeight
  },
)

// 日志更新：跟随模式下滚动到底（用户上翻时不打断）
watch(
  () => [selectedCard.value?.log, selectedCard.value?.deployLog],
  async () => {
    if (!selectedCard.value) return
    const isDeployRunning = selectedCard.value.deployStatus === 'Running'
    const isBuildRunning = selectedCard.value.status === 'Running' || selectedCard.value.status === 'Waiting'
    if (!isDeployRunning && !isBuildRunning) return
    if (!logAutoFollow.value) return
    await nextTick()
    if (logTerminalRef.value) {
      logTerminalRef.value.scrollTop = logTerminalRef.value.scrollHeight
    }
  },
)

onMounted(async () => {
  // 打开页面即检测本地构建环境，并默认弹出检测结果弹窗
  envVisible.value = true
  loadEnvironment()
  // 恢复上次的卡片配置（没有存档时给一张默认卡片）
  if (!await restoreCards()) {
    addCard()
  }
  restoreDeployRemember()
  loadTemplates()
  loadDeployTemplates()
  loadSchedules()
  // 尝试授权系统通知（页面在后台时构建/部署完成可弹系统通知；失败不影响功能）
  try {
    if ('Notification' in window && Notification.permission === 'default') Notification.requestPermission()
  } catch { /* ignore */ }
})

onUnmounted(() => {
  stopPolling()
  stopDeployPolling()
  if (tickTimer) { clearInterval(tickTimer); tickTimer = null }
  if (persistTimer) { window.clearTimeout(persistTimer); persistTimer = null }
})
</script>

<template>
  <div class="universal-build-page" @scroll.passive="onPageScroll">
    <!-- 导入配置的隐藏文件选择框 -->
    <input ref="importFileInput" type="file" accept=".json,application/json" style="display: none" @change="importConfig" />

    <!-- 工具栏 -->
    <div class="toolbar">
      <el-button :icon="Monitor" :loading="envLoading" @click="openEnvDialog">环境检测</el-button>
      <el-button :icon="VideoPlay" @click="buildAll">全部构建</el-button>
      <el-button :icon="Collection" @click="templateVisible = true">模板库</el-button>
      <el-button :icon="Clock" @click="openHistory">部署历史</el-button>
      <el-button :icon="Promotion" @click="gotoPipeline">流水线</el-button>
      <el-button :icon="Brush" @click="openArtifactDialog">产物清理</el-button>
      <el-button :icon="Download" @click="exportConfig">导出配置</el-button>
      <el-button :icon="Upload" @click="triggerImport">导入配置</el-button>
      <el-button type="primary" :icon="Plus" @click="addCard">新增构建</el-button>
      <el-button type="success" :icon="UploadFilled" @click="openDeployOnlyDialog">部署到服务器</el-button>
    </div>

    <!-- 环境检测弹窗：页面打开时默认弹出 -->
    <el-dialog v-model="envVisible" title="本地构建环境" width="720px">
      <div class="env-dialog-toolbar">
        <span class="env-dialog-summary">检测各构建类型依赖的本地工具链，缺失工具可点「去下载」安装</span>
        <el-button size="small" :loading="envLoading" link type="primary" @click="loadEnvironment">重新检测</el-button>
      </div>
      <div class="env-list">
        <div
          v-for="item in envInfo"
          :key="item.type"
          class="env-item"
          :class="{ ok: item.installed, missing: !item.installed }"
        >
          <el-icon v-if="item.installed" class="env-icon ok"><CircleCheck /></el-icon>
          <el-icon v-else class="env-icon missing"><CircleClose /></el-icon>
          <div class="env-info">
            <div class="env-label" :title="envCommandMap[item.type] || ''">{{ item.name }}</div>
            <div class="env-message" :title="item.message">{{ item.message || (item.installed ? '已安装' : '未安装') }}</div>
          </div>
          <div v-if="!item.installed" class="env-actions">
            <el-button size="small" type="primary" link @click="openDownload(item.downloadUrl)">去下载</el-button>
          </div>
        </div>
      </div>
    </el-dialog>

    <!-- 构建卡片 -->
    <div class="build-cards">
      <el-card
        v-for="card in cards"
        :key="card.id"
        v-loading="startingCardId === card.id"
        element-loading-text="启动中…"
        class="build-card"
        :class="{ selected: selectedCardId === card.id, running: card.status === 'Running' || card.status === 'Waiting' }"
        shadow="hover"
        :body-style="{ padding: '12px' }"
        @click="selectCard(card)"
      >
        <div class="card-header">
          <div class="name-input-wrap">
            <el-input v-model="card.name" size="small" placeholder="任务名称" class="card-name-input" :style="{ '--name-color': typeColor(card.type) }" :disabled="isRunning(card)" @input="onNameChange(card); refreshInputPreviewText($event)" @focus="showInputPreview" @blur="hideInputPreview" />
          </div>
          <el-select v-model="card.type" size="small" class="type-select" :disabled="isRunning(card)" @change="onTypeChange(card)">
            <el-option
              v-for="opt in typeOptions"
              :key="opt.value"
              :label="opt.label"
              :value="opt.value"
            />
          </el-select>
          <el-tooltip v-if="!isRunning(card)" content="存为模板" placement="top">
            <el-icon class="header-icon" @click.stop="saveCardAsTemplate(card)"><Collection /></el-icon>
          </el-tooltip>
          <el-tooltip v-if="!isRunning(card)" content="定时构建" placement="top">
            <el-icon class="header-icon" :class="{ active: !!getSchedule(card) }" @click.stop="openScheduleDialog(card)"><AlarmClock /></el-icon>
          </el-tooltip>
          <el-tooltip v-if="!isRunning(card)" content="删除卡片" placement="top">
            <el-icon class="header-icon danger" @click.stop="removeCard(card.id)"><Delete /></el-icon>
          </el-tooltip>
        </div>

        <div class="card-field">
          <div class="field-label">项目目录</div>
          <div class="field-input-wrap">
            <el-input v-model="card.projectDir" placeholder="选择或手动输入项目目录" size="small" @focus="showInputPreview" @blur="hideInputPreview" @input="refreshInputPreviewText($event)">
              <template #append>
                <el-button type="primary" :icon="FolderOpened" :disabled="isRunning(card)" @click.stop="pickProjectDir(card)">选择</el-button>
              </template>
            </el-input>
          </div>
        </div>

        <div class="card-field">
          <div class="field-label">输出目录</div>
          <div class="field-input-wrap">
            <el-input v-model="card.outputDir" class="output-dir-input" placeholder="默认输出路径（可手动修改）" size="small" @focus="showInputPreview" @blur="hideInputPreview" @input="card.outputDirCustom = true; refreshInputPreviewText($event)">
              <template #append>
                <el-button :icon="FolderOpened" :disabled="isRunning(card)" @click.stop="pickOutputDir(card)">选择</el-button>
              </template>
              <template #suffix>
                <el-tooltip content="复制路径" placement="top">
                  <el-icon class="copy-icon" @click.stop="copyPath(card.outputDir)"><DocumentCopy /></el-icon>
                </el-tooltip>
                <el-tooltip content="打开目录" placement="top">
                  <el-icon class="copy-icon" @click.stop="openOutputDir(card)"><Folder /></el-icon>
                </el-tooltip>
              </template>
            </el-input>
          </div>
        </div>

        <!-- 构建行为选项：拉取最新代码 / 成功后自动部署 + 自动部署模版（构建中锁定） -->
        <div class="card-options">
          <el-checkbox v-model="card.prePull" size="small" :disabled="isRunning(card)" @click.stop>构建前 git pull</el-checkbox>
          <el-checkbox v-model="card.packArtifact" size="small" :disabled="isRunning(card)" @click.stop>成功后打压缩包</el-checkbox>
          <el-checkbox v-model="card.autoDeploy" size="small" :disabled="isRunning(card)" @click.stop>成功后自动部署</el-checkbox>
          <el-select
            v-if="card.autoDeploy && deployTemplates.length > 0"
            v-model="card.deployTemplateId"
            size="small"
            :disabled="isRunning(card)"
            placeholder="不选=用上次配置"
            class="deploy-template-select"
            @click.stop
          >
            <el-option label="不选模版（用上次部署配置）" value="" />
            <el-option v-for="t in deployTemplates" :key="t.id" :label="t.name" :value="t.id" />
          </el-select>
        </div>

        <!-- 状态标记 + 结果摘要：耗时 / 产物大小 / 部署耗时 -->
        <div v-if="card.status || card.deployStatus || buildElapsed(card) || card.artifactSize || deployElapsed(card)" class="card-result">
          <el-tag v-if="!isRunning(card)" :type="statusType(card.status)" size="small" effect="light">{{ statusText(card.status) }}</el-tag>
          <el-tag v-if="card.deployStatus && card.deployStatus !== 'Running'" :type="deployStatusType(card.deployStatus)" size="small" effect="dark">{{ deployStatusText(card.deployStatus) }}</el-tag>
          <span v-if="buildElapsed(card)">构建耗时 {{ buildElapsed(card) }}</span>
          <span v-if="card.artifactSize">产物 {{ formatSize(card.artifactSize) }}</span>
          <el-tooltip v-if="card.artifactArchiveSize" :content="card.artifactArchivePath" placement="top">
            <span class="archive-size">压缩包 {{ formatSize(card.artifactArchiveSize) }}</span>
          </el-tooltip>
          <span v-else-if="card.status === 'Success' && card.packArtifact" class="archive-failed">压缩包打包失败</span>
          <span v-if="deployElapsed(card)">部署耗时 {{ deployElapsed(card) }}</span>
        </div>

        <div v-if="card.status === 'Running' || card.status === 'Waiting'" class="card-progress">
          <el-progress :percentage="card.progress" :stroke-width="6" :show-text="true" />
        </div>

        <!-- 部署进度条（橙色）：上传段按字节实时推进，档位间缓慢爬行；步骤名显示当前在干什么 -->
        <div v-if="card.deployStatus === 'Running'" class="card-progress">
          <div v-if="card.deployStepText" class="deploy-step-hint">{{ card.deployStepText }}</div>
          <el-progress :percentage="Math.floor(card.deployDisplayProgress)" :stroke-width="6" :show-text="true" status="warning" />
        </div>

        <div class="card-actions">
          <el-button
            type="primary"
            size="small"
            :loading="isRunning(card)"
            :icon="(card.status === 'Failed' || card.status === 'Cancelled') ? RefreshRight : undefined"
            @click.stop="startBuild(card)"
          >{{ card.status === 'Waiting' ? (card.queuePosition > 1 ? `排队中 #${card.queuePosition}` : '排队中...') : isRunning(card) ? '构建中...' : (card.status === 'Failed' || card.status === 'Cancelled') ? '重试' : '开始构建' }}</el-button>
          <el-button
            v-if="card.deployStatus === 'Success'"
            type="success"
            size="small"
            plain
            :icon="Link"
            @click.stop="openSite(card)"
          >打开站点</el-button>
          <el-button v-if="card.status === 'Running' || card.status === 'Waiting'" type="danger" size="small" plain @click.stop="cancelBuild(card)">取消构建</el-button>
          <el-button v-if="card.deployStatus === 'Running'" type="danger" size="small" plain @click.stop="cancelDeployAction(card)">取消部署</el-button>
          <el-button v-if="card.deployStatus === 'Success' && card.lastDeployConfig" type="warning" size="small" plain :icon="RefreshLeft" :disabled="card.status === 'Running' || card.status === 'Waiting'" @click.stop="rollbackCard(card)">回滚</el-button>
          <el-button
            v-if="card.status === 'Success'"
            type="warning"
            size="small"
            :loading="card.deployStatus === 'Running'"
            :icon="(card.deployStatus === 'Failed' || card.deployStatus === 'Cancelled') ? RefreshRight : undefined"
            @click.stop="openDeployDialog(card)"
          >{{ card.deployStatus === 'Running' ? '部署中...' : (card.deployStatus === 'Failed' || card.deployStatus === 'Cancelled') ? '重试' : '🚀 部署' }}</el-button>
        </div>
      </el-card>
    </div>

    <!-- 日志区（构建日志 / 部署日志 标签切换） -->
    <el-card class="log-card" shadow="never">
      <template #header>
        <div class="log-header">
          <el-tabs v-model="activeLogTab" class="log-tabs">
            <el-tab-pane label="构建日志" name="build">
              <span class="log-title">{{ selectedCard?.name || '未选择' }} ({{ selectedCard ? typeLabelMap[selectedCard.type] : '' }})</span>
            </el-tab-pane>
            <el-tab-pane label="部署日志" name="deploy">
              <span class="log-title">
                {{ selectedCard?.deployStatus ? `${selectedCard.name} - ${deployStatusText(selectedCard.deployStatus)}` : '暂无部署任务' }}
              </span>
            </el-tab-pane>
          </el-tabs>
          <!-- 日志过滤：类型快捷筛选 + 关键字，统计显示行数 -->
          <div class="log-filter">
            <span v-if="logFilterStat && (logFilter || logFilterType)" class="log-filter-stat">
              {{ logFilterStat.shown }}/{{ logFilterStat.total }} 行
            </span>
            <el-radio-group v-model="logFilterType" size="small">
              <el-radio-button value="">全部</el-radio-button>
              <el-radio-button value="error">❌</el-radio-button>
              <el-radio-button value="warn">⚠</el-radio-button>
              <el-radio-button value="success">✓</el-radio-button>
            </el-radio-group>
            <el-input v-model="logFilter" size="small" clearable placeholder="过滤关键字" class="log-filter-input" />
          </div>
          <!-- 复制当前标签页完整日志（剥离 ANSI 转义码后的纯文本） -->
          <el-tooltip content="复制完整日志" placement="top">
            <el-button :icon="DocumentCopy" size="small" text type="primary" @click="copyLog" />
          </el-tooltip>
        </div>
      </template>
      <div ref="logTerminalRef" class="log-terminal" @scroll="onLogScroll">
        <template v-if="activeLogTab === 'build'">
          <pre v-if="selectedCard?.log || selectedCard?.preLog" v-html="formatLogHtml((selectedCard?.preLog || '') + (selectedCard?.log || ''))"></pre>
          <el-empty v-else description="暂无构建日志" />
        </template>
        <template v-else>
          <pre v-if="selectedCard?.deployLog" v-html="formatLogHtml(selectedCard.deployLog)"></pre>
          <el-empty v-else description="暂无部署日志，请先点击卡片上的 🚀 部署 按钮" />
        </template>
      </div>
      <!-- 上翻查日志后出现，点击回到底部并恢复自动跟随 -->
      <transition name="el-fade-in">
        <button
          v-show="!logAutoFollow && (activeLogTab === 'build' ? selectedCard?.log : selectedCard?.deployLog)"
          class="scroll-bottom-btn"
          @click="scrollToBottom"
        >
          <el-icon><ArrowDown /></el-icon>
          <span>回到底部</span>
        </button>
      </transition>
    </el-card>

    <!-- 部署弹窗 -->
    <el-dialog v-model="deployDialogVisible" title="部署构建产物" width="560px" :close-on-click-modal="false">
      <el-form ref="deployFormRef" :model="deployForm" :rules="deployFormRules" label-width="90px" size="small" class="deploy-form">
        <el-form-item label="部署模版">
          <div class="template-row">
            <el-select v-model="selectedDeployTemplateId" placeholder="不使用模版，手动填写" clearable @change="onDeployTemplateChange">
              <el-option v-for="t in deployTemplates" :key="t.id" :label="t.name" :value="t.id" />
            </el-select>
            <el-button size="small" @click="saveDeployTemplate">存为模版</el-button>
            <el-button size="small" type="danger" plain :icon="Delete" :disabled="!selectedDeployTemplateId" @click="removeDeployTemplateById(selectedDeployTemplateId)" />
          </div>
          <div class="form-hint">选择模版自动填充下方全部配置（填充后仍可修改）；手动填好的配置也可存为模版复用</div>
        </el-form-item>
        <el-form-item label="部署目录" prop="outputDir">
          <el-input v-if="deployReadOnlyDir" :model-value="deployForm.outputDir" readonly>
            <template #suffix>
              <el-tooltip content="打开目录" placement="top">
                <el-icon class="copy-icon" @click="openDeployOutputDir"><Folder /></el-icon>
              </el-tooltip>
            </template>
          </el-input>
          <el-input v-else v-model="deployForm.outputDir" placeholder="选择要上传到服务器的本地构建产物目录">
            <template #append>
              <el-button :icon="FolderOpened" @click="pickDeployOutputDir">选择</el-button>
            </template>
            <template #suffix>
              <el-tooltip content="打开目录" placement="top">
                <el-icon class="copy-icon" @click="openDeployOutputDir"><Folder /></el-icon>
              </el-tooltip>
            </template>
          </el-input>
          <div class="form-hint">当前部署产物：{{ deployForm.buildName }}（{{ typeLabelMap[deployForm.buildType] }}）</div>
        </el-form-item>
        <el-form-item label="目标系统">
          <el-radio-group v-model="deployForm.targetOS">
            <el-radio-button value="Linux">Linux (Docker)</el-radio-button>
            <el-radio-button value="Windows">Windows (IIS/服务)</el-radio-button>
          </el-radio-group>
          <div class="form-hint">Linux 通过 SSH + Docker Compose 部署；Windows 通过 SSH + Windows 服务/IIS 部署</div>
        </el-form-item>
        <el-form-item label="站点名称" prop="siteName">
          <el-input v-model="deployForm.siteName" placeholder="如 convenient" />
          <div class="site-exists-hint" :class="{ exists: siteExistsInfo?.exists, new: !siteExistsInfo?.exists }">
            {{ siteExistsInfo?.message || ' ' }}
          </div>
          <div class="form-hint">Docker Compose 项目名 / Windows 站点目录名，不同站点互相隔离</div>
        </el-form-item>
        <el-form-item label="服务名">
          <el-input v-model="deployForm.serviceName" placeholder="留空自动推断：前端→web，后端→api" />
          <div class="form-hint">Linux 对应 docker compose 服务名，Windows 对应 Windows 服务名</div>
        </el-form-item>
        <el-form-item label="远程目录">
          <el-input v-model="deployForm.remoteDir" placeholder="留空按构建类型自动推断" />
          <div class="form-hint">产物在服务器上最终解压的目标路径，留空时后端自动选择默认路径</div>
        </el-form-item>
        <el-form-item label="压缩包名">
          <el-input v-model="deployForm.archiveName" :placeholder="`留空自动生成 {站点名}-{服务名}${archiveExtension(deployForm.targetOS)}`" />
          <div class="form-hint">上传用的临时压缩包名，固定命名可覆盖旧包，不会叠加文件</div>
        </el-form-item>
        <el-form-item label="服务器地址" prop="host">
          <el-input v-model="deployForm.host" placeholder="如 123.56.68.132" />
          <div class="form-hint">目标服务器的公网 IP 或域名，用于 SSH/SFTP 连接</div>
        </el-form-item>
        <el-form-item label="SSH用户名" prop="userName">
          <el-input v-model="deployForm.userName" placeholder="如 root" />
          <div class="form-hint">具有 SSH/SFTP 权限的登录用户，Linux 服务器通常为 root</div>
        </el-form-item>
        <el-form-item label="SSH密码" prop="password">
          <div class="password-row">
            <el-input v-model="deployForm.password" type="password" show-password placeholder="输入 SSH 登录密码" />
            <el-checkbox v-model="deployForm.rememberPassword">记住密码</el-checkbox>
          </div>
          <div class="form-hint">对应 SSH 用户的登录密码；勾选“记住密码”后 DPAPI 加密保存在本机，下次自动回填</div>
        </el-form-item>
        <el-form-item label="部署路径">
          <el-input v-model="deployForm.deployPath" placeholder="留空使用默认路径" />
          <div class="form-hint">
            Linux 默认 /opt/{站点名}（docker-compose.yml 所在目录），Windows 默认 D:\apps\{站点名}
          </div>
        </el-form-item>
        <el-form-item label="选项">
          <el-checkbox v-model="deployForm.verifyHealth">部署后自动验证健康检查</el-checkbox>
          <el-checkbox v-model="deployForm.keepDatabase">保留数据库容器（仅 Linux Docker）</el-checkbox>
          <div class="form-hint">验证：Linux 用 curl 检查容器，Windows 用 Invoke-WebRequest 检查服务</div>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="deployDialogVisible = false">取消</el-button>
        <el-button type="warning" :loading="deployLoading" @click="startDeployAction">🚀 开始部署</el-button>
      </template>
    </el-dialog>

    <!-- 部署历史弹窗：宽幅弹窗 + 表体独立滚动（表头固定），各列完整展示 -->
    <el-dialog v-model="historyVisible" title="部署历史（最近 100 条）" width="880px">
      <el-table :data="historyItems" size="small" stripe :max-height="520">
        <el-table-column label="时间" width="100" fixed="left">
          <template #default="{ row }">{{ formatHistoryTime(row.startTime) }}</template>
        </el-table-column>
        <el-table-column prop="buildName" label="任务" min-width="240" show-overflow-tooltip />
        <el-table-column prop="siteName" label="站点" width="90" show-overflow-tooltip />
        <el-table-column label="目标" width="64">
          <template #default="{ row }">{{ row.targetOS === 'Linux' ? 'Linux' : 'Win' }}</template>
        </el-table-column>
        <el-table-column label="结果" width="82">
          <template #default="{ row }">
            <el-tag :type="deployStatusType(row.status)" size="small" effect="light">{{ deployStatusText(row.status) }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="耗时" width="70">
          <template #default="{ row }">{{ formatElapsed(row.durationSeconds * 1000) }}</template>
        </el-table-column>
        <el-table-column prop="host" label="服务器" width="120" show-overflow-tooltip />
        <el-table-column label="操作" width="128" fixed="right">
          <template #default="{ row }">
            <el-button size="small" type="primary" plain :disabled="!row.jobId" @click="showHistoryLog(row as DeployHistoryItem)">日志</el-button>
            <el-button size="small" type="warning" plain @click="rollbackFromHistory(row as DeployHistoryItem)">回滚</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-dialog>

    <!-- 部署历史日志弹层：复用 log-terminal 黑底终端样式（不过滤，展示完整日志） -->
    <el-dialog v-model="historyLogVisible" :title="`部署日志 - ${historyLog?.buildName || ''}`" width="760px">
      <div v-loading="historyLogLoading">
        <template v-if="historyLog">
          <div class="history-log-meta">
            <el-tag :type="historyLogStatusMeta.type" size="small" effect="light">{{ historyLogStatusMeta.text }}</el-tag>
            <span>开始 {{ formatHistoryTime(historyLog.startTime || '') }}</span>
            <span v-if="historyLog.completedTime">完成 {{ formatHistoryTime(historyLog.completedTime) }}</span>
          </div>
          <div class="log-terminal history-log-terminal">
            <pre v-html="formatHistoryLogHtml(historyLog.log)"></pre>
          </div>
        </template>
        <el-empty v-else-if="!historyLogLoading" description="未获取到日志" />
      </div>
    </el-dialog>

    <!-- 产物占用清理弹窗：列出各卡片产物目录，勾选后清空（保留目录本身） -->
    <el-dialog v-model="artifactVisible" title="产物占用清理" width="640px">
      <div class="env-dialog-toolbar">
        <span class="env-dialog-summary">清空构建产物目录内容可释放磁盘空间（目录本身保留，下次构建重新生成）</span>
        <el-button size="small" :loading="artifactLoading" link type="primary" @click="refreshArtifactUsage">重新统计</el-button>
      </div>
      <div v-loading="artifactLoading" class="artifact-list">
        <div v-if="artifactRows.length === 0 && !artifactLoading" class="template-empty">
          暂无产物目录（卡片未设置输出目录或尚未构建）
        </div>
        <div v-for="(row, idx) in artifactRows" :key="idx" class="artifact-item">
          <el-checkbox v-model="row.selected" :disabled="!row.exists || row.sizeBytes <= 0" />
          <div class="artifact-info">
            <div class="template-name">{{ row.cardName }}</div>
            <div class="template-meta" :title="row.path">{{ row.path }}</div>
            <div class="template-meta">
              {{ row.exists ? (row.sizeBytes > 0 ? `${formatBytes(row.sizeBytes)} · ${row.fileCount} 个文件 · 最后写入 ${row.lastWriteTime ? formatHistoryTime(row.lastWriteTime) : '-'}` : '目录为空') : '目录不存在（尚未构建）' }}
            </div>
          </div>
        </div>
      </div>
      <template #footer>
        <el-button @click="artifactVisible = false">关闭</el-button>
        <el-button type="danger" :loading="artifactCleaning" @click="cleanSelectedArtifacts">清理所选</el-button>
      </template>
    </el-dialog>

    <!-- 模板库抽屉：构建模板 + 部署模板 -->
    <el-drawer v-model="templateVisible" title="模板库" size="480px">
      <el-tabs v-model="templateTab" class="template-tabs">
        <el-tab-pane :label="`构建模板 (${templates.length})`" name="build">
          <div v-if="templates.length === 0" class="template-empty">
            暂无构建模板。点击卡片标题栏的收藏图标，可把当前配置存为模板，下次一键新建。
          </div>
          <div v-for="tpl in templates" :key="tpl.id" class="template-item">
            <div class="template-info">
              <div class="template-name" :style="{ color: typeColor(tpl.type) }">{{ tpl.name }}</div>
              <div class="template-meta" :title="tpl.projectDir">{{ typeLabelMap[tpl.type] }} · {{ tpl.projectDir }}</div>
            </div>
            <el-button size="small" type="primary" @click="addCardFromTemplate(tpl)">新建</el-button>
            <el-button size="small" @click="showTemplateDetail(tpl, 'build')">详情</el-button>
            <el-button size="small" type="warning" plain @click="showTemplateEdit(tpl, 'build')">编辑</el-button>
            <el-button size="small" type="danger" plain :icon="Delete" @click="removeTemplate(tpl.id)" />
          </div>
        </el-tab-pane>
        <el-tab-pane :label="`部署模板 (${deployTemplates.length})`" name="deploy">
          <div v-if="deployTemplates.length === 0" class="template-empty">
            暂无部署模板。在部署弹窗中填好配置后点「存为模版」，构建卡片的自动部署可直接引用。
          </div>
          <div v-for="tpl in deployTemplates" :key="tpl.id" class="template-item">
            <div class="template-info">
              <div class="template-name">
                <el-tag :type="tpl.targetOS === 'Linux' ? 'success' : 'primary'" size="small" effect="light">{{ tpl.targetOS === 'Linux' ? 'Linux' : 'Win' }}</el-tag>
                <span class="deploy-tpl-name">{{ tpl.name }}</span>
              </div>
              <div class="template-meta" :title="`${tpl.host} · ${tpl.deployPath}`">{{ tpl.siteName || '未填站点' }} · {{ tpl.host }}</div>
              <div class="template-meta">引用卡片：{{ countTemplateUsage(tpl.id) }} 张</div>
            </div>
            <el-button size="small" @click="showTemplateDetail(tpl, 'deploy')">详情</el-button>
            <el-button size="small" type="warning" plain @click="showTemplateEdit(tpl, 'deploy')">编辑</el-button>
            <el-button size="small" type="danger" plain :icon="Delete" @click="removeDeployTemplateById(tpl.id)" />
          </div>
        </el-tab-pane>
      </el-tabs>
    </el-drawer>

    <!-- 模板详情弹窗：构建/部署模板共用，打开时预计算展示行 -->
    <el-dialog v-model="templateDetailVisible" :title="templateDetailTitle" width="540px">
      <el-descriptions :column="1" border size="small">
        <el-descriptions-item v-for="row in templateDetailRows" :key="row.label" :label="row.label">
          <span :class="{ 'tpl-detail-empty': row.isEmpty }">{{ row.value }}</span>
        </el-descriptions-item>
      </el-descriptions>
    </el-dialog>

    <!-- 模板编辑弹窗：构建/部署模板共用，按类型切换表单（表单绑副本，取消不影响原模板） -->
    <el-dialog v-model="templateEditVisible" :title="templateEditKind === 'build' ? '编辑构建模板' : '编辑部署模板'" width="480px" :close-on-click-modal="false">
      <el-form v-if="templateEditKind === 'build'" ref="buildTplEditRef" :model="buildTplForm" :rules="buildTplRules" label-width="80px" size="small">
        <el-form-item label="名称" prop="name">
          <el-input v-model="buildTplForm.name" placeholder="模板名称" />
        </el-form-item>
        <el-form-item label="构建类型">
          <el-select v-model="buildTplForm.type" style="width: 100%;">
            <el-option v-for="opt in typeOptions" :key="opt.value" :label="opt.label" :value="opt.value" />
          </el-select>
        </el-form-item>
        <el-form-item label="项目目录" prop="projectDir">
          <el-input v-model="buildTplForm.projectDir" placeholder="本地项目根目录" />
        </el-form-item>
        <el-form-item label="输出目录">
          <el-input v-model="buildTplForm.outputDir" placeholder="留空使用默认输出路径" />
        </el-form-item>
      </el-form>
      <el-form v-else ref="deployTplEditRef" :model="deployTplForm" :rules="deployTplRules" label-width="80px" size="small">
        <el-form-item label="名称" prop="name">
          <el-input v-model="deployTplForm.name" placeholder="模板名称" />
        </el-form-item>
        <el-form-item label="目标系统">
          <el-radio-group v-model="deployTplForm.targetOS">
            <el-radio-button value="Linux">Linux (Docker)</el-radio-button>
            <el-radio-button value="Windows">Windows (IIS/服务)</el-radio-button>
          </el-radio-group>
        </el-form-item>
        <el-form-item label="站点名称" prop="siteName">
          <el-input v-model="deployTplForm.siteName" placeholder="如 convenient" />
        </el-form-item>
        <el-form-item label="服务名">
          <el-input v-model="deployTplForm.serviceName" placeholder="留空自动推断：前端→web，后端→api" />
        </el-form-item>
        <el-form-item label="远程目录">
          <el-input v-model="deployTplForm.remoteDir" placeholder="留空按构建类型自动推断" />
        </el-form-item>
        <el-form-item label="压缩包名">
          <el-input v-model="deployTplForm.archiveName" placeholder="留空自动生成" />
        </el-form-item>
        <el-form-item label="部署路径">
          <el-input v-model="deployTplForm.deployPath" placeholder="留空使用默认路径" />
        </el-form-item>
        <el-form-item label="服务器地址" prop="host">
          <el-input v-model="deployTplForm.host" placeholder="如 123.56.68.132" />
        </el-form-item>
        <el-form-item label="SSH用户名" prop="userName">
          <el-input v-model="deployTplForm.userName" placeholder="如 root" />
        </el-form-item>
        <el-form-item label="选项">
          <el-checkbox v-model="deployTplForm.verifyHealth">部署后自动验证健康检查</el-checkbox>
          <el-checkbox v-model="deployTplForm.keepDatabase">保留数据库容器（仅 Linux Docker）</el-checkbox>
        </el-form-item>
      </el-form>
      <div class="form-hint">修改部署模板后，引用它的卡片自动部署时将使用最新配置</div>
      <template #footer>
        <el-button @click="templateEditVisible = false">取消</el-button>
        <el-button type="primary" @click="saveTemplateEdit">保存</el-button>
      </template>
    </el-dialog>

    <!-- 定时构建弹窗 -->
    <el-dialog v-model="scheduleVisible" title="定时构建" width="430px">
      <el-form label-width="80px" size="small">
        <el-form-item label="启用">
          <el-switch v-model="scheduleForm.enabled" />
        </el-form-item>
        <el-form-item label="触发间隔">
          <el-select v-model="scheduleForm.intervalMinutes" :disabled="!scheduleForm.enabled" style="width: 200px;">
            <el-option label="每 30 分钟" :value="30" />
            <el-option label="每 1 小时" :value="60" />
            <el-option label="每 2 小时" :value="120" />
            <el-option label="每 6 小时" :value="360" />
            <el-option label="每 12 小时" :value="720" />
            <el-option label="每 24 小时" :value="1440" />
          </el-select>
        </el-form-item>
      </el-form>
      <div class="form-hint">
        到点后按保存时的卡片配置自动触发构建（程序需保持运行）。修改卡片配置后请重新保存定时项。
      </div>
      <template #footer>
        <el-button v-if="scheduleForm.id" type="danger" plain @click="removeScheduleAction">删除</el-button>
        <el-button @click="scheduleVisible = false">取消</el-button>
        <el-button type="primary" :loading="scheduleLoading" @click="saveSchedule">保存</el-button>
      </template>
    </el-dialog>

    <!-- 长文本输入框聚焦悬浮预览条：fixed 直挂视口，不受卡片层级/滚动容器裁剪影响 -->
    <transition name="el-fade-in">
      <div
        v-if="inputPreview.visible"
        class="input-preview-pop"
        :style="{ left: `${inputPreview.left}px`, top: `${inputPreview.top}px`, width: `${inputPreview.width}px` }"
      >
        <span class="input-preview-text">{{ inputPreview.text }}</span>
      </div>
    </transition>
  </div>
</template>

<style scoped>
.universal-build-page {
  padding: 16px;
  /* height:100%（非 min-height）：主窗口与独立窗口（.standalone-page 为 100vh+overflow:hidden）
     下父容器高度均确定，页面自身接管滚动，内容超出时出现滚动条，两种环境都显示完整 */
  height: 100%;
  overflow-y: auto;
}

.universal-build-page > * {
  margin-bottom: 16px;
}

.universal-build-page > *:last-child {
  margin-bottom: 0;
}

/* 环境检测弹窗：顶部说明 + 重新检测 */
.env-dialog-toolbar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 12px;
}

.env-dialog-summary {
  font-size: 12px;
  color: #909399;
}

.env-list {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(200px, 1fr));
  gap: 12px;
}

.env-item {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 10px 12px;
  border-radius: 6px;
  background: #f5f7fa;
}

.env-item.ok {
  background: #f0fdf4;
}

.env-item.missing {
  background: #fef2f2;
}

.env-icon {
  font-size: 20px;
}

.env-icon.ok {
  color: #16a34a;
}

.env-icon.missing {
  color: #dc2626;
}

.env-info {
  flex: 1;
  min-width: 0;
}

.env-label {
  font-size: 13px;
  font-weight: 500;
}

.env-message {
  font-size: 12px;
  color: #6b7280;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.toolbar {
  display: flex;
  justify-content: flex-end;
  /* 窄窗口/小屏：按钮自动换行，不挤出视口 */
  flex-wrap: wrap;
  gap: 8px;
  row-gap: 8px;
}

.build-cards {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(260px, 1fr));
  gap: 16px;
}

.build-card {
  position: relative;
  border-radius: 8px;
  cursor: pointer;
  transition: box-shadow 0.2s, border-color 0.2s;
  border: 1px solid transparent;
}

.build-card:hover {
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.08);
}

.build-card.selected {
  border-color: #409eff;
  box-shadow: 0 0 0 1px #409eff;
}

.build-card.running {
  border-color: #a0cfff;
}

.card-header {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 8px;
}

/* 任务名输入框容器：header 行内弹性伸缩 */
.name-input-wrap {
  flex: 1;
  min-width: 0;
}

/* 目录输入框容器 */
.field-input-wrap {
  min-width: 0;
}

.card-name-input {
  width: 100%;
}

.card-name-input :deep(.el-input__inner) {
  color: var(--name-color, #606266);
  font-weight: 600;
}

.type-select {
  width: 110px;
  flex-shrink: 0;
}

.header-icon {
  font-size: 16px;
  color: #909399;
  cursor: pointer;
  transition: color 0.2s;
  flex-shrink: 0;
}

.header-icon:hover {
  color: #409eff;
}

.header-icon.danger:hover {
  color: #f56c6c;
}

/* 已开启定时构建的卡片，闹钟图标高亮 */
.header-icon.active {
  color: #e6a23c;
}

/* 状态标记 + 结果摘要行：标签/耗时/产物大小/部署耗时 */
.card-result {
  display: flex;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
  font-size: 12px;
  color: #909399;
  margin-bottom: 8px;
}

/* 压缩包大小：鼠标悬停 tooltip 展示完整路径 */
.archive-size {
  cursor: default;
}

/* 勾选了打压缩包但打包失败：红色提示（成功时为灰色大小 + tooltip 路径） */
.archive-failed {
  color: #f56c6c;
}

.card-field {
  margin-bottom: 8px;
}

/* 构建行为选项行（git pull / 自动部署 + 自动部署模版下拉） */
.card-options {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 10px;
  margin-bottom: 8px;
}

/* 卡片：自动部署模版下拉（独占整行，模版名完整显示） */
.deploy-template-select {
  width: 100%;
  flex-basis: 100%;
}

/* 部署弹窗：模版选择与操作按钮并排 */
.template-row {
  display: flex;
  align-items: center;
  gap: 8px;
  width: 100%;
}

.template-row .el-select {
  flex: 1;
}

/* 部署弹窗：密码输入与“记住密码”并排 */
.password-row {
  display: flex;
  align-items: center;
  gap: 8px;
  width: 100%;
}

.password-row .el-input {
  flex: 1;
}

.field-label {
  font-size: 12px;
  color: #606266;
  margin-bottom: 4px;
}

.output-dir-input :deep(.el-input__inner) {
  color: #409eff !important;
  font-family: 'Consolas', 'Monaco', monospace;
}

.copy-icon {
  font-size: 14px;
  color: #909399;
  cursor: pointer;
  transition: color 0.2s;
  /* suffix 区多个功能图标并排（复制/打开），图标间隔开 */
  margin-left: 6px;
}

.copy-icon:first-of-type {
  margin-left: 0;
}

.copy-icon:hover {
  color: #409eff;
}

.card-progress {
  margin-bottom: 8px;
}

/* 部署步骤提示：进度条上方小字，卡档（如 Docker 构建）时也能看出当前在干什么 */
.deploy-step-hint {
  font-size: 12px;
  color: #909399;
  line-height: 1.4;
  margin-bottom: 2px;
}

/* 卡片操作按钮行：窄卡片下自动换行，不溢出卡片 */
.card-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

/* 覆盖 element-plus 相邻按钮默认 12px 左边距：该边距在 flex 换行后仍作用于
   第二行首个按钮，导致第二行整体缩进与上一行错位（间距同时与 gap 叠加为 20px）；
   间距统一由上方 gap: 8px 负责（同 PipelineView .row-actions 先例） */
.card-actions .el-button + .el-button {
  margin-left: 0;
}

.log-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

/* 日志过滤行：类型快捷按钮 + 关键字输入（构建/部署日志共用） */
.log-filter {
  display: flex;
  align-items: center;
  gap: 8px;
}

.log-filter-input {
  width: 160px;
}

.log-filter-stat {
  font-size: 12px;
  color: #909399;
  white-space: nowrap;
}

/* 日志卡片：为“回到底部”悬浮按钮提供定位基准 */
.log-card {
  position: relative;
}

/* 用户上翻后出现，点击回到底部并恢复自动跟随 */
.scroll-bottom-btn {
  position: absolute;
  right: 28px;
  bottom: 28px;
  display: flex;
  align-items: center;
  gap: 4px;
  padding: 5px 12px;
  border: none;
  border-radius: 14px;
  background: #409eff;
  color: #fff;
  font-size: 12px;
  cursor: pointer;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.25);
  z-index: 2;
}

.scroll-bottom-btn:hover {
  background: #66b1ff;
}

/* 模板库 */
.template-tabs :deep(.el-tabs__content) {
  padding-top: 4px;
}

.deploy-tpl-name {
  margin-left: 6px;
}

.template-empty {
  color: #909399;
  font-size: 13px;
  text-align: center;
  padding: 48px 0;
}

.template-item {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 10px 12px;
  border: 1px solid #e4e7ed;
  border-radius: 6px;
  margin-bottom: 10px;
}

.template-info {
  flex: 1;
  min-width: 0;
}

.template-name {
  font-weight: 600;
  font-size: 14px;
}

.template-meta {
  font-size: 12px;
  color: #909399;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

/* 模板详情弹窗：空值/占位说明置灰 */
.tpl-detail-empty {
  color: #909399;
}

.log-terminal {
  background: #1e1e1e;
  color: #d4d4d4;
  border-radius: 6px;
  padding: 12px;
  min-height: 240px;
  max-height: 400px;
  overflow: auto;
  font-family: 'Consolas', 'Monaco', monospace;
  font-size: 12px;
  line-height: 1.5;
}

.log-terminal pre {
  margin: 0;
  white-space: pre-wrap;
  word-break: break-all;
}

.log-terminal .log-error { color: #f56c6c; }
.log-terminal .log-warn { color: #e6a23c; }
.log-terminal .log-success { color: #67c23a; }
.log-terminal .log-info { color: #409eff; }
.log-terminal .log-step { color: #61afef; font-weight: 600; }
.log-terminal .log-cmd { color: #abb2bf; background: rgba(255, 255, 255, 0.07); border-radius: 3px; padding: 0 4px; }
.log-terminal .log-cancel { color: #56b6c2; font-weight: 600; }

/* 输入框聚焦悬浮预览条：fixed 定位直挂视口（脱离卡片层叠上下文与滚动容器裁剪），
   纯展示不拦截鼠标（点击穿透到下层输入框） */
.input-preview-pop {
  position: fixed;
  z-index: 3000;
  display: flex;
  align-items: center;
  min-height: 32px;
  padding: 5px 12px;
  background: #fff;
  border: 1px solid #dcdfe6;
  border-radius: 4px;
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.18);
  pointer-events: none;
  box-sizing: border-box;
}

.input-preview-text {
  white-space: pre-wrap;
  word-break: break-all;
  font-family: 'Consolas', 'Monaco', monospace;
  font-size: 12px;
  line-height: 1.5;
  color: #303133;
}

/* 历史日志弹层：复用 log-terminal 终端样式，仅加大高度 */
.history-log-terminal {
  max-height: 560px;
}

/* 历史日志弹层：状态与时间摘要行 */
.history-log-meta {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 10px;
  font-size: 12px;
  color: #909399;
}

/* 产物清理弹窗：目录行列表 */
.artifact-list {
  display: flex;
  flex-direction: column;
  gap: 10px;
  min-height: 120px;
  max-height: 420px;
  overflow-y: auto;
}

.artifact-item {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 10px 12px;
  border: 1px solid #e4e7ed;
  border-radius: 6px;
}

.artifact-info {
  flex: 1;
  min-width: 0;
}

/* 表单验证错误信息改为占位显示，避免与字段下方提示文字重叠 */
.deploy-form :deep(.el-form-item__error) {
  position: static;
  padding-top: 2px;
}

/* 部署表单提示 */
.form-hint {
  font-size: 12px;
  color: #909399;
  line-height: 1.4;
  margin-top: 2px;
}

.site-exists-hint {
  font-size: 12px;
  line-height: 1.4;
  margin-top: 4px;
  min-height: 17px;
}

.site-exists-hint.exists {
  color: #e6a23c;
}

.site-exists-hint.new {
  color: #67c23a;
}

/* 日志标签页 */
.log-tabs {
  --el-tabs-header-height: 36px;
}

.log-tabs :deep(.el-tabs__header) {
  margin-bottom: 0;
}

.log-tabs :deep(.el-tabs__nav-wrap::after) {
  display: none;
}

.log-title {
  font-size: 13px;
  color: #606266;
}

@media (max-width: 768px) {
  .build-cards {
    grid-template-columns: 1fr;
  }

  .env-list {
    grid-template-columns: 1fr;
  }
}
</style>

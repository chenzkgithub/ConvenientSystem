<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, reactive, ref, watch } from 'vue'
import { ElMessage, ElMessageBox, ElNotification } from 'element-plus'
import {
  CircleCheck,
  CircleClose,
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
  Clock,
  Collection,
  AlarmClock,
} from '@element-plus/icons-vue'
import {
  checkUniversalEnvironment,
  startUniversalBuild,
  getUniversalBuildProgress,
  cancelUniversalBuild,
  getUniversalDefaultOutputDir,
  startDeploy,
  getDeployProgress,
  cancelDeploy,
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
} from '@/common/api/universalBuild'
import { selectFolder, openOutputFolder } from '@/common/api/universalBuild'

interface BuildCard {
  id: string
  name: string
  type: UniversalBuildType
  projectDir: string
  outputDir: string
  jobId: string | null
  status: UniversalBuildStatus | null
  progress: number
  log: string
  deployJobId: string | null
  deployStatus: DeployStatus | null
  deployLog: string
  outputDirCustom: boolean
  /** 构建产物总大小（字节，构建成功后由后端统计） */
  artifactSize: number | null
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
const envCollapse = ref<string[]>(['env'])  // 默认展开环境检测
const cards = reactive<BuildCard[]>([])
const selectedCardId = ref<string | null>(null)
const logTerminalRef = ref<HTMLDivElement | null>(null)

const selectedCard = computed(() => cards.find((c) => c.id === selectedCardId.value) ?? cards[0])

function createCard(): BuildCard {
  const id = crypto.randomUUID()
  return {
    id,
    name: `构建任务 ${cards.length + 1}`,
    type: 'Web',
    projectDir: '',
    outputDir: '',
    jobId: null,
    status: null,
    progress: 0,
    log: '',
    deployJobId: null,
    deployStatus: null,
    deployLog: '',
    outputDirCustom: false,
    artifactSize: null,
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

function getEnv(type: string) {
  return envInfo.value.find((x) => x.type === type)
}

function envStatusForCard(card: BuildCard) {
  const required = getRequiredEnvTypes(card.type)
  const missing = required
    .map((t) => getEnv(t))
    .filter((x): x is UniversalEnvironmentInfo => !!x && !x.installed)
  return { ok: missing.length === 0, missing }
}

function getRequiredEnvTypes(type: UniversalBuildType): string[] {
  switch (type) {
    case 'Web':
    case 'Node':
      return ['node', 'npm']
    case 'DotNet':
      return ['dotnet']
    case 'JavaMaven':
      return ['java', 'mvn']
    case 'JavaGradle':
      return ['java', 'gradle']
    case 'Installer':
      return ['iscc']
    default:
      return []
  }
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
  await updateDefaultOutputDir(card)
}

async function onNameChange(card: BuildCard) {
  // 卡片名称变化时，重新生成默认输出目录（末级文件夹跟名称走）
  // 手动选择了输出目录的则不覆盖
  if (!card.outputDirCustom) {
    await updateDefaultOutputDir(card)
  }
}

async function startBuild(card: BuildCard) {
  if (!card.projectDir.trim()) {
    ElMessage.warning('请选择项目目录')
    return
  }

  card.jobId = null
  card.status = 'Running'
  card.progress = 0
  card.log = ''
  card.startTime = Date.now()
  card.endTime = null
  selectedCardId.value = card.id
  ensureTick()

  try {
    const dto = await startUniversalBuild({
      type: card.type,
      projectDir: card.projectDir.trim(),
      outputDir: card.outputDir.trim(),
      name: card.name.trim(),
    })
    card.jobId = dto.id
    card.status = dto.status === 'Failed' ? 'Failed' : 'Running'
    card.outputDir = dto.outputDir
    card.log = dto.log || card.log
    startPolling()
  } catch (err: any) {
    card.status = 'Failed'
    card.log = `>> 启动失败：${err?.message || '未知错误'}`
  }
}

let pollTimer: number | null = null

function startPolling() {
  if (pollTimer) return
  pollTimer = window.setInterval(async () => {
    const runningCards = cards.filter((c) => c.status === 'Running' && c.jobId)
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
          card.artifactSize = dto.artifactSize ?? null
          if (prevStatus === 'Running' && dto.status !== 'Running') {
            card.endTime = Date.now()
            notifyDone(dto.status === 'Success' ? '构建成功' : `构建${statusText(dto.status)}`, card.name, dto.status === 'Success' ? 'success' : 'error')
            stopTickIfIdle()
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
  return card.status === 'Running'
}

function statusText(status: UniversalBuildStatus | null) {
  switch (status) {
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

/** 格式化日志为带颜色的 HTML：优先还原 ANSI 原生色，其次按结构化标记着色 */
function formatLogHtml(log: string): string {
  return log.split('\n').map(line => {
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
  // 密码不缓存：每次部署单独输入（仅记住主机/用户名等非敏感项）
  password: '',
  deployPath: '/opt/convenient',
  verifyHealth: true,
  keepDatabase: true,
})
const deployLoading = ref(false)
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
  const text = activeLogTab.value === 'build' ? card.log : card.deployLog
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
  doCheckSiteExists()
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
  doCheckSiteExists()
}

/** 压缩包名联动：站点名、服务名或目标系统变化时自动生成 {站点名}-{服务名}.{扩展名} */
watch(
  () => [deployForm.siteName, deployForm.serviceName, deployForm.targetOS],
  () => {
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
    if (newOS === oldOS) return
    deployForm.deployPath = defaultDeployPath(newOS, deployForm.siteName)
    deployForm.serviceName = defaultServiceName(deployForm.buildType, newOS)
  },
)

/** 站点信息变化时重新检查站点是否存在 */
let siteCheckTimer: number | null = null
watch(
  () => [deployForm.siteName, deployForm.serviceName, deployForm.host],
  () => {
    if (siteCheckTimer) window.clearTimeout(siteCheckTimer)
    siteCheckTimer = window.setTimeout(() => {
      if (deployDialogVisible.value) doCheckSiteExists()
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
  if (!deployForm.host.trim()) {
    ElMessage.warning('请填写服务器地址')
    return
  }
  if (!deployForm.userName.trim()) {
    ElMessage.warning('请填写 SSH 用户名')
    return
  }
  if (!deployForm.password) {
    ElMessage.warning('请填写 SSH 密码')
    return
  }

  if (!deployForm.outputDir.trim()) {
    ElMessage.warning('请选择部署目录')
    return
  }

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
    card.deployLog = dto.log || ''
    deployDialogVisible.value = false
    activeLogTab.value = 'deploy'
    selectedCardId.value = card.id
    startDeployPolling()
    ElMessage.success('部署任务已启动')
  } catch (err: any) {
    card.deployEndTime = Date.now()
    stopTickIfIdle()
    ElMessage.error(`启动部署失败：${err?.message || '未知错误'}`)
  } finally {
    deployLoading.value = false
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

/** 部署状态标签类型 */
function deployStatusType(status: DeployStatus | null) {
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
  const ready = cards.filter((c) => c.projectDir.trim() && c.status !== 'Running')
  if (ready.length === 0) {
    ElMessage.warning('没有可构建的任务（卡片需先选择项目目录）')
    return
  }
  try {
    await ElMessageBox.confirm(
      `将构建 ${ready.length} 个任务：${ready.map((c) => c.name).join('、')}`,
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

/** 保存卡片配置到本地（运行状态/日志/任务号不持久化，重开页面后从头开始） */
function persistCards() {
  const data = cards.map((c) => ({
    id: c.id,
    name: c.name,
    type: c.type,
    projectDir: c.projectDir,
    outputDir: c.outputDir,
    outputDirCustom: c.outputDirCustom,
  }))
  try {
    localStorage.setItem(CARDS_STORAGE_KEY, JSON.stringify(data))
  } catch { /* 存储不可用时静默 */ }
}

/** 恢复上次的卡片配置（仅配置项，运行态清零）；返回是否有存档 */
function restoreCards(): boolean {
  try {
    const raw = localStorage.getItem(CARDS_STORAGE_KEY)
    if (!raw) return false
    const data = JSON.parse(raw) as Partial<BuildCard>[]
    if (!Array.isArray(data) || data.length === 0) return false
    for (const item of data) {
      const card = createCard()
      if (item.id) card.id = item.id // 保留 id：定时构建按 cardId 关联
      card.name = item.name || card.name
      card.type = item.type || 'Web'
      card.projectDir = item.projectDir || ''
      card.outputDir = item.outputDir || ''
      card.outputDirCustom = !!item.outputDirCustom
      cards.push(card)
    }
    selectedCardId.value = cards[0].id
    return true
  } catch {
    return false
  }
}

// 配置字段变化时延迟保存（避免输入过程中高频写 localStorage；日志/状态变化不触发）
let persistTimer: number | null = null
watch(
  () => cards.map((c) => `${c.name}|${c.type}|${c.projectDir}|${c.outputDir}|${c.outputDirCustom}`).join('\n'),
  () => {
    if (persistTimer) window.clearTimeout(persistTimer)
    persistTimer = window.setTimeout(persistCards, 500)
  },
)

/** 记住部署连接信息（只记非敏感项：主机/用户名/站点/目标系统，密码永不持久化） */
function persistDeployRemember() {
  try {
    localStorage.setItem(DEPLOY_REMEMBER_KEY, JSON.stringify({
      host: deployForm.host,
      userName: deployForm.userName,
      siteName: deployForm.siteName,
      targetOS: deployForm.targetOS,
    }))
  } catch { /* ignore */ }
}

function restoreDeployRemember() {
  try {
    const raw = localStorage.getItem(DEPLOY_REMEMBER_KEY)
    if (!raw) return
    const d = JSON.parse(raw)
    if (d.host) deployForm.host = d.host
    if (d.userName) deployForm.userName = d.userName
    if (d.siteName) deployForm.siteName = d.siteName
    if (d.targetOS === 'Linux' || d.targetOS === 'Windows') deployForm.targetOS = d.targetOS
  } catch { /* ignore */ }
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

function loadTemplates() {
  try {
    templates.value = JSON.parse(localStorage.getItem(TEMPLATES_KEY) || '[]')
  } catch {
    templates.value = []
  }
}

function saveTemplates() {
  try {
    localStorage.setItem(TEMPLATES_KEY, JSON.stringify(templates.value))
  } catch { /* ignore */ }
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
  card.name = tpl.name
  card.type = tpl.type
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
      ? `定时构建已开启，下次触发：${formatHistoryTime(saved.nextRunAt)}`
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

/** 秒级时钟：仅在存在运行中任务时跳动（驱动耗时实时显示） */
const nowTick = ref(Date.now())
let tickTimer: number | null = null

function ensureTick() {
  if (tickTimer) return
  tickTimer = window.setInterval(() => { nowTick.value = Date.now() }, 1000)
}

function stopTickIfIdle() {
  const busy = cards.some(c => c.status === 'Running' || c.deployStatus === 'Running')
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
  const end = card.status === 'Running' ? nowTick.value : (card.endTime ?? nowTick.value)
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
    const isBuildRunning = selectedCard.value.status === 'Running'
    if (!isDeployRunning && !isBuildRunning) return
    if (!logAutoFollow.value) return
    await nextTick()
    if (logTerminalRef.value) {
      logTerminalRef.value.scrollTop = logTerminalRef.value.scrollHeight
    }
  },
)

onMounted(() => {
  loadEnvironment()
  // 恢复上次的卡片配置（没有存档时给一张默认卡片）
  if (!restoreCards()) {
    addCard()
  }
  restoreDeployRemember()
  loadTemplates()
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
  <div class="universal-build-page">
    <!-- 环境检测（默认展开） -->
    <el-collapse v-model="envCollapse" class="env-collapse">
      <el-collapse-item name="env">
        <template #title>
          <div class="env-header">
            <span class="env-title">本地构建环境</span>
            <el-button :loading="envLoading" link type="primary" @click.stop="loadEnvironment">重新检测</el-button>
          </div>
        </template>
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
      </el-collapse-item>
    </el-collapse>

    <!-- 工具栏 -->
    <div class="toolbar">
      <el-button type="primary" :icon="Plus" @click="addCard">新增构建</el-button>
      <el-button :icon="VideoPlay" @click="buildAll">全部构建</el-button>
      <el-button :icon="Collection" @click="templateVisible = true">模板库</el-button>
      <el-button :icon="Clock" @click="openHistory">部署历史</el-button>
      <el-button type="success" :icon="UploadFilled" @click="openDeployOnlyDialog">部署到服务器</el-button>
    </div>

    <!-- 构建卡片 -->
    <div class="build-cards">
      <el-card
        v-for="card in cards"
        :key="card.id"
        class="build-card"
        :class="{ selected: selectedCardId === card.id, running: card.status === 'Running' }"
        shadow="hover"
        :body-style="{ padding: '12px' }"
        @click="selectCard(card)"
      >
        <div class="card-header">
          <el-input v-model="card.name" size="small" placeholder="任务名称" class="card-name-input" :style="{ '--name-color': typeColor(card.type) }" @input="onNameChange(card)" />
          <el-select v-model="card.type" size="small" class="type-select" @change="onTypeChange(card)">
            <el-option
              v-for="opt in typeOptions"
              :key="opt.value"
              :label="opt.label"
              :value="opt.value"
            />
          </el-select>
          <el-tooltip content="存为模板" placement="top">
            <el-icon class="header-icon" @click.stop="saveCardAsTemplate(card)"><Collection /></el-icon>
          </el-tooltip>
          <el-tooltip content="定时构建" placement="top">
            <el-icon class="header-icon" :class="{ active: !!getSchedule(card) }" @click.stop="openScheduleDialog(card)"><AlarmClock /></el-icon>
          </el-tooltip>
          <el-tooltip content="删除卡片" placement="top">
            <el-icon class="header-icon danger" @click.stop="removeCard(card.id)"><Delete /></el-icon>
          </el-tooltip>
        </div>

        <div class="card-field">
          <div class="field-label">项目目录</div>
          <el-input v-model="card.projectDir" placeholder="请选择项目目录" readonly size="small">
            <template #append>
              <el-button type="primary" :icon="FolderOpened" @click.stop="pickProjectDir(card)">选择</el-button>
            </template>
          </el-input>
        </div>

        <div class="card-field">
          <div class="field-label">输出目录</div>
          <el-input v-model="card.outputDir" class="output-dir-input" placeholder="默认输出路径" readonly size="small">
            <template #append>
              <el-button :icon="FolderOpened" @click.stop="pickOutputDir(card)">选择</el-button>
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

        <div class="card-meta">
          <div class="card-env">
            <template v-if="envStatusForCard(card).ok">
              <el-tag type="success" size="small" effect="light">环境正常</el-tag>
            </template>
            <template v-else>
              <el-tag type="danger" size="small" effect="light">缺少 {{ envStatusForCard(card).missing.map(x => x.name).join('、') }}</el-tag>
            </template>
          </div>
          <div class="card-status">
            <el-tag :type="statusType(card.status)" size="small" effect="light">{{ statusText(card.status) }}</el-tag>
            <el-tag v-if="card.deployStatus" :type="deployStatusType(card.deployStatus)" size="small" effect="dark" style="margin-left: 4px;">{{ deployStatusText(card.deployStatus) }}</el-tag>
          </div>
        </div>

        <!-- 结果摘要：耗时 / 产物大小 / 部署耗时 -->
        <div v-if="buildElapsed(card) || card.artifactSize || deployElapsed(card)" class="card-result">
          <span v-if="buildElapsed(card)">构建耗时 {{ buildElapsed(card) }}</span>
          <span v-if="card.artifactSize">产物 {{ formatSize(card.artifactSize) }}</span>
          <span v-if="deployElapsed(card)">部署耗时 {{ deployElapsed(card) }}</span>
        </div>

        <div v-if="card.status === 'Running'" class="card-progress">
          <el-progress :percentage="card.progress" :stroke-width="6" :show-text="true" />
        </div>

        <div class="card-actions">
          <el-button
            type="primary"
            size="small"
            :loading="isRunning(card)"
            :icon="(card.status === 'Failed' || card.status === 'Cancelled') ? RefreshRight : undefined"
            @click.stop="startBuild(card)"
          >{{ (card.status === 'Failed' || card.status === 'Cancelled') ? '重试' : '开始构建' }}</el-button>
          <el-button
            v-if="card.deployStatus === 'Success'"
            type="success"
            size="small"
            plain
            :icon="Link"
            @click.stop="openSite(card)"
          >打开站点</el-button>
          <el-button v-if="card.status === 'Running'" type="danger" size="small" plain @click.stop="cancelBuild(card)">取消构建</el-button>
          <el-button v-if="card.deployStatus === 'Running'" type="danger" size="small" plain @click.stop="cancelDeployAction(card)">取消部署</el-button>
          <el-button
            v-if="card.status === 'Success'"
            type="warning"
            size="small"
            :loading="card.deployStatus === 'Running'"
            @click.stop="openDeployDialog(card)"
          >🚀 部署</el-button>
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
          <!-- 复制当前标签页完整日志（剥离 ANSI 转义码后的纯文本） -->
          <el-tooltip content="复制完整日志" placement="top">
            <el-button :icon="DocumentCopy" size="small" text type="primary" @click="copyLog" />
          </el-tooltip>
        </div>
      </template>
      <div ref="logTerminalRef" class="log-terminal" @scroll="onLogScroll">
        <template v-if="activeLogTab === 'build'">
          <pre v-if="selectedCard?.log" v-html="formatLogHtml(selectedCard.log)"></pre>
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
      <el-form label-width="90px" size="small">
        <el-form-item label="部署目录">
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
        <el-form-item label="站点名称">
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
        <el-form-item label="服务器地址">
          <el-input v-model="deployForm.host" placeholder="如 123.56.68.132" />
          <div class="form-hint">目标服务器的公网 IP 或域名，用于 SSH/SFTP 连接</div>
        </el-form-item>
        <el-form-item label="SSH用户名">
          <el-input v-model="deployForm.userName" placeholder="如 root" />
          <div class="form-hint">具有 SSH/SFTP 权限的登录用户，Linux 服务器通常为 root</div>
        </el-form-item>
        <el-form-item label="SSH密码">
          <el-input v-model="deployForm.password" type="password" show-password placeholder="输入 SSH 登录密码" />
          <div class="form-hint">对应 SSH 用户的登录密码，仅用于本次部署连接</div>
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

    <!-- 部署历史抽屉 -->
    <el-drawer v-model="historyVisible" title="部署历史（最近 100 条）" size="640px">
      <el-table :data="historyItems" size="small" stripe>
        <el-table-column label="时间" width="100">
          <template #default="{ row }">{{ formatHistoryTime(row.startTime) }}</template>
        </el-table-column>
        <el-table-column prop="buildName" label="任务" min-width="110" show-overflow-tooltip />
        <el-table-column prop="siteName" label="站点" width="85" show-overflow-tooltip />
        <el-table-column label="目标" width="60">
          <template #default="{ row }">{{ row.targetOS === 'Linux' ? 'Linux' : 'Win' }}</template>
        </el-table-column>
        <el-table-column label="结果" width="78">
          <template #default="{ row }">
            <el-tag :type="deployStatusType(row.status)" size="small" effect="light">{{ deployStatusText(row.status) }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="耗时" width="62">
          <template #default="{ row }">{{ formatElapsed(row.durationSeconds * 1000) }}</template>
        </el-table-column>
      </el-table>
    </el-drawer>

    <!-- 模板库抽屉 -->
    <el-drawer v-model="templateVisible" title="构建模板库" size="480px">
      <div v-if="templates.length === 0" class="template-empty">
        暂无模板。点击卡片标题栏的收藏图标，可把当前配置存为模板，下次一键新建。
      </div>
      <div v-for="tpl in templates" :key="tpl.id" class="template-item">
        <div class="template-info">
          <div class="template-name" :style="{ color: typeColor(tpl.type) }">{{ tpl.name }}</div>
          <div class="template-meta" :title="tpl.projectDir">{{ typeLabelMap[tpl.type] }} · {{ tpl.projectDir }}</div>
        </div>
        <el-button size="small" type="primary" @click="addCardFromTemplate(tpl)">新建</el-button>
        <el-button size="small" type="danger" plain :icon="Delete" @click="removeTemplate(tpl.id)" />
      </div>
    </el-drawer>

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

/* 环境检测折叠面板 */
.env-collapse {
  background: #fff;
  border-radius: 8px;
  border: 1px solid #e4e7ed;
}

.env-collapse :deep(.el-collapse-item__header) {
  padding: 0 16px;
  height: 44px;
  border-bottom: none;
}

.env-collapse :deep(.el-collapse-item__wrap) {
  padding: 0 16px 12px;
}

.env-collapse :deep(.el-collapse-item__content) {
  padding-bottom: 0;
}

.env-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  width: 100%;
  margin-bottom: 0;
}

.env-title {
  font-weight: 600;
  font-size: 15px;
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
}

.build-cards {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(260px, 1fr));
  gap: 16px;
}

.build-card {
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

.card-name-input {
  flex: 1;
  min-width: 0;
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

/* 结果摘要行：耗时/产物大小/部署耗时 */
.card-result {
  display: flex;
  gap: 12px;
  flex-wrap: wrap;
  font-size: 12px;
  color: #909399;
  margin-bottom: 8px;
}

.card-field {
  margin-bottom: 8px;
}

.card-meta {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  margin-bottom: 8px;
}

.card-env,
.card-status {
  flex-shrink: 0;
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

.card-env {
  margin-bottom: 12px;
}

.card-progress {
  margin-bottom: 8px;
}

.card-actions {
  display: flex;
  gap: 8px;
}

.log-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
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

<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, reactive, ref, watch } from 'vue'
import { ElMessage, ElMessageBox, ElNotification } from 'element-plus'
import {
  VideoPlay,
  Plus,
  Delete,
  ArrowUp,
  ArrowDown,
  ArrowRight,
  Clock,
  Folder,
  Document,
  Edit,
  Refresh,
  Loading,
  CircleCheckFilled,
  CircleCloseFilled,
  DocumentCopy,
  Link,
  FolderOpened,
} from '@element-plus/icons-vue'
import {
  getPipelineList,
  savePipeline,
  removePipeline,
  startPipelineRun,
  getPipelineRun,
  getPipelineRuns,
  cancelPipelineRun,
  testPipelineConnection,
  type PipelineDefinition,
  type PipelineStage,
  type PipelineStageType,
  type PipelineRun,
  type PipelineRunStatus,
  type PipelineStageRunStatus,
  type PipelineConnectionTestResult,
} from '@/common/api/pipeline'
import { selectFolder, selectSqlFile, getUniversalDefaultOutputDir, type UniversalBuildType, type DeployTargetOS } from '@/common/api/universalBuild'
import { notifyBuildComplete } from '@/common/api/notice'

// ============================ 常量 ============================

const buildTypeOptions: { label: string; value: UniversalBuildType }[] = [
  { label: 'Web 前端', value: 'Web' },
  { label: 'Node 项目', value: 'Node' },
  { label: 'C# (.NET)', value: 'DotNet' },
  { label: 'Java Maven', value: 'JavaMaven' },
  { label: 'Java Gradle', value: 'JavaGradle' },
  { label: '安装包打包', value: 'Installer' },
]

const stageTypeLabel: Record<PipelineStageType, string> = { Build: '构建', Deploy: '部署', Sql: '数据库' }

/** 数据库脚本阶段支持的目标库类型（与后端 FreeSql.DataType 对应） */
const dbTypeOptions = ['SqlServer', 'MySql', 'PostgreSQL', 'Sqlite', 'Oracle']

function runStatusText(status: PipelineRunStatus): string {
  return ({ Running: '运行中', Success: '成功', Failed: '失败', Cancelled: '已取消' } as const)[status]
}
function runStatusTagType(status: PipelineRunStatus): 'success' | 'danger' | 'info' | 'primary' {
  return ({ Running: 'primary', Success: 'success', Failed: 'danger', Cancelled: 'info' } as const)[status]
}
function stageStatusText(status: PipelineStageRunStatus): string {
  return ({ Pending: '等待', Running: '运行中', Success: '成功', Failed: '失败', Skipped: '跳过' } as const)[status]
}
function stageStatusClass(status?: PipelineStageRunStatus): string {
  return status ? `is-${status.toLowerCase()}` : 'is-pending'
}

/** 阶段连线状态 = 箭头指向节点的状态（线色表达“流到这一步的结果”） */
function arrowClass(nextRunStatus?: PipelineStageRunStatus): string {
  if (nextRunStatus === 'Running') return 'is-active'
  if (nextRunStatus === 'Success') return 'is-done'
  if (nextRunStatus === 'Failed') return 'is-failed'
  return 'is-idle'
}

// ============================ 列表 ============================

const pipelines = ref<PipelineDefinition[]>([])
const listLoading = ref(false)
/** 每条流水线最近一次运行（列表"最近运行"列 + 运行中判断） */
const recentRuns = ref<Record<string, PipelineRun>>({})

function recentRunOf(p: PipelineDefinition): PipelineRun | undefined {
  return recentRuns.value[p.id ?? '']
}

function isPipelineRunning(p: PipelineDefinition): boolean {
  return recentRunOf(p)?.status === 'Running'
}

/** 拉取列表 + 最近运行（并发） */
async function loadAll() {
  listLoading.value = true
  try {
    const [list, runs] = await Promise.all([
      getPipelineList(),
      getPipelineRuns(undefined, 100),
    ])
    pipelines.value = list
    const map: Record<string, PipelineRun> = {}
    for (const run of runs) {
      if (run.pipelineId && !(run.pipelineId in map)) map[run.pipelineId] = run
    }
    recentRuns.value = map
    // 页面加载/刷新时接管运行中的流水线（含定时触发的运行，结束后能收到完成通知）
    for (const run of Object.values(map)) watchRun(run)
  } catch (e: any) {
    ElMessage.error(`加载流水线列表失败：${e?.message || e}`)
  } finally {
    listLoading.value = false
  }
}

/** 列表"阶段"列：徽标显示步骤名称（空则回退子类型标签），颜色按阶段类型区分，悬停显示"名称 · 子类型" */
function stageBadges(p: PipelineDefinition): { type: PipelineStageType; label: string; title: string }[] {
  return p.stages.map((s) => {
    const sub = stageSubLabel(s)
    const name = s.name?.trim()
    return {
      type: s.type,
      label: name || sub,
      title: `${name || stageTypeLabel[s.type]} · ${sub}`,
    }
  })
}

/** 运行中流水线当前正在执行的阶段信息（含进度与步骤文本，阶段类型决定进度条颜色） */
function runningStageInfo(p: PipelineDefinition): { current: number; total: number; name: string; percent?: number; stepText?: string; stageType?: PipelineStageType } | null {
  const run = recentRunOf(p)
  if (!run || run.status !== 'Running' || !run.stages?.length) return null
  const idx = run.stages.findIndex((s) => s.status === 'Running')
  const stage = run.stages[idx]
  if (!stage) return null
  return {
    current: idx + 1,
    total: run.stages.length,
    name: stage.name,
    percent: stage.progress,
    stepText: stage.stepText,
    stageType: p.stages[idx]?.type,
  }
}

/** 阶段左侧导航里的子类型标签 */
function stageSubLabel(stage: PipelineStage): string {
  if (stage.type === 'Build') {
    return buildTypeOptions.find((o) => o.value === stage.buildType)?.label ?? stage.buildType ?? '构建'
  }
  if (stage.type === 'Deploy') {
    return stage.targetOS ?? '部署'
  }
  return stage.dbType ?? '数据库'
}

async function onDeletePipeline(p: PipelineDefinition) {
  if (isPipelineRunning(p)) {
    ElMessage.warning(`流水线【${p.name}】正在运行，请等待结束后再删除`)
    return
  }
  try {
    await ElMessageBox.confirm(`确定删除流水线【${p.name}】？运行历史保留，定义不可恢复`, '删除流水线', { type: 'warning' })
  } catch { return }
  try {
    await removePipeline(p.id ?? '')
    ElMessage.success('已删除')
    await loadAll()
  } catch (e: any) {
    ElMessage.error(`删除失败：${e?.message || e}`)
  }
}

// ============================ 新增/编辑弹窗 ============================

const editVisible = ref(false)
const saving = ref(false)
/** 编辑副本（与列表对象隔离，取消不污染原数据） */
const editing = reactive({
  id: '',
  name: '',
  stages: [] as PipelineStage[],
})

function createStage(type: PipelineStageType): PipelineStage {
  return {
    id: Math.random().toString(36).slice(2, 10),
    name: stageTypeLabel[type],
    type,
    buildType: 'Web',
    projectDir: '',
    outputDir: '',
    outputDirCustom: false,
    prePull: true,
    explicitOutputDir: '',
    deployBuildType: 'Web',
    serviceName: '',
    remoteDir: '',
    archiveName: '',
    targetOS: 'Linux',
    siteName: 'convenient',
    host: '',
    userName: 'root',
    deployPath: '',
    verifyHealth: true,
    keepDatabase: true,
    sqlSource: '',
    dbType: 'SqlServer',
    connectionString: '',
    useTransaction: false,
  }
}

function openCreate() {
  editing.id = ''
  editing.name = '新流水线'
  editing.stages = [createStage('Build')]
  activeStageIndex.value = 0
  activeStage.value = editing.stages[0]
  editVisible.value = true
}

function openEdit(p: PipelineDefinition) {
  if (isPipelineRunning(p)) {
    ElMessage.warning(`流水线【${p.name}】正在运行，请等待结束后再编辑`)
    return
  }
  editing.id = p.id ?? ''
  editing.name = p.name
  editing.stages = p.stages.map((s) => ({ ...s }))
  activeStageIndex.value = 0
  activeStage.value = editing.stages[0] ?? null
  editVisible.value = true
}

/** 编辑中的流水线正在运行（禁用阶段表单） */
const editingRunning = computed(() => {
  if (!editing.id) return false
  return recentRuns.value[editing.id]?.status === 'Running'
})

/** 未保存提示：与列表中的定义比对 */
const dirty = computed(() => {
  const p = pipelines.value.find((x) => x.id === editing.id)
  if (!p) return true
  if (p.name !== editing.name) return true
  if (p.stages.length !== editing.stages.length) return true
  return JSON.stringify(p.stages) !== JSON.stringify(editing.stages)
})

// ============================ 阶段编辑 ============================

function addStage(type: PipelineStageType) {
  editing.stages.push(createStage(type))
}

function addStageAndFocus(type: PipelineStageType) {
  addStage(type)
  activeStageIndex.value = editing.stages.length - 1
  activeStage.value = editing.stages[activeStageIndex.value]
}

function moveStage(index: number, dir: -1 | 1) {
  const target = index + dir
  if (target < 0 || target >= editing.stages.length) return
  const [item] = editing.stages.splice(index, 1)
  editing.stages.splice(target, 0, item)
  // 如果移动的是当前选中阶段，同步索引与引用
  if (activeStageIndex.value === index) {
    activeStageIndex.value = target
  } else if (activeStageIndex.value === target) {
    activeStageIndex.value = index
  }
  activeStage.value = editing.stages[activeStageIndex.value] ?? null
}

async function pickStageDir(stage: PipelineStage, field: 'projectDir' | 'outputDir' | 'explicitOutputDir' | 'sqlSource') {
  try {
    // 输入框已有路径时从该位置打开（后端 ResolveInitialDir：目录直用、文件取父目录）
    const dir = await selectFolder(stage[field])
    if (dir) {
      stage[field] = dir
      if (field === 'outputDir') stage.outputDirCustom = true
    }
  } catch { /* 用户取消 */ }
}

/** 选择单个 SQL 脚本文件填入 sqlSource */
async function pickSqlFile(stage: PipelineStage) {
  try {
    const file = await selectSqlFile(stage.sqlSource)
    if (file) stage.sqlSource = file
  } catch { /* 用户取消 */ }
}

/** 选择 SQL 目录（sqlSource 支持目录：执行其中全部 .sql、按文件名排序） */
async function pickSqlFolder(stage: PipelineStage) {
  try {
    const dir = await selectFolder(stage.sqlSource)
    if (dir) stage.sqlSource = dir
  } catch { /* 用户取消 */ }
}

/** 编辑弹窗当前选中的阶段索引 */
const activeStageIndex = ref(0)
/** 当前选中阶段的 ref（与 editing.stages[index] 保持同步，避免 computed 对象属性写入不可靠导致表单改不动） */
const activeStage = ref<PipelineStage | null>(null)

watch(
  () => activeStageIndex.value,
  (index) => {
    activeStage.value = editing.stages[index] ?? null
  },
  { immediate: true },
)

/** 测试连接进行中（按钮 loading） */
const testingConn = ref(false)
/** 测试连接结果（内联常驻；切换阶段或改动连接串即失效清空，避免结果与配置对不上） */
const connTestResult = ref<PipelineConnectionTestResult | null>(null)

/** 测试数据库连接：不落配置、不执行脚本，结果内联展示 */
async function onTestConnection(stage: PipelineStage) {
  const connectionString = stage.connectionString?.trim()
  if (!connectionString) return
  testingConn.value = true
  connTestResult.value = null
  try {
    connTestResult.value = await testPipelineConnection({ dbType: stage.dbType || 'SqlServer', connectionString })
  } catch (e) {
    connTestResult.value = { success: false, message: e instanceof Error ? e.message : '请求异常', serverVersion: '', elapsedMs: 0 }
  } finally {
    testingConn.value = false
  }
}

// 切换阶段或改动连接串后旧结果立即失效（清空）
watch([activeStageIndex, () => activeStage.value?.connectionString], () => {
  connTestResult.value = null
})

/** 当前编辑阶段之前是否存在构建阶段：部署类型/服务名/远程目录默认值自动跟随最近构建产物 */
const hasBuildBeforeActive = computed(() =>
  editing.stages.slice(0, activeStageIndex.value).some((s) => s.type === 'Build'))

/** 当前编辑阶段之前最近构建阶段的类型（跟随提示展示用） */
const precedingBuildType = computed<UniversalBuildType | null>(() => {
  for (let i = activeStageIndex.value - 1; i >= 0; i--) {
    const s = editing.stages[i]
    if (s.type === 'Build') return s.buildType ?? 'Web'
  }
  return null
})

/** 构建类型的中文标签（跟随提示展示用） */
function buildTypeLabel(t: UniversalBuildType | null): string {
  return buildTypeOptions.find((o) => o.value === t)?.label ?? t ?? ''
}

/** Build 阶段输出目录未手动指定时，根据阶段名和构建类型自动生成默认输出路径 */
watch(
  () => activeStage.value && activeStage.value.type === 'Build'
    ? {
        name: activeStage.value.name,
        buildType: activeStage.value.buildType ?? 'Web',
        outputDirCustom: activeStage.value.outputDirCustom ?? false,
      }
    : null,
  async (cur, prev) => {
    if (!cur || cur.outputDirCustom) return
    if (prev && cur.name === prev.name && cur.buildType === prev.buildType) return
    try {
      const dir = await getUniversalDefaultOutputDir({ type: cur.buildType, name: cur.name })
      const stage = activeStage.value
      if (stage && stage.type === 'Build' && !(stage.outputDirCustom ?? false)) {
        stage.outputDir = dir
      }
    } catch { /* 生成失败则保持空，由后端自动推断 */ }
  },
  { immediate: true },
)

/** 阶段类型切换时，若名称仍是默认标签则同步更新 */
watch(
  () => activeStage.value?.type,
  (newType, oldType) => {
    if (!activeStage.value || !newType || !oldType) return
    if (activeStage.value.name === stageTypeLabel[oldType]) {
      activeStage.value.name = stageTypeLabel[newType]
    }
  },
)

/** 删除阶段并自动切换到相邻阶段 */
function removeStageWithSwitch(index: number) {
  editing.stages.splice(index, 1)
  if (activeStageIndex.value >= editing.stages.length) {
    activeStageIndex.value = Math.max(0, editing.stages.length - 1)
  }
  activeStage.value = editing.stages[activeStageIndex.value] ?? null
}

async function onSave() {
  if (!editing.name.trim()) {
    ElMessage.warning('流水线名称不能为空')
    return
  }
  if (editing.stages.length === 0) {
    ElMessage.warning('至少配置一个阶段')
    return
  }
  for (let i = 0; i < editing.stages.length; i++) {
    const s = editing.stages[i]
    if (!s.name.trim()) {
      activeStageIndex.value = i
      ElMessage.warning('阶段名称不能为空')
      return
    }
    if (s.type === 'Build' && !s.projectDir?.trim()) {
      activeStageIndex.value = i
      ElMessage.warning(`阶段【${s.name}】未配置项目目录`)
      return
    }
    if (s.type === 'Deploy' && !s.host?.trim()) {
      activeStageIndex.value = i
      ElMessage.warning(`阶段【${s.name}】未配置服务器地址`)
      return
    }
    if (s.type === 'Sql' && !s.connectionString?.trim()) {
      activeStageIndex.value = i
      ElMessage.warning(`阶段【${s.name}】未配置数据库连接串`)
      return
    }
  }
  saving.value = true
  try {
    await savePipeline({
      id: editing.id || undefined,
      name: editing.name.trim(),
      stages: editing.stages,
    })
    ElMessage.success('已保存')
    editVisible.value = false
    await loadAll()
  } catch (e: any) {
    ElMessage.error(`保存失败：${e?.message || e}`)
  } finally {
    saving.value = false
  }
}

// ============================ 执行弹窗（运行视图，无配置） ============================

const runVisible = ref(false)
const currentRun = ref<PipelineRun | null>(null)
const running = computed(() => currentRun.value?.status === 'Running')

let pollTimer: number | null = null

function startPolling() {
  stopPolling()
  pollTimer = window.setInterval(async () => {
    const id = currentRun.value?.id
    if (!id) return stopPolling()
    try {
      const run = await getPipelineRun(id, { silent: true })
      if (run) currentRun.value = run
      if (run && run.status !== 'Running') {
        stopPolling()
        // 运行结束：刷新列表"最近运行"列（完成通知由视图级运行监听统一发送，弹窗开不开都能收到且不重复）
        void refreshRecentRuns()
      }
    } catch { /* 网络抖动静默，下次轮询重试 */ }
  }, 1000)
}

function stopPolling() {
  if (pollTimer != null) {
    window.clearInterval(pollTimer)
    pollTimer = null
  }
}

/** 只刷新最近运行映射（运行结束回写列表状态用） */
async function refreshRecentRuns() {
  try {
    const runs = await getPipelineRuns(undefined, 100, { silent: true })
    const map: Record<string, PipelineRun> = {}
    for (const run of runs) {
      if (run.pipelineId && !(run.pipelineId in map)) map[run.pipelineId] = run
    }
    recentRuns.value = map
  } catch { /* 静默 */ }
}

/** 列表执行图标：未运行 → 启动并打开弹窗；运行中 → 打开弹窗接管查看 */
async function onExecute(p: PipelineDefinition) {
  if (!p.id) return
  const recent = recentRunOf(p)
  if (recent?.status === 'Running') {
    currentRun.value = recent
    runVisible.value = true
    startPolling()
    watchRun(recent)
    await nextTick()
    scrollToLogBottom()
    return
  }
  try {
    const run = await startPipelineRun(p.id)
    currentRun.value = run
    recentRuns.value = { ...recentRuns.value, [p.id]: run }
    runVisible.value = true
    startPolling()
    watchRun(run)
    ElMessage.success(`流水线【${run.pipelineName}】已启动`)
    await nextTick()
    scrollToLogBottom()
  } catch (e: any) {
    ElMessage.error(`启动失败：${e?.message || e}`)
  }
}

/** 查看某流水线最近一次运行（列表时钟图标） */
async function onViewRun(p: PipelineDefinition) {
  const recent = recentRunOf(p)
  if (!recent) {
    ElMessage.info(`流水线【${p.name}】还没有运行记录`)
    return
  }
  // 运行记录重启后日志不可查，优先取内存中的实时数据
  if (recent.status === 'Running') {
    currentRun.value = recent
    runVisible.value = true
    startPolling()
  } else {
    try {
      const run = await getPipelineRun(recent.id)
      currentRun.value = run ?? recent
    } catch {
      currentRun.value = recent
    }
    runVisible.value = true
    stopPolling()
  }
  await nextTick()
  scrollToLogBottom()
}

async function onCancelRun() {
  if (!currentRun.value) return
  try {
    await ElMessageBox.confirm('确定取消当前运行？部署阶段取消会自动还原部署前环境', '取消运行', { type: 'warning' })
  } catch { return }
  try {
    await cancelPipelineRun(currentRun.value.id)
    ElMessage.success('已发送取消请求')
  } catch (e: any) {
    ElMessage.error(`取消失败：${e?.message || e}`)
  }
}

// ============================ 流水线图数据（执行弹窗内） ============================

/**
 * 阶段节点：以运行记录的 stages 为主序（含状态/耗时），
 * 关联到流水线定义时补充类型徽标，找不到定义也能显示。
 */
interface StageNode {
  name: string
  type?: PipelineStageType
  run?: PipelineRunStageLite
}
interface PipelineRunStageLite {
  status: PipelineStageRunStatus
  message?: string
  startTime?: string
  completedTime?: string
  progress?: number
  stepText?: string
}

const diagramStages = computed<StageNode[]>(() => {
  const run = currentRun.value
  if (!run) return []
  const def = pipelines.value.find((p) => p.id === run.pipelineId)
  const typeById = new Map((def?.stages ?? []).map((s) => [s.id, s.type]))
  return run.stages.map((s) => ({
    name: s.name,
    type: typeById.get(s.stageId),
    run: { status: s.status, message: s.message, startTime: s.startTime, completedTime: s.completedTime, progress: s.progress, stepText: s.stepText },
  }))
})

// ============================ 历史 ============================

const historyVisible = ref(false)
const historyLoading = ref(false)
const historyItems = ref<PipelineRun[]>([])
/** 历史弹窗按哪个流水线过滤（空 = 全部；正常入口都会带当前流水线，空仅兜底） */
const historyPipelineId = ref('')

/** 打开运行历史弹窗：只看传入流水线的历史（列表行与运行详情弹窗都传当前流水线，不再默认带出所有流水线） */
async function openHistory(pipelineId?: string) {
  historyPipelineId.value = pipelineId ?? ''
  historyVisible.value = true
  historyLoading.value = true
  try {
    historyItems.value = await getPipelineRuns(historyPipelineId.value || undefined, 50)
  } catch (e: any) {
    ElMessage.error(`加载运行历史失败：${e?.message || e}`)
  } finally {
    historyLoading.value = false
  }
}

function viewHistoryRun(run: PipelineRun) {
  currentRun.value = run
  historyVisible.value = false
  runVisible.value = true
  if (run.status === 'Running') startPolling()
  else stopPolling()
  nextTick(() => scrollToLogBottom())
}

// ============================ 时间与格式化 ============================

const nowTick = ref(Date.now())
let tickTimer: number | null = null

function parseTime(v?: string): number | null {
  if (!v) return null
  const t = Date.parse(v)
  return Number.isNaN(t) ? null : t
}

function formatDateTime(v?: string): string {
  const t = parseTime(v)
  return t == null ? '' : new Date(t).toLocaleString('zh-CN', { hour12: false })
}

/** 时刻文本（HH:mm:ss） */
function formatClock(v?: string): string {
  const t = parseTime(v)
  return t == null ? '' : new Date(t).toLocaleTimeString('zh-CN', { hour12: false })
}

/** 起止区间：运行中显示"开始时刻 → 运行中"；跨天时两端补日期（如 9/9 22:10 → 9/10 00:03） */
function formatRunRange(run: PipelineRun): string {
  const start = parseTime(run.startTime)
  if (start == null) return ''
  const s = new Date(start)
  const startText = `${s.getMonth() + 1}/${s.getDate()} ${formatClock(run.startTime)}`
  if (run.status === 'Running') return `${startText} → 运行中`
  const end = parseTime(run.completedTime)
  if (end == null) return startText
  const e = new Date(end)
  const sameDay = s.getFullYear() === e.getFullYear() && s.getMonth() === e.getMonth() && s.getDate() === e.getDate()
  const endText = `${sameDay ? '' : `${e.getMonth() + 1}/${e.getDate()} `}${formatClock(run.completedTime)}`
  return `${startText} → ${endText}`
}

function formatDuration(ms: number): string {
  if (ms < 0) ms = 0
  const s = Math.floor(ms / 1000)
  if (s < 60) return `${s}s`
  if (s < 3600) return `${Math.floor(s / 60)}m${String(s % 60).padStart(2, '0')}s`
  return `${Math.floor(s / 3600)}h${String(Math.floor((s % 3600) / 60)).padStart(2, '0')}m${String(s % 60).padStart(2, '0')}s`
}

function runElapsedText(run: PipelineRun): string {
  const start = parseTime(run.startTime)
  if (start == null) return ''
  const end = run.status === 'Running' ? nowTick.value : (parseTime(run.completedTime) ?? nowTick.value)
  return formatDuration(end - start)
}

function stageElapsedText(node: StageNode): string {
  const start = parseTime(node.run?.startTime)
  if (start == null) return ''
  const end = node.run?.status === 'Running' ? nowTick.value : (parseTime(node.run?.completedTime) ?? nowTick.value)
  return formatDuration(end - start)
}

/** 阶段节点状态行文本：开始时刻 · 耗时（运行中耗时跳动） */
function stageClockText(node: StageNode): string {
  const clock = formatClock(node.run?.startTime)
  const elapsed = stageElapsedText(node)
  if (clock && elapsed) return `${clock} · ${elapsed}`
  return clock || elapsed
}

/** 节点悬浮详情：名称 + 起止时刻 + 耗时 + 消息 */
function stageNodeTitle(node: StageNode): string {
  const lines = [node.name]
  if (node.run?.startTime) lines.push(`开始 ${formatDateTime(node.run.startTime)}`)
  if (node.run?.completedTime) lines.push(`结束 ${formatDateTime(node.run.completedTime)}`)
  const elapsed = stageElapsedText(node)
  if (elapsed) lines.push(`耗时 ${elapsed}`)
  if (node.run?.message) lines.push(node.run.message)
  return lines.join('\n')
}

// ============================ 通知 ============================

function notifyDone(title: string, message: string, type: 'success' | 'error') {
  ElNotification({ title, message, type, duration: 5000 })
  if (document.hidden && 'Notification' in window && Notification.permission === 'granted') {
    try { new Notification(title, { body: message }) } catch { /* WebView2 不支持时静默 */ }
  }
  // 同步创建系统通知：右上角弹卡片（NoticeAlert，level=2）+ 铃铛未读，离线/其他页面也能看到
  void notifyBuildComplete(title, message)
}

// ============================ 运行监听（视图级完成通知） ============================

/** 被监视的运行：runId → 流水线名（进入终态发完通知后移除） */
const watchedRuns = new Map<string, string>()
let watchTimer: number | null = null

/** 监视一个运行中的流水线：结束后发完成通知（执行弹窗关闭也不受影响） */
function watchRun(run: PipelineRun) {
  if (run.status !== 'Running') return
  watchedRuns.set(run.id, run.pipelineName)
  startWatchTimer()
}

function startWatchTimer() {
  if (watchTimer != null) return
  watchTimer = window.setInterval(async () => {
    if (watchedRuns.size === 0) return stopWatchTimer()
    try {
      const runs = await getPipelineRuns(undefined, 100, { silent: true })
      // 刷新列表“最近运行”列（弹窗轮询停止后列表状态由这里兜底刷新）
      const map: Record<string, PipelineRun> = {}
      for (const run of runs) {
        if (run.pipelineId && !(run.pipelineId in map)) map[run.pipelineId] = run
      }
      recentRuns.value = map
      // 同时接管列表中新出现的运行中流水线（如定时触发的运行），结束后同样能收到通知
      for (const run of runs) {
        if (run.status === 'Running') watchedRuns.set(run.id, run.pipelineName)
      }
      // 翻转检测：被监视的运行进入终态 → 发通知（快照独立于 recentRuns，弹窗轮询刷新列表不影响）
      for (const [runId, pipelineName] of [...watchedRuns.entries()]) {
        const run = runs.find((r) => r.id === runId)
        if (!run || run.status === 'Running') continue
        watchedRuns.delete(runId)
        const ok = run.status === 'Success'
        notifyDone(
          ok ? '流水线运行成功' : `流水线${runStatusText(run.status)}`,
          `${pipelineName} · ${formatClock(run.completedTime) || formatClock(run.startTime)} 结束 · 总耗时 ${runElapsedText(run)}`,
          ok ? 'success' : 'error',
        )
      }
      if (watchedRuns.size === 0) stopWatchTimer()
    } catch { /* 网络抖动静默，下次轮询重试 */ }
  }, 3000)
}

function stopWatchTimer() {
  if (watchTimer != null) {
    window.clearInterval(watchTimer)
    watchTimer = null
  }
}

// ============================ 日志滚动 ============================

const logTerminalRef = ref<HTMLElement | null>(null)
const logAutoFollow = ref(true)

function onLogScroll() {
  const el = logTerminalRef.value
  if (!el) return
  logAutoFollow.value = el.scrollHeight - el.scrollTop - el.clientHeight < 40
}

function scrollToLogBottom() {
  logAutoFollow.value = true
  const el = logTerminalRef.value
  if (el) el.scrollTop = el.scrollHeight
}

/** 复制日志到剪贴板 */
async function copyLog() {
  const log = currentRun.value?.log
  if (!log) {
    ElMessage.warning('无日志可复制')
    return
  }
  try {
    await navigator.clipboard.writeText(log)
    ElMessage.success('日志已复制到剪贴板')
  } catch {
    // 降级方案：创建 textarea 复制
    const textarea = document.createElement('textarea')
    textarea.value = log
    textarea.style.position = 'fixed'
    textarea.style.opacity = '0'
    document.body.appendChild(textarea)
    textarea.select()
    try {
      document.execCommand('copy')
      ElMessage.success('日志已复制到剪贴板')
    } catch {
      ElMessage.error('复制失败，请手动选择复制')
    }
    document.body.removeChild(textarea)
  }
}

// 日志变化时跟随滚动到底部
watch(
  () => currentRun.value?.log?.length,
  async () => {
    if (!logAutoFollow.value) return
    await nextTick()
    const el = logTerminalRef.value
    if (el) el.scrollTop = el.scrollHeight
  },
)

// ============================ 生命周期 ============================

onMounted(async () => {
  tickTimer = window.setInterval(() => (nowTick.value = Date.now()), 1000)
  await loadAll()
})

onUnmounted(() => {
  stopPolling()
  stopWatchTimer()
  // 监视快照随页面销毁一并清空（切回页面时 loadAll 会重新接管仍在运行的流水线）
  watchedRuns.clear()
  if (tickTimer != null) window.clearInterval(tickTimer)
})
</script>

<template>
  <div class="pipeline-view">
    <!-- 工具栏 -->
    <div class="toolbar">
      <el-button v-if="$has('pipeline:add')" type="primary" :icon="Plus" @click="openCreate">新建流水线</el-button>
      <el-button :icon="Refresh" :loading="listLoading" @click="loadAll">刷新</el-button>
      <span class="toolbar-tip">流水线定义保存在本机 exe 目录 pipelines.json，重装不丢失</span>
    </div>

    <!-- 流水线列表：卡片网格（每条流水线一张卡，与构建页风格统一） -->
    <div class="pipeline-grid" v-loading="listLoading">
      <div
        v-for="p in pipelines"
        :key="p.id"
        class="pipeline-card"
        :class="{ 'is-running': isPipelineRunning(p) }"
      >
        <div class="pipe-head">
          <span class="pipe-name">{{ p.name }}</span>
          <el-icon v-if="isPipelineRunning(p)" class="spin run-spin"><Loading /></el-icon>
          <el-tag v-if="recentRunOf(p)" class="pipe-status" :type="runStatusTagType(recentRunOf(p)!.status)" size="small" :effect="recentRunOf(p)!.status === 'Running' ? 'dark' : 'light'">
            {{ runStatusText(recentRunOf(p)!.status) }}
          </el-tag>
          <span v-else class="run-none">从未运行</span>
        </div>

        <div v-if="p.stages.length" class="pipe-flow">
          <template v-for="(t, i) in stageBadges(p)" :key="i">
            <span class="type-badge" :class="`type-${t.type.toLowerCase()}`" :title="t.title">{{ t.label }}</span>
            <el-icon v-if="i < stageBadges(p).length - 1" class="flow-arrow"><ArrowRight /></el-icon>
          </template>
        </div>

        <div v-if="runningStageInfo(p)" class="pipe-running-stage">
          <el-icon class="spin"><Loading /></el-icon>
          <span>阶段 {{ runningStageInfo(p)!.current }}/{{ runningStageInfo(p)!.total }} · {{ runningStageInfo(p)!.name }} · {{ runningStageInfo(p)!.percent ?? 0 }}%</span>
        </div>

        <!-- 运行中卡片：当前阶段进度条（构建=蓝 / 部署、数据库=橙，与通用构建页一致） -->
        <div v-if="runningStageInfo(p)" class="pipe-run-progress">
          <el-progress
            :percentage="Math.min(runningStageInfo(p)!.percent ?? 0, 100)"
            :stroke-width="5"
            :show-text="false"
            :status="runningStageInfo(p)!.stageType === 'Build' ? undefined : 'warning'"
          />
        </div>

        <div class="pipe-meta">
          <span v-if="recentRunOf(p)" class="pipe-recent" :title="`${formatRunRange(recentRunOf(p)!)} · ${runElapsedText(recentRunOf(p)!)}`">{{ formatRunRange(recentRunOf(p)!) }} · {{ runElapsedText(recentRunOf(p)!) }}</span>
          <span class="pipe-update" :title="formatDateTime(p.updateTime)">更新 {{ formatDateTime(p.updateTime) }}</span>
        </div>

        <div class="pipe-actions">
          <el-tooltip :content="isPipelineRunning(p) ? '查看运行' : '执行'" placement="top">
            <el-button
              v-if="$has('pipeline:run')"
              size="small"
              circle
              :type="isPipelineRunning(p) ? 'warning' : 'success'"
              :icon="VideoPlay"
              @click="onExecute(p)"
            />
          </el-tooltip>
          <el-tooltip content="最近运行详情" placement="top">
            <el-button size="small" circle :icon="Clock" :disabled="!recentRunOf(p)" @click="onViewRun(p)" />
          </el-tooltip>
          <el-tooltip content="运行历史" placement="top">
            <el-button size="small" circle :icon="Document" @click="openHistory(p.id)" />
          </el-tooltip>
          <el-tooltip content="编辑" placement="top">
            <el-button
              v-if="$has('pipeline:edit')"
              size="small"
              circle
              type="primary"
              :icon="Edit"
              :disabled="isPipelineRunning(p)"
              @click="openEdit(p)"
            />
          </el-tooltip>
          <el-tooltip content="删除" placement="top">
            <el-button
              v-if="$has('pipeline:delete')"
              size="small"
              circle
              type="danger"
              :icon="Delete"
              :disabled="isPipelineRunning(p)"
              @click="onDeletePipeline(p)"
            />
          </el-tooltip>
        </div>
      </div>

      <div v-if="!listLoading && pipelines.length === 0" class="grid-empty">
        <el-empty description="暂无流水线，点击上方「新建流水线」创建" :image-size="70" />
      </div>
    </div>

    <!-- 新增/编辑流水线弹窗：名称 + 阶段配置 -->
    <el-dialog
      v-model="editVisible"
      :title="editing.id ? `编辑流水线 - ${editing.name}` : '新增流水线'"
      width="920px"
      top="4vh"
      :close-on-click-modal="false"
    >
      <div class="edit-head">
        <el-input v-model="editing.name" class="name-input" placeholder="流水线名称" maxlength="50" />
        <span v-if="dirty" class="dirty-tip">● 有未保存修改</span>
      </div>

      <div class="edit-body">
        <!-- 左栏：阶段导航 -->
        <div class="edit-nav">
          <div class="nav-list">
            <div
              v-for="(stage, i) in editing.stages"
              :key="stage.id"
              class="nav-item"
              :class="[
                `is-${stage.type.toLowerCase()}`,
                { 'is-active': activeStageIndex === i },
              ]"
              @click="activeStageIndex = i"
            >
              <span class="nav-index">{{ i + 1 }}</span>
              <span class="nav-type-dot" />
              <div class="nav-info">
                <span class="nav-name">{{ stage.name || '未命名' }}</span>
                <span class="nav-label">{{ stageSubLabel(stage) }}</span>
              </div>
            </div>
          </div>
          <!-- 左栏底部：添加阶段 -->
          <div v-if="!editingRunning" class="add-stage-group">
            <el-button plain :icon="Plus" @click="addStageAndFocus('Build')">构建</el-button>
            <el-button plain :icon="Plus" @click="addStageAndFocus('Deploy')">部署</el-button>
            <el-button plain :icon="Plus" @click="addStageAndFocus('Sql')">数据库</el-button>
          </div>
        </div>

        <!-- 右栏：当前阶段表单 -->
        <div class="edit-content">
          <div v-if="activeStage && editing.stages.length > 0" class="content-scroll">
            <div class="content-header">
              <el-input v-model="activeStage.name" class="content-name-input" size="small" placeholder="阶段名称" maxlength="30" :disabled="editingRunning" />
              <el-tag size="small" :type="activeStage.type === 'Build' ? 'primary' : activeStage.type === 'Sql' ? 'warning' : 'success'" effect="plain">{{ stageTypeLabel[activeStage.type] }}</el-tag>
            </div>

          <el-form v-if="activeStage.type === 'Build'" label-width="82px" size="small" class="stage-form" :disabled="editingRunning">
            <el-form-item label="项目目录">
              <el-input
                v-model="activeStage.projectDir"
                :placeholder="activeStage.buildType === 'Installer'
                  ? '安装脚本 .iss 所在目录；留空 = 继承上一步项目目录'
                  : '本地项目根目录；留空 = 使用上一步骤输出目录'"
              >
                <template #append>
                  <el-button :icon="Folder" @click="pickStageDir(activeStage, 'projectDir')" />
                </template>
              </el-input>
            </el-form-item>
            <el-row :gutter="12">
              <el-col :span="12">
                <el-form-item label="构建类型">
                    <el-select v-model="activeStage.buildType" style="width: 100%;">
                      <el-option v-for="opt in buildTypeOptions" :key="opt.value" :label="opt.label" :value="opt.value" />
                    </el-select>
                  </el-form-item>
              </el-col>
              <el-col :span="12">
                <el-form-item label="输出目录">
                    <el-input
                      v-model="activeStage.outputDir"
                      placeholder="留空自动推断"
                      @change="activeStage.outputDirCustom = (activeStage.outputDir ?? '').trim() !== ''"
                    >
                      <template #append>
                        <el-button :icon="Folder" @click="pickStageDir(activeStage, 'outputDir')" />
                      </template>
                    </el-input>
                  </el-form-item>
              </el-col>
            </el-row>
            <div class="stage-options">
              <el-checkbox v-model="activeStage.prePull">构建前拉取代码（git pull）</el-checkbox>
            </div>
          </el-form>

          <el-form v-if="activeStage.type === 'Deploy'" label-width="82px" size="small" class="stage-form" :disabled="editingRunning">
            <el-row :gutter="12">
              <el-col :span="12">
                <el-form-item label="服务器">
                    <el-input v-model="activeStage.host" placeholder="SSH 服务器地址" />
                  </el-form-item>
              </el-col>
              <el-col :span="6">
                <el-form-item label="用户名">
                    <el-input v-model="activeStage.userName" placeholder="root" />
                  </el-form-item>
              </el-col>
              <el-col :span="6">
                <el-form-item label="目标系统">
                    <el-select v-model="activeStage.targetOS" style="width: 100%;">
                      <el-option label="Linux" value="Linux" />
                      <el-option label="Windows" value="Windows" />
                    </el-select>
                  </el-form-item>
              </el-col>
            </el-row>
            <el-row :gutter="12">
              <el-col :span="12">
                <el-form-item label="部署目录">
                    <el-input v-model="activeStage.explicitOutputDir" placeholder="留空 = 上一个构建阶段的产物目录">
                      <template #append>
                        <el-button :icon="Folder" @click="pickStageDir(activeStage, 'explicitOutputDir')" />
                      </template>
                    </el-input>
                  </el-form-item>
              </el-col>
              <el-col :span="6">
                <el-form-item label="站点名">
                    <el-input v-model="activeStage.siteName" placeholder="convenient" />
                  </el-form-item>
              </el-col>
              <el-col :span="6">
                <el-form-item label="部署类型">
                    <!-- 前面有构建阶段时自动跟随构建产物（后端同一规则），无需也无法手选 -->
                    <el-select v-if="!hasBuildBeforeActive" v-model="activeStage.deployBuildType" style="width: 100%;">
                      <el-option v-for="opt in buildTypeOptions" :key="opt.value" :label="opt.label" :value="opt.value" />
                    </el-select>
                    <el-input v-else :model-value="`跟随构建 · ${buildTypeLabel(precedingBuildType)}`" disabled />
                  </el-form-item>
              </el-col>
            </el-row>
            <el-row :gutter="12">
              <el-col :span="8">
                <el-form-item label="服务名">
                    <el-input v-model="activeStage.serviceName" placeholder="留空自动推断" />
                  </el-form-item>
              </el-col>
              <el-col :span="8">
                <el-form-item label="远程目录">
                    <el-input v-model="activeStage.remoteDir" placeholder="留空自动推断" />
                  </el-form-item>
              </el-col>
              <el-col :span="8">
                <el-form-item label="部署路径">
                    <el-input v-model="activeStage.deployPath" placeholder="留空用默认" />
                  </el-form-item>
              </el-col>
            </el-row>
            <div class="stage-options">
              <el-checkbox v-model="activeStage.verifyHealth">部署后健康检查</el-checkbox>
              <el-checkbox v-model="activeStage.keepDatabase">保留数据库容器</el-checkbox>
            </div>
            <div class="stage-hint">
              SSH 密码不落配置：运行时自动读取本机已保存凭据（在通用构建页部署时勾选"记住密码"即可）；
              前面有构建阶段时，部署类型自动跟随构建产物（服务名/远程目录留空即按其默认值推断）
            </div>
          </el-form>

          <el-form v-if="activeStage.type === 'Sql'" label-width="82px" size="small" class="stage-form" :disabled="editingRunning">
            <el-row :gutter="12">
              <el-col :span="12">
                <el-form-item label="数据库类型">
                    <el-select v-model="activeStage.dbType" style="width: 100%;">
                      <el-option v-for="t in dbTypeOptions" :key="t" :label="t" :value="t" />
                    </el-select>
                  </el-form-item>
              </el-col>
              <el-col :span="12">
                <el-form-item label="事务包裹">
                    <el-checkbox v-model="activeStage.useTransaction">每个文件一个事务，失败回滚</el-checkbox>
                  </el-form-item>
              </el-col>
            </el-row>
            <el-form-item label="连接串">
              <el-input
                v-model="activeStage.connectionString"
                type="password"
                show-password
                placeholder="目标数据库连接串（Server=...;Database=...;User Id=...;Password=...）"
              >
                <template #append>
                  <el-button
                    :icon="Link"
                    :loading="testingConn"
                    :disabled="!activeStage.connectionString?.trim()"
                    @click="onTestConnection(activeStage)"
                  >测试连接</el-button>
                </template>
              </el-input>
              <!-- 测试结果内联常驻：绿=连通（含版本与耗时），红=失败原因原文；改连接串/切阶段自动清空 -->
              <div v-if="connTestResult" class="conn-test-result" :class="connTestResult.success ? 'is-ok' : 'is-fail'">
                <el-icon v-if="connTestResult.success"><CircleCheckFilled /></el-icon>
                <el-icon v-else><CircleCloseFilled /></el-icon>
                <span>{{ connTestResult.success
                  ? `连接成功（${connTestResult.serverVersion || '已连通'}，${connTestResult.elapsedMs} ms）`
                  : `连接失败：${connTestResult.message}` }}</span>
              </div>
            </el-form-item>
            <el-form-item label="SQL 文件">
              <el-input v-model="activeStage.sqlSource" placeholder="SQL 文件或目录；留空 = 上一个构建阶段的产物目录">
                <template #append>
                  <!-- 与其他行（项目目录等）统一：纯图标按钮，避免带文字按钮挤压重叠 -->
                  <el-tooltip content="选择单个 SQL 文件" placement="top">
                    <el-button :icon="Document" @click="pickSqlFile(activeStage)" />
                  </el-tooltip>
                  <el-tooltip content="选择目录（执行其中全部 .sql）" placement="top">
                    <el-button :icon="FolderOpened" @click="pickSqlFolder(activeStage)" />
                  </el-tooltip>
                </template>
              </el-input>
            </el-form-item>
            <div class="stage-hint">
              目录时按文件名排序执行其中全部 .sql（不含子目录）；SQL Server 脚本按 GO 行切批次，其他库整文件执行。
              脚本含 CREATE PROCEDURE / BACKUP 等不能进事务的语句时请关闭事务包裹。
            </div>
          </el-form>
          </div>
          <div class="stage-actions-bar">
            <el-button size="small" text type="danger" :icon="Delete" :disabled="editingRunning || editing.stages.length <= 1" @click="removeStageWithSwitch(activeStageIndex)">删除此阶段</el-button>
            <el-button size="small" text :icon="ArrowUp" :disabled="activeStageIndex === 0 || editingRunning" @click="moveStage(activeStageIndex, -1)" />
            <el-button size="small" text :icon="ArrowDown" :disabled="activeStageIndex === editing.stages.length - 1 || editingRunning" @click="moveStage(activeStageIndex, 1)" />
          </div>
        </div>
      </div>

      <template #footer>
        <el-button @click="editVisible = false">取消</el-button>
        <el-button
          type="primary"
          :loading="saving"
          :disabled="editing.id ? !$has('pipeline:edit') : !$has('pipeline:add')"
          @click="onSave"
        >保存</el-button>
      </template>
    </el-dialog>

    <!-- 执行弹窗：流水线图 + 运行摘要 + 日志（只读运行视图，无配置信息） -->
    <el-dialog v-model="runVisible" width="900px" top="4vh" :title="currentRun ? `运行 - ${currentRun.pipelineName}` : '运行'">
      <template v-if="currentRun">
        <!-- 流水线图：横向阶段节点 -->
        <div class="pipeline-diagram">
          <template v-for="(node, i) in diagramStages" :key="i">
            <div class="stage-node" :class="stageStatusClass(node.run?.status)" :title="stageNodeTitle(node)">
              <div class="node-head">
                <span class="node-index">
                  <el-icon v-if="node.run?.status === 'Success'"><CircleCheckFilled /></el-icon>
                  <el-icon v-else-if="node.run?.status === 'Failed'"><CircleCloseFilled /></el-icon>
                  <template v-else>{{ i + 1 }}</template>
                </span>
                <span class="node-name">{{ node.name }}</span>
                <span v-if="node.type" class="node-type">{{ stageTypeLabel[node.type] }}</span>
              </div>
              <div class="node-status">
                <el-icon v-if="node.run?.status === 'Running'" class="spin"><Loading /></el-icon>
                <template v-else>
                  <span class="node-status-dot"></span>
                  <span>{{ stageStatusText(node.run?.status || 'Pending') }}</span>
                </template>
                <span v-if="stageClockText(node)" class="node-elapsed">{{ stageClockText(node) }}</span>
              </div>
              <!-- 运行中：当前步骤文本 + 进度条（构建=蓝 / 部署、数据库=橙，与通用构建页卡片一致） -->
              <template v-if="node.run?.status === 'Running'">
                <div v-if="node.run!.stepText" class="node-step-text" :title="node.run!.stepText">{{ node.run!.stepText }}</div>
                <div class="node-progress">
                  <el-progress
                    :percentage="Math.min(node.run!.progress ?? 0, 100)"
                    :stroke-width="5"
                    :show-text="false"
                    :status="node.type === 'Build' ? undefined : 'warning'"
                  />
                </div>
              </template>
              <!-- 失败：红色进度条停在失败位置 + 最后步骤文本 + 失败原因 -->
              <template v-else-if="node.run?.status === 'Failed'">
                <div v-if="node.run!.stepText" class="node-step-text is-fail" :title="node.run!.stepText">{{ node.run!.stepText }}</div>
                <div class="node-progress">
                  <el-progress
                    :percentage="Math.min(node.run!.progress ?? 0, 100)"
                    :stroke-width="5"
                    :show-text="false"
                    status="exception"
                  />
                </div>
                <div v-if="node.run!.message" class="node-message is-fail" :title="node.run!.message">{{ node.run!.message }}</div>
              </template>
              <!-- 成功：结果摘要（产物大小/部署目标/SQL 统计）；跳过：原因 -->
              <div
                v-else-if="(node.run?.status === 'Success' || node.run?.status === 'Skipped') && node.run!.message"
                class="node-message"
                :class="{ 'is-skip': node.run!.status === 'Skipped' }"
                :title="node.run!.message"
              >{{ node.run!.message }}</div>
            </div>
            <div v-if="i < diagramStages.length - 1" class="stage-arrow" :class="arrowClass(diagramStages[i + 1]?.run?.status)"></div>
          </template>
          <el-empty v-if="diagramStages.length === 0" description="无阶段数据" :image-size="60" />
        </div>

        <!-- 运行摘要条 -->
        <div class="run-summary">
          <el-tag :type="runStatusTagType(currentRun.status)" effect="dark" size="small">{{ runStatusText(currentRun.status) }}</el-tag>
          <div class="summary-stages" :title="`阶段 ${currentRun.stages.filter((s) => s.status === 'Success').length}/${currentRun.stages.length} 成功`">
            <span v-for="(s, i) in currentRun.stages" :key="i" class="seg" :class="stageStatusClass(s.status)"></span>
            <span class="seg-count">{{ currentRun.stages.filter((s) => s.status === 'Success').length }}/{{ currentRun.stages.length }}</span>
          </div>
          <span class="summary-text">{{ formatRunRange(currentRun) }}</span>
          <span class="summary-text">{{ running ? `已运行 ${runElapsedText(currentRun)}` : `总耗时 ${runElapsedText(currentRun)}` }}</span>
          <span v-if="currentRun.stages.some((s) => s.status === 'Failed')" class="summary-fail">
            失败：{{ currentRun.stages.find((s) => s.status === 'Failed')?.message }}
          </span>
        </div>

        <!-- 运行日志 -->
        <div class="run-panel">
          <div class="run-panel-head">
            <span class="run-panel-title">运行日志</span>
            <div class="run-panel-actions">
              <el-checkbox v-model="logAutoFollow" size="small">自动滚动</el-checkbox>
              <el-tooltip content="复制日志" placement="top">
                <el-icon class="copy-icon" :class="{ 'is-disabled': !currentRun?.log }" @click="currentRun?.log && copyLog()">
                  <DocumentCopy />
                </el-icon>
              </el-tooltip>
            </div>
          </div>
          <div ref="logTerminalRef" class="log-terminal" @scroll="onLogScroll">
            <pre v-if="currentRun.log">{{ currentRun.log }}</pre>
            <el-empty v-else description="无日志（历史记录重启后日志不可查）" :image-size="60" />
          </div>
        </div>
      </template>
      <el-empty v-else description="暂无运行数据" :image-size="70" />

      <template #footer>
        <el-button v-if="running && $has('pipeline:cancel')" type="danger" plain @click="onCancelRun">取消运行</el-button>
        <el-button :icon="Document" @click="openHistory(currentRun?.pipelineId)">运行历史</el-button>
        <el-button type="primary" @click="runVisible = false">关闭</el-button>
      </template>
    </el-dialog>

    <!-- 运行历史弹窗 -->
    <el-dialog v-model="historyVisible" title="运行历史（最近 50 条）" width="860px">
      <el-table :data="historyItems" size="small" stripe :max-height="480" v-loading="historyLoading">
        <el-table-column label="流水线" width="130" show-overflow-tooltip>
          <template #default="{ row }">{{ row.pipelineName }}</template>
        </el-table-column>
        <el-table-column label="开始时间" width="150">
          <template #default="{ row }">{{ formatDateTime(row.startTime) }}</template>
        </el-table-column>
        <el-table-column label="结束时间" width="150">
          <template #default="{ row }">{{ formatDateTime(row.completedTime) || (row.status === 'Running' ? '运行中' : '—') }}</template>
        </el-table-column>
        <el-table-column label="状态" width="86">
          <template #default="{ row }">
            <el-tag :type="runStatusTagType(row.status)" size="small" effect="light">{{ runStatusText(row.status) }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="耗时" width="78">
          <template #default="{ row }">{{ runElapsedText(row as PipelineRun) }}</template>
        </el-table-column>
        <el-table-column label="阶段" min-width="140">
          <template #default="{ row }">
            <span class="history-stage-dots">
              <span
                v-for="(s, i) in row.stages"
                :key="i"
                class="mini-dot"
                :class="stageStatusClass(s.status)"
                :title="`${s.name}：${stageStatusText(s.status)}${s.message ? ' · ' + s.message : ''}`"
              ></span>
            </span>
            <span class="history-stage-count">{{ row.stages.filter((s: any) => s.status === 'Success').length }}/{{ row.stages.length }}</span>
          </template>
        </el-table-column>
        <el-table-column label="操作" width="76" fixed="right">
          <template #default="{ row }">
            <el-button size="small" text type="primary" @click="viewHistoryRun(row as PipelineRun)">查看</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-dialog>
  </div>
</template>

<style scoped>
.pipeline-view {
  display: flex;
  flex-direction: column;
  gap: 14px;
  height: 100%;
  min-height: 0;
  padding: 14px;
  box-sizing: border-box;
  overflow: auto;
}

/* ============================ 工具栏 ============================ */

.toolbar {
  display: flex;
  align-items: center;
  gap: 8px;
  background: #fff;
  border-radius: 8px;
  border: 1px solid #e4e7ed;
  padding: 10px 12px;
  flex-wrap: wrap;
}

.toolbar-tip {
  color: #909399;
  font-size: 12px;
  margin-left: auto;
}

.dirty-tip {
  color: #e6a23c;
  font-size: 12px;
}

.name-input {
  width: 260px;
}

/* ============================ 列表（卡片网格） ============================ */

.pipeline-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(360px, 1fr));
  gap: 14px;
  align-content: start;
  min-height: 120px;
}

/* 空态占满整行网格 */
.grid-empty {
  grid-column: 1 / -1;
  justify-self: center;
}

.pipeline-card {
  background: #fff;
  border: 1px solid #e4e7ed;
  border-radius: 8px;
  padding: 14px 16px 12px;
  display: flex;
  flex-direction: column;
  gap: 10px;
  transition: border-color 0.2s, box-shadow 0.2s, transform 0.2s;
}

.pipeline-card:hover {
  border-color: #c6e2ff;
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.08);
  transform: translateY(-2px);
}

/* 运行中的卡片：蓝色边框呼吸，提示“正在跑” */
.pipeline-card.is-running {
  border-color: #409eff;
  animation: card-breath 2s ease-in-out infinite;
}

@keyframes card-breath {
  0%, 100% { box-shadow: 0 0 0 0 rgba(64, 158, 255, 0.22); }
  50% { box-shadow: 0 0 0 5px rgba(64, 158, 255, 0.07); }
}

.pipe-head {
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 0;
}

.pipe-name {
  font-weight: 600;
  font-size: 15px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

/* 状态徽标靠右 */
.pipe-head .pipe-status,
.pipe-head .run-none {
  margin-left: auto;
  flex-shrink: 0;
}

.run-spin {
  color: #e6a23c;
  flex-shrink: 0;
}

/* 卡片内阶段流程条：胶囊 + 右箭头 */
.pipe-flow {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 4px;
}

.flow-arrow {
  font-size: 12px;
  color: #c0c4cc;
  flex-shrink: 0;
}

/* 运行中卡片：当前阶段提示 */
.pipe-running-stage {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 12px;
  color: #409eff;
  background: #ecf5ff;
  padding: 4px 10px;
  border-radius: 4px;
  width: fit-content;
}

/* 运行中卡片：当前阶段进度条（紧贴提示行下方） */
.pipe-run-progress {
  margin-top: -6px;
}

.pipe-meta {
  display: flex;
  align-items: center;
  gap: 10px;
  font-size: 12px;
  color: #909399;
  min-width: 0;
}

.pipe-recent {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.pipe-update {
  margin-left: auto;
  flex-shrink: 0;
  font-size: 12px;
  color: #c0c4cc;
}

/* 卡片操作区：细分割线下方、按钮居右并排 */
.pipe-actions {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  border-top: 1px solid #f0f2f5;
  padding-top: 10px;
}

.pipe-actions .el-button + .el-button {
  margin-left: 0;
}

.type-badge {
  font-size: 11px;
  border-radius: 4px;
  padding: 1px 6px;
  border: 1px solid;
}

.type-badge.type-build {
  color: #409eff;
  border-color: #b3d8ff;
  background: #ecf5ff;
}

.type-badge.type-deploy {
  color: #67c23a;
  border-color: #c2e7b0;
  background: #f0f9eb;
}

.type-badge.type-sql {
  color: #e6a23c;
  border-color: #f5dab1;
  background: #fdf6ec;
}

.run-none {
  color: #c0c4cc;
  font-size: 12px;
}

.edit-head {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 14px;
}

/* ============================ 流水线图（执行弹窗） ============================ */

.pipeline-diagram {
  display: flex;
  align-items: stretch;
  gap: 4px;
  border-radius: 8px;
  border: 1px solid #e4e7ed;
  background: #fafafa;
  padding: 14px 12px;
  overflow-x: auto;
  min-height: 84px;
}

.stage-node {
  min-width: 128px;
  flex: 1;
  border: 1px solid #dcdfe6;
  border-radius: 8px;
  padding: 8px 10px;
  background: #fff;
  display: flex;
  flex-direction: column;
  gap: 6px;
  transition: border-color 0.25s, box-shadow 0.25s;
}

.stage-node.is-running {
  border-color: #409eff;
  box-shadow: 0 0 0 2px rgba(64, 158, 255, 0.15);
  background: #ecf5ff;
}

.stage-node.is-success {
  border-color: #b3e19d;
  background: #f0f9eb;
}

.stage-node.is-failed {
  border-color: #fab6b6;
  background: #fef0f0;
}

.stage-node.is-skipped {
  opacity: 0.55;
}

/* 阶段连线：线色 = 箭头指向节点的状态（绿=已完成 / 蓝虚线流动=进行中 / 红=失败 / 灰=待运行） */
.stage-arrow {
  align-self: center;
  flex-shrink: 0;
  width: 26px;
  height: 2px;
  position: relative;
  background: #dcdfe6;
}

/* 右端箭头三角 */
.stage-arrow::after {
  content: '';
  position: absolute;
  right: 0;
  top: 50%;
  transform: translateY(-50%);
  border-top: 4px solid transparent;
  border-bottom: 4px solid transparent;
  border-left: 6px solid #dcdfe6;
}

.stage-arrow.is-idle {
  background: repeating-linear-gradient(90deg, #dcdfe6 0 5px, transparent 5px 10px);
}

.stage-arrow.is-done {
  background: #b3e19d;
}

.stage-arrow.is-done::after {
  border-left-color: #b3e19d;
}

.stage-arrow.is-active {
  background: repeating-linear-gradient(90deg, #409eff 0 5px, transparent 5px 10px);
  animation: arrow-flow 0.6s linear infinite;
}

.stage-arrow.is-active::after {
  border-left-color: #409eff;
}

.stage-arrow.is-failed {
  background: #fab6b6;
}

.stage-arrow.is-failed::after {
  border-left-color: #fab6b6;
}

@keyframes arrow-flow {
  to { background-position: 10px 0; }
}

.node-head {
  display: flex;
  align-items: center;
  gap: 6px;
  min-width: 0;
}

.node-index {
  width: 18px;
  height: 18px;
  border-radius: 50%;
  background: #e4e7ed;
  color: #606266;
  font-size: 11px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
}

.stage-node.is-running .node-index {
  background: #409eff;
  color: #fff;
}

/* 成功/失败：序号圆换彩色结果图标（✓/✕，比数字换底色更直观） */
.stage-node.is-success .node-index,
.stage-node.is-failed .node-index {
  background: transparent;
}

.stage-node.is-success .node-index .el-icon {
  color: #67c23a;
  font-size: 18px;
}

.stage-node.is-failed .node-index .el-icon {
  color: #f56c6c;
  font-size: 18px;
}

.node-name {
  font-weight: 600;
  font-size: 13px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.node-type {
  font-size: 11px;
  color: #909399;
  border: 1px solid #e4e7ed;
  border-radius: 4px;
  padding: 0 4px;
  flex-shrink: 0;
}

.node-status {
  display: flex;
  align-items: center;
  gap: 5px;
  font-size: 12px;
  color: #606266;
  min-width: 0;
}

.node-status-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: #c0c4cc;
  flex-shrink: 0;
}

.stage-node.is-running .node-status {
  color: #409eff;
}

.stage-node.is-success .node-status-dot {
  background: #67c23a;
}

.stage-node.is-failed .node-status-dot {
  background: #f56c6c;
}

.node-elapsed {
  margin-left: auto;
  font-size: 11px;
  color: #909399;
  flex-shrink: 0;
}

/* 运行中节点：当前步骤文本（如 [3/7] SFTP 上传到服务器）与进度条 */
.node-step-text {
  font-size: 11px;
  color: #409eff;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

/* 失败时步骤文本变红（保留失败瞬间的步骤，与红色进度条呼应） */
.node-step-text.is-fail {
  color: #f56c6c;
}

.node-progress {
  margin-top: -2px;
}

/* 终态节点结果/原因摘要：成功=灰、失败=红、跳过=更浅的灰 */
.node-message {
  margin-top: 4px;
  font-size: 11px;
  color: #909399;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.node-message.is-fail {
  color: #f56c6c;
}

.node-message.is-skip {
  color: #c0c4cc;
}

/* ============================ 运行摘要条 ============================ */

.run-summary {
  display: flex;
  align-items: center;
  gap: 12px;
  border-radius: 8px;
  border: 1px solid #e4e7ed;
  padding: 8px 12px;
  font-size: 13px;
  margin-top: 12px;
}

/* 阶段分段进度条：每段一色，跑到哪一眼可见 */
.summary-stages {
  display: inline-flex;
  align-items: center;
  gap: 4px;
}

.summary-stages .seg {
  width: 20px;
  height: 6px;
  border-radius: 3px;
  background: #e4e7ed;
}

.summary-stages .seg.is-running {
  background: #409eff;
}

.summary-stages .seg.is-success {
  background: #67c23a;
}

.summary-stages .seg.is-failed {
  background: #f56c6c;
}

.summary-stages .seg.is-skipped {
  background: #dcdfe6;
}

.seg-count {
  margin-left: 4px;
  font-size: 12px;
  color: #909399;
}

.summary-text {
  color: #606266;
}

.summary-fail {
  color: #f56c6c;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

/* ============================ 阶段配置（编辑弹窗） ============================ */

.edit-body {
  display: flex;
  gap: 16px;
  height: 440px;
  min-height: 0;
}

/* --- 左栏：阶段导航 --- */

.edit-nav {
  width: 176px;
  flex-shrink: 0;
  display: flex;
  flex-direction: column;
  padding: 4px;
  background: #f5f7fa;
  border-radius: 8px;
  min-height: 0;
  overflow: hidden;
}

.nav-list {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.nav-item {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 8px 10px;
  border-radius: 6px;
  cursor: pointer;
  transition: background 0.15s;
}

.nav-item:hover {
  background: #ebeef5;
}

.nav-item.is-active {
  background: #ecf5ff;
}

/* 步骤之间用带线条的向下箭头指示流程方向 */
.nav-item:not(:last-child) {
  position: relative;
}

.nav-item:not(:last-child)::after {
  content: '';
  position: absolute;
  left: 50%;
  top: 100%;
  transform: translateX(-50%);
  width: 1px;
  height: 12px;
  background: #c0c4cc;
  z-index: 1;
}

.nav-item:not(:last-child)::before {
  content: '';
  position: absolute;
  left: 50%;
  top: calc(100% + 7px);
  transform: translateX(-50%);
  width: 0;
  height: 0;
  border-left: 4px solid transparent;
  border-right: 4px solid transparent;
  border-top: 5px solid #c0c4cc;
  z-index: 1;
}

.nav-index {
  width: 20px;
  height: 20px;
  border-radius: 50%;
  font-size: 11px;
  font-weight: 600;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  background: #c0c4cc;
  color: #fff;
}

.nav-item.is-build .nav-index { background: #409eff; }
.nav-item.is-deploy .nav-index { background: #67c23a; }
.nav-item.is-sql .nav-index { background: #e6a23c; }

.nav-type-dot {
  width: 7px;
  height: 7px;
  border-radius: 50%;
  flex-shrink: 0;
  background: #c0c4cc;
}

.nav-item.is-build .nav-type-dot { background: #409eff; }
.nav-item.is-deploy .nav-type-dot { background: #67c23a; }
.nav-item.is-sql .nav-type-dot { background: #e6a23c; }

.nav-info {
  display: flex;
  flex-direction: column;
  min-width: 0;
}

.nav-name {
  font-size: 13px;
  line-height: 1.3;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.nav-label {
  font-size: 11px;
  color: #909399;
  line-height: 1.2;
}

.nav-item.is-build .nav-label { color: #409eff; }
.nav-item.is-deploy .nav-label { color: #67c23a; }
.nav-item.is-sql .nav-label { color: #e6a23c; }

/* --- 左栏底部：添加阶段 --- */

.add-stage-group {
  display: flex;
  /* 176px 窄栏放不下三个按钮一行：el-button 的 white-space:nowrap 使其无法收缩，
     会整体溢出并被 .edit-nav 的 overflow:hidden 裁剪（第三个按钮只剩 14px 残影）。
     允许换行，放不下时自动折到下一行。 */
  flex-wrap: wrap;
  gap: 4px;
  padding-top: 8px;
  border-top: 1px solid #e4e7ed;
  margin-top: auto;
}

.add-stage-group .el-button {
  flex: 1;
  font-size: 12px;
  padding: 8px 6px;
}

/* --- 右栏：表单内容区 --- */

.edit-content {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  min-height: 0;
  overflow: hidden;
}

.content-scroll {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  padding-right: 4px;
}

.content-header {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-bottom: 12px;
  padding-bottom: 10px;
  border-bottom: 1px solid #ebeef5;
}

.content-name-input {
  flex: 1;
  max-width: 280px;
}

.stage-form {
  padding: 0 4px;
}

.stage-options {
  display: flex;
  gap: 18px;
  padding: 2px 0 0 82px;
  flex-wrap: wrap;
}

.stage-hint {
  margin-top: 6px;
  padding-left: 82px;
  font-size: 12px;
  color: #909399;
}

/* 测试连接结果：内联常驻，绿=连通 / 红=失败；长错误信息换行不溢出 */
.conn-test-result {
  display: flex;
  gap: 6px;
  margin-top: 6px;
  font-size: 12px;
  line-height: 18px;
  word-break: break-all;
}

.conn-test-result.is-ok {
  color: #67c23a;
}

.conn-test-result.is-fail {
  color: #f56c6c;
}

.stage-actions-bar {
  display: flex;
  align-items: center;
  gap: 4px;
  margin-top: auto;
  padding-top: 12px;
  border-top: 1px solid #ebeef5;
}

/* ============================ 运行日志 ============================ */

.run-panel {
  border-radius: 8px;
  border: 1px solid #e4e7ed;
  padding: 10px 12px;
  margin-top: 12px;
}

.run-panel-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 8px;
}

.run-panel-title {
  font-weight: 600;
  font-size: 13px;
}

.copy-icon {
  font-size: 14px;
  color: #909399;
  cursor: pointer;
  transition: color 0.2s;
  margin-left: 8px;
}

.copy-icon:hover {
  color: #409eff;
}

.copy-icon.is-disabled {
  color: #c0c4cc;
  cursor: not-allowed;
}

.copy-icon.is-disabled:hover {
  color: #c0c4cc;
}

.log-terminal {
  background: #1e1e1e;
  color: #d4d4d4;
  border-radius: 6px;
  padding: 12px;
  min-height: 200px;
  max-height: 380px;
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

/* ============================ 历史弹窗 ============================ */

.history-stage-dots {
  display: inline-flex;
  align-items: center;
  margin-right: 8px;
}

.mini-dot {
  width: 10px;
  height: 10px;
  border-radius: 50%;
  background: #dcdfe6;
  display: inline-block;
  flex-shrink: 0;
}

/* 点间连线：迷你流水线 */
.mini-dot + .mini-dot {
  margin-left: 12px;
  position: relative;
}

.mini-dot + .mini-dot::before {
  content: '';
  position: absolute;
  left: -11px;
  top: 50%;
  transform: translateY(-50%);
  width: 10px;
  height: 2px;
  border-radius: 1px;
  background: #e4e7ed;
}

.mini-dot.is-running {
  background: #409eff;
}

.mini-dot.is-success {
  background: #67c23a;
}

.mini-dot.is-failed {
  background: #f56c6c;
}

.mini-dot.is-skipped {
  background: #c0c4cc;
}

.history-stage-count {
  color: #909399;
  font-size: 12px;
}

/* ============================ 通用动画 ============================ */

.spin {
  animation: pipeline-spin 1.2s linear infinite;
}

@keyframes pipeline-spin {
  from { transform: rotate(0deg); }
  to { transform: rotate(360deg); }
}
</style>

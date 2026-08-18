<script setup lang="ts">
import { onMounted, onBeforeUnmount, onActivated, onDeactivated, ref, shallowRef, nextTick, computed, markRaw, watch } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Setting, Refresh, Search, FullScreen } from '@element-plus/icons-vue'
import { monaco } from '@/common/monacoSetup'
import * as XLSX from 'xlsx'
import { httpGet, httpPost, ApiError } from '@/api/request'
import { useAuthStore } from '@/common/stores/auth'
import CommonDialog from '@/common/components/CommonDialog.vue'

// ========== 类型 ==========
interface QueryResult {
  columns: string[]
  /** 当前页数据（服务端分页，内存中只保留一页） */
  rows: Record<string, unknown>[]
  /** 实际总行数（翻页响应中为 -1，表示沿用已知总数） */
  totalRows: number
}

interface SqlTab {
  id: string
  name: string
  model: monaco.editor.ITextModel
  viewState?: monaco.editor.ICodeEditorViewState
  /** 该页签绑定的数据源名称 */
  dataSource: string
  // 运行时状态（不持久化）
  executing: boolean
  /** 当前执行的 AbortController（用于取消请求） */
  abortController: AbortController | null
  resultSets: QueryResult[]
  activeResultIdx: number
  errorMsg: string
  executionTime: number
  /** 已执行的 SQL（翻页时复用，避免编辑器内容已被修改） */
  lastSql: string
  /** 当前页码 */
  page: number
}

// ========== 编辑器 ==========
const editorEl = ref<HTMLElement>()
const editor = shallowRef<monaco.editor.IStandaloneCodeEditor>()

// 编辑器 / 结果区全屏（浏览器 Fullscreen API）
const editorWrapEl = ref<HTMLElement>()
const resultAreaEl = ref<HTMLElement>()
const isEditorFullscreen = ref(false)
const isResultFullscreen = ref(false)

async function toggleEditorFullscreen() {
  if (document.fullscreenElement) {
    await document.exitFullscreen()
  } else {
    await editorWrapEl.value?.requestFullscreen()
  }
}

async function toggleResultFullscreen() {
  if (document.fullscreenElement) {
    await document.exitFullscreen()
  } else {
    await resultAreaEl.value?.requestFullscreen()
  }
}

function onFsChange() {
  const fsEl = document.fullscreenElement
  isEditorFullscreen.value = fsEl === editorWrapEl.value
  isResultFullscreen.value = fsEl === resultAreaEl.value
  // 全屏切换后重新布局 Monaco 编辑器
  nextTick(() => editor.value?.layout())
}

// ========== 数据源管理（后端 SysDataSource 表） ==========
interface DataSourceItem {
  id: number
  name: string
  connectionString: string
  dbType?: string
  /** 内置数据源（ConvenientSystemDb）：不允许修改删除，支持执行全部 SQL */
  isBuiltIn?: boolean
}

const dataSources = ref<DataSourceItem[]>([])
const activeDataSource = ref('')
const dsDialogVisible = ref(false)
const dsSearchKeyword = ref('')
const newDsName = ref('')
const newDsConnStr = ref('')
const newDsDbType = ref('sqlserver')
const editingDsId = ref<number | null>(null) // 正在编辑的数据源主键 Id
const editingDsName = ref('') // 编辑前的原名称（改名后同步页签用）

/** 是否处于编辑状态（决定表单标题与提交按钮“添加/保存”文案） */
const isEditingDs = computed(() => editingDsId.value !== null)

/** 按名称筛选数据源列表 */
const filteredDataSources = computed(() => {
  const kw = dsSearchKeyword.value.trim().toLowerCase()
  if (!kw) return dataSources.value
  return dataSources.value.filter(d => d.name.toLowerCase().includes(kw))
})

const dbTypeOptions = [
  { value: 'sqlserver', label: 'SQL Server', example: 'server=localhost;database=MyDb;user id=sa;password=xxx;Encrypt=False;TrustServerCertificate=True;Connection Timeout=30;' },
  { value: 'mysql', label: 'MySQL', example: 'Server=localhost;Port=3306;Database=mydb;User ID=root;Password=xxx;' },
  { value: 'postgresql', label: 'PostgreSQL', example: 'Host=localhost;Port=5432;Database=mydb;Username=postgres;Password=xxx;' },
  { value: 'oracle', label: 'Oracle', example: 'Data Source=localhost:1521/ORCL;User Id=scott;Password=xxx;' },
  { value: 'sqlite', label: 'SQLite', example: 'Data Source=D:\\data\\mydb.db;' },
  { value: 'clickhouse', label: 'ClickHouse', example: 'Host=localhost;Port=8123;Database=default;Username=default;Password=xxx;' },
]

/** 当前选中数据源的数据库类型 */
const activeDsType = computed(() => dataSources.value.find(d => d.name === activeDataSource.value)?.dbType || 'sqlserver')

// 当前执行 SQL 的目标数据库（空 = 连接串默认库；PG/Oracle/SQLite 连接绑定单库不支持切换）
const activeDatabase = ref('')
const databases = ref<string[]>([])
const dbSwitchable = computed(() => ['sqlserver', 'mysql', 'clickhouse'].includes(activeDsType.value))

/** 当前选中数据源是否为内置本地库（可执行全部 SQL，其余只读） */
const activeDsIsLocal = computed(() => !!dataSources.value.find(d => d.name === activeDataSource.value)?.isBuiltIn)

function dbTypeLabel(v?: string) {
  return dbTypeOptions.find(o => o.value === (v || 'sqlserver'))?.label ?? v
}

/** 按数据库类型生成对象完整名（各库引号与层级不同） */
function quoteFullName(db?: string, schema?: string, name?: string) {
  switch (activeDsType.value) {
    case 'clickhouse':
    case 'mysql':
      return `\`${db}\`.\`${name}\``
    case 'postgresql':
      return `"${schema || 'public'}"."${name}"`
    case 'oracle':
      return `"${db}"."${name}"`
    case 'sqlite':
      return `"${name}"`
    default:
      return `[${db}].[${schema}].[${name}]`
  }
}

/** 按数据库类型引用列名 */
function quoteColumn(col: string) {
  switch (activeDsType.value) {
    case 'clickhouse':
    case 'mysql':
      return `\`${col}\``
    case 'postgresql':
    case 'oracle':
    case 'sqlite':
      return `"${col}"`
    default:
      return `[${col}]`
  }
}

function onDbTypeChange() {
  const opt = dbTypeOptions.find(o => o.value === newDsDbType.value)
  if (!opt) return
  // 仅在连接字符串为空或仍是未修改的示例时才带出默认示例，避免覆盖用户已填内容
  const cur = newDsConnStr.value.trim()
  if (!cur || dbTypeOptions.some(o => o.example === cur)) {
    newDsConnStr.value = opt.example
  }
}

async function loadDataSources() {
  try {
    dataSources.value = await httpGet<DataSourceItem[]>('/api/Common/SqlQuery/GetDataSources')
  } catch { /* ignore */ }
  const availableNames = new Set(dataSources.value.map(d => d.name))
  if (!activeDataSource.value || !availableNames.has(activeDataSource.value)) {
    activeDataSource.value = dataSources.value[0]?.name ?? ''
  }
  // 同步修正已恢复页签中不可用的数据源，避免切换用户/权限后访问无权数据源
  tabs.value.forEach(t => {
    if (t.dataSource && !availableNames.has(t.dataSource)) {
      t.dataSource = activeDataSource.value
    }
  })
  // 确保初始化时数据库列表被加载（watch 可能因值未变化而不触发）
  if (activeDataSource.value && databases.value.length === 0) loadDatabases()
}

async function addDataSource() {
  const name = newDsName.value.trim()
  const connStr = newDsConnStr.value.trim()
  if (!name || !connStr) {
    ElMessage.warning('名称和连接字符串不能为空')
    return
  }
  const isEdit = editingDsId.value !== null
  try {
    // 修改走 Update 接口（按主键 Id 更新，支持改名）；新增走 Add 接口
    await httpPost(isEdit ? '/api/Common/SqlQuery/UpdateDataSource' : '/api/Common/SqlQuery/AddDataSource',
      { id: editingDsId.value ?? 0, name, connectionString: connStr, dbType: newDsDbType.value })
    // 改名后同步各页签绑定的数据源名称
    if (isEdit && editingDsName.value && editingDsName.value !== name) {
      tabs.value.forEach(t => { if (t.dataSource === editingDsName.value) t.dataSource = name })
    }
    ElMessage.success(isEdit ? '数据源已修改' : '数据源已添加')
    resetDsForm()
    await loadDataSources()
    activeDataSource.value = name
  } catch { /* 错误已由 request.ts 弹出提示 */ }
}

function resetDsForm() {
  newDsName.value = ''
  newDsDbType.value = 'sqlserver'
  // 默认带出当前类型的示例连接字符串
  newDsConnStr.value = dbTypeOptions.find(o => o.value === newDsDbType.value)?.example ?? ''
  editingDsId.value = null
  editingDsName.value = ''
}
// 初始化预填示例，保证首次打开弹窗也有默认连接字符串
resetDsForm()

/** 调用后端测试连接并提示结果（表单测试与列表行内测试共用） */
async function runConnectionTest(name: string, connStr: string, dbType: string) {
  try {
    const data = await httpPost<{ message: string }>('/api/Common/SqlQuery/TestDataSource', { name, connectionString: connStr, dbType })
    ElMessage.success(data?.message || '连接成功')
  } catch (e) {
    if (e instanceof ApiError && e.responseBody.hint) {
      const body = e.responseBody
      try {
        await ElMessageBox.confirm(
          `${body.hint}\n\n原始错误：${(body.message as string) || ''}`,
          '缺少驱动或运行环境',
          {
            confirmButtonText: body.downloadUrl ? '打开下载页面' : '知道了',
            cancelButtonText: '关闭',
            showCancelButton: !!body.downloadUrl,
            type: 'warning',
            customStyle: { whiteSpace: 'pre-wrap' },
          },
        )
        if (body.downloadUrl) window.open(body.downloadUrl as string, '_blank')
      } catch { /* 用户关闭弹窗 */ }
    } else {
      ElMessage.error((e as Error).message || '连接失败')
    }
  }
}

/** 测试表单中的连接字符串是否可用（无需先保存） */
const testingDs = ref(false)
async function testDataSource() {
  const connStr = newDsConnStr.value.trim()
  if (!connStr) {
    ElMessage.warning('请先填写连接字符串')
    return
  }
  testingDs.value = true
  try {
    await runConnectionTest(newDsName.value.trim(), connStr, newDsDbType.value)
  } finally {
    testingDs.value = false
  }
}

/** 内置数据源（ConvenientSystemDb）行置灰 */
function dsRowClass({ row }: { row: DataSourceItem }) {
  return row.isBuiltIn ? 'ds-row-builtin' : ''
}

/** 列表行内测试连接（按行显示 loading） */
const testingRowName = ref<string | null>(null)
async function testDataSourceRow(row: DataSourceItem) {
  testingRowName.value = row.name
  try {
    await runConnectionTest(row.name, row.connectionString, row.dbType || 'sqlserver')
  } finally {
    testingRowName.value = null
  }
}

function startEditDs(ds: DataSourceItem) {
  if (ds.isBuiltIn) {
    ElMessage.warning('内置数据源不允许修改')
    return
  }
  editingDsId.value = ds.id
  editingDsName.value = ds.name
  newDsName.value = ds.name
  newDsConnStr.value = ds.connectionString
  newDsDbType.value = ds.dbType || 'sqlserver'
}

async function removeDataSource(ds: DataSourceItem) {
  if (ds.isBuiltIn) {
    ElMessage.warning('内置数据源不允许删除')
    return
  }
  try {
    await ElMessageBox.confirm(`确定删除数据源 "${ds.name}" 吗？`, '删除确认', {
      confirmButtonText: '删除',
      cancelButtonText: '取消',
      type: 'warning',
    })
  } catch { return } // 用户取消
  try {
    await httpPost('/api/Common/SqlQuery/RemoveDataSource', { name: ds.name, connectionString: '' })
    ElMessage.success('已删除')
    if (editingDsId.value === ds.id) resetDsForm()
    await loadDataSources()
  } catch { /* 错误已由 request.ts 弹出提示 */ }
}

// ========== 对象资源管理器（数据库对象树） ==========
interface ObjTreeNode {
  id: string
  label: string
  type: 'database' | 'folder' | 'table' | 'view' | 'proc' | 'func' | 'column' | 'tableFolder' | 'childItem'
  db?: string
  schema?: string
  name?: string
  folderKind?: 'tables' | 'views' | 'procedures' | 'functions'
  /** 表下分组文件夹类别：columns/keys/constraints/triggers/indexes/stats */
  tableFolderKind?: string
  suffix?: string
  leaf?: boolean
  /** 表/字段的中文注释（用于悬浮提示） */
  description?: string
}

interface DbObjectItem { schema: string; name: string; description?: string }
interface DbObjects { tables: DbObjectItem[]; views: DbObjectItem[]; procedures: DbObjectItem[]; functions: DbObjectItem[] }

const objExplorerVisible = ref(true)
const objTreeKey = ref(0)
const dbObjCache = new Map<string, DbObjects>()

// 左侧面板宽度（可拖拽调整）
const objExplorerWidth = ref(260)
const objExplorerEl = ref<HTMLElement>()

/** 依据树中最长节点内容自适应左侧面板宽度（只放宽不收窄，与拖拽上限一致封顶 600px） */
function autoFitObjExplorerWidth() {
  nextTick(() => {
    const panel = objExplorerEl.value
    if (!panel) return
    const panelLeft = panel.getBoundingClientRect().left
    let needed = 0
    panel.querySelectorAll<HTMLElement>('.obj-tree .obj-label').forEach(label => {
      // scrollWidth 为省略号截断前的完整文本宽度；筛选图标/后缀等兄弟元素宽度一并计入
      let w = label.getBoundingClientRect().left - panelLeft + label.scrollWidth
      for (let sib = label.nextElementSibling as HTMLElement | null; sib; sib = sib.nextElementSibling as HTMLElement | null)
        w += sib.offsetWidth + 4
      needed = Math.max(needed, w)
    })
    const target = Math.min(600, Math.ceil(needed) + 16) // 预留滚动条宽度
    if (target > objExplorerWidth.value) objExplorerWidth.value = target
  })
}

function startObjResize(e: MouseEvent) {
  e.preventDefault()
  const startX = e.clientX
  const startW = objExplorerWidth.value
  const onMove = (ev: MouseEvent) => {
    objExplorerWidth.value = Math.min(600, Math.max(160, startW + ev.clientX - startX))
  }
  const onUp = () => {
    document.removeEventListener('mousemove', onMove)
    document.removeEventListener('mouseup', onUp)
    document.body.style.cursor = ''
    document.body.style.userSelect = ''
  }
  document.addEventListener('mousemove', onMove)
  document.addEventListener('mouseup', onUp)
  document.body.style.cursor = 'col-resize'
  document.body.style.userSelect = 'none'
}

function reloadObjTree() {
  dbObjCache.clear()
  objTreeKey.value++
}

// 对象树筛选（文件夹级：点击文件夹行的筛选图标，仅筛选该文件夹下的对象）
const objTreeRef = ref<{ filter: (v: string) => void }>()
const folderFilters = ref<Record<string, string>>({})
const folderFilterOpenId = ref<string | null>(null)

function toggleFolderFilter(id: string) {
  folderFilterOpenId.value = folderFilterOpenId.value === id ? null : id
}

function focusFolderFilter(el: unknown) {
  ;(el as { focus?: () => void } | null)?.focus?.()
}

function closeFolderFilter(id: string) {
  folderFilters.value[id] = ''
  folderFilterOpenId.value = null
}

/** 清除指定文件夹的筛选内容（输入框内 × 或筛选标签上的 ×） */
function clearFolderFilter(id: string) {
  folderFilters.value[id] = ''
}

function onFolderFilterBlur() {
  // 失焦即收起输入框；有内容时改为标签形式继续展示筛选内容
  folderFilterOpenId.value = null
}

watch(folderFilters, () => { objTreeRef.value?.filter('') }, { deep: true })

/** 存在生效中的筛选时重新应用（懒加载新展开的节点不会自动参与上次筛选） */
function reapplyObjFilter() {
  if (Object.values(folderFilters.value).some(v => v)) {
    nextTick(() => objTreeRef.value?.filter(''))
  }
}

interface ObjFilterTreeNode { data?: ObjTreeNode; parent?: ObjFilterTreeNode }

function filterObjNodeImpl(data: ObjTreeNode, node?: ObjFilterTreeNode): boolean {
  // 列/子对象/表下分组文件夹：跟随所属表的筛选结果
  if ((data.type === 'column' || data.type === 'childItem' || data.type === 'tableFolder') && node?.parent?.data) {
    // tableFolder 的父节点是表，column/childItem 的父是 tableFolder，再往上找表
    const parentData = node.parent.data
    if (parentData.type === 'table' || parentData.type === 'view') {
      return filterObjNodeImpl(parentData, node.parent)
    }
    if (parentData.type === 'tableFolder' && node.parent.parent?.data) {
      return filterObjNodeImpl(node.parent.parent.data, node.parent.parent)
    }
    return true
  }
  // 文件夹级筛选：仅作用于该文件夹的直接子节点
  const parent = node?.parent?.data
  if (parent?.type === 'folder') {
    const f = folderFilters.value[parent.id]
    if (f && !data.label.toLowerCase().includes(f.toLowerCase())) return false
  }
  return true
}

/** el-tree 筛选方法（el-tree 声明的是宽松的 TreeNodeData，这里收窄为 ObjTreeNode） */
function filterObjNode(_value: unknown, data: unknown, node?: unknown): boolean {
  return filterObjNodeImpl(data as ObjTreeNode, node as ObjFilterTreeNode | undefined)
}

// 切换数据源时：重新加载对象树、加载数据库列表并默认选中连接串配置库，并同步到当前页签
watch(activeDataSource, v => {
  reloadObjTree()
  loadDatabases()
  if (activeTab.value && activeTab.value.dataSource !== v) {
    activeTab.value.dataSource = v
    debouncedSave()
  }
})

/** 加载当前数据源的数据库列表，并默认选中连接串中配置的库 */
async function loadDatabases() {
  databases.value = []
  activeDatabase.value = ''
  if (!activeDataSource.value) return
  try {
    const data = await httpGet<{ databases: string[]; defaultDatabase?: string }>('/api/Common/SqlQuery/GetDatabases', { dataSource: activeDataSource.value })
    databases.value = data.databases
    // 默认选中连接串里配置的数据库
    const defaultDb = data.defaultDatabase ?? null
    if (defaultDb && databases.value.includes(defaultDb)) {
      activeDatabase.value = defaultDb
    }
  } catch { /* ignore */ }
}

// ========== 编辑器/结果区伸缩布局 ==========
const editorHeight = ref(200)
const layoutMode = ref<'normal' | 'editorMax' | 'resultMax'>('normal')

const editorStyle = computed(() => {
  if (layoutMode.value === 'editorMax') return { flex: '1', height: 'auto', minHeight: '0' }
  return { height: editorHeight.value + 'px' }
})

/** ▼ 编辑器全屏 / 还原 */
function toggleEditorMax() {
  layoutMode.value = layoutMode.value === 'editorMax' ? 'normal' : 'editorMax'
}

/** ▲ 结果区全屏 / 还原 */
function toggleResultMax() {
  layoutMode.value = layoutMode.value === 'resultMax' ? 'normal' : 'resultMax'
}

function startEditorResize(e: MouseEvent) {
  e.preventDefault()
  // 全屏状态下拖拽分隔条：先恢复分屏，再从当前位置继续拖拽
  if (layoutMode.value === 'editorMax') {
    editorHeight.value = editorEl.value?.clientHeight ?? editorHeight.value
  } else if (layoutMode.value === 'resultMax') {
    editorHeight.value = 80
  }
  layoutMode.value = 'normal'
  const startY = e.clientY
  const startH = editorHeight.value
  const onMove = (ev: MouseEvent) => {
    const maxH = Math.max(120, window.innerHeight - 260)
    editorHeight.value = Math.min(maxH, Math.max(80, startH + ev.clientY - startY))
  }
  const onUp = () => {
    document.removeEventListener('mousemove', onMove)
    document.removeEventListener('mouseup', onUp)
    document.body.style.cursor = ''
    document.body.style.userSelect = ''
  }
  document.addEventListener('mousemove', onMove)
  document.addEventListener('mouseup', onUp)
  document.body.style.cursor = 'row-resize'
  document.body.style.userSelect = 'none'
}

async function fetchDbObjects(db: string): Promise<DbObjects | null> {
  const cached = dbObjCache.get(db)
  if (cached) return cached
  try {
    const data = await httpGet<DbObjects>('/api/Common/SqlQuery/GetSchemaObjects', { dataSource: activeDataSource.value, database: db })
    dbObjCache.set(db, data)
    return data
  } catch (e) {
    ElMessage.warning((e as Error).message || '获取对象列表失败')
    return null
  }
}

async function loadObjNodeImpl(
  node: { level: number; data: ObjTreeNode },
  resolve: (data: ObjTreeNode[]) => void,
) {
  try {
    // 根节点：数据库列表
    if (node.level === 0) {
      if (!activeDataSource.value) { resolve([]); return }
      const data = await httpGet<{ databases: string[]; defaultDatabase?: string }>('/api/Common/SqlQuery/GetDatabases', { dataSource: activeDataSource.value })
      // 同步到工具栏的“数据库”下拉（执行 SQL 时的目标库）
      databases.value = data.databases
      if (!activeDatabase.value) {
        const defaultDb = data.defaultDatabase ?? null
        if (defaultDb && databases.value.includes(defaultDb)) activeDatabase.value = defaultDb
      } else if (!databases.value.includes(activeDatabase.value)) {
        activeDatabase.value = ''
      }
      resolve(data.databases.map(db => ({ id: 'db:' + db, label: db, type: 'database' as const, db })))
      return
    }
    const d = node.data
    // 数据库 → 分类文件夹（ClickHouse/SQLite 无存储过程与函数）
    if (d.type === 'database') {
      const db = d.db!
      const folders: ObjTreeNode[] = [
        { id: db + ':tables', label: '表', type: 'folder', db, folderKind: 'tables' },
        { id: db + ':views', label: '视图', type: 'folder', db, folderKind: 'views' },
      ]
      if (!['clickhouse', 'sqlite'].includes(activeDsType.value)) {
        folders.push(
          { id: db + ':procedures', label: '存储过程', type: 'folder', db, folderKind: 'procedures' },
          { id: db + ':functions', label: '函数', type: 'folder', db, folderKind: 'functions' },
        )
      }
      resolve(folders)
      return
    }
    // 文件夹 → 对象列表（整库一次拉取并缓存）
    if (d.type === 'folder') {
      const objs = await fetchDbObjects(d.db!)
      if (!objs) { resolve([]); return }
      const kind = d.folderKind!
      const typeMap = { tables: 'table', views: 'view', procedures: 'proc', functions: 'func' } as const
      resolve((objs[kind] ?? []).map(o => ({
        id: `${d.db}:${kind}:${o.schema}.${o.name}`,
        label: o.schema === 'dbo' || o.schema === 'public' || o.schema === 'main' || o.schema === d.db ? o.name : `${o.schema}.${o.name}`,
        type: typeMap[kind],
        db: d.db, schema: o.schema, name: o.name,
        leaf: kind === 'procedures' || kind === 'functions',
        description: o.description,
      })))
      reapplyObjFilter()
      return
    }
    // 表/视图 → 分组文件夹（列、键、约束、触发器、索引、统计信息）
    if (d.type === 'table' || d.type === 'view') {
      const subFolders: ObjTreeNode[] = [
        { id: `${d.id}:tf:columns`, label: '列', type: 'tableFolder', tableFolderKind: 'columns', db: d.db, schema: d.schema, name: d.name },
        { id: `${d.id}:tf:keys`, label: '键', type: 'tableFolder', tableFolderKind: 'keys', db: d.db, schema: d.schema, name: d.name },
        { id: `${d.id}:tf:constraints`, label: '约束', type: 'tableFolder', tableFolderKind: 'constraints', db: d.db, schema: d.schema, name: d.name },
        { id: `${d.id}:tf:triggers`, label: '触发器', type: 'tableFolder', tableFolderKind: 'triggers', db: d.db, schema: d.schema, name: d.name },
        { id: `${d.id}:tf:indexes`, label: '索引', type: 'tableFolder', tableFolderKind: 'indexes', db: d.db, schema: d.schema, name: d.name },
      ]
      // SQL Server 额外显示“统计信息”
      if (activeDsType.value === 'sqlserver')
        subFolders.push({ id: `${d.id}:tf:stats`, label: '统计信息', type: 'tableFolder', tableFolderKind: 'stats', db: d.db, schema: d.schema, name: d.name })
      resolve(subFolders)
      return
    }
    // 表下分组文件夹 → 加载对应子对象
    if (d.type === 'tableFolder') {
      const kind = d.tableFolderKind!
      if (kind === 'columns') {
        // 列信息：沿用原有接口
        const data = await httpGet<{ columns: { name: string; type: string; nullable: boolean; isPk: boolean; description?: string }[] }>('/api/Common/SqlQuery/GetSchemaColumns', { dataSource: activeDataSource.value, database: d.db!, schema: d.schema || '', name: d.name! })
        resolve(data.columns.map(c => ({
          id: `${d.id}:${c.name}`,
          label: c.name,
          type: 'column' as const,
          suffix: (c.isPk ? 'PK, ' : '') + c.type + (c.nullable ? ', null' : ', not null'),
          leaf: true,
          description: c.description,
        })))
      } else {
        // 其他分组：调用 TableChildren 接口
        const data = await httpGet<{ items?: { name: string; suffix?: string | null }[] }>('/api/Common/SqlQuery/GetTableChildren', { dataSource: activeDataSource.value, database: d.db!, schema: d.schema || '', name: d.name!, kind })
        resolve(((data.items ?? []) as { name: string; suffix?: string | null }[]).map(item => ({
          id: `${d.id}:${item.name}`,
          label: item.name,
          type: 'childItem' as const,
          suffix: item.suffix || undefined,
          leaf: true,
        })))
      }
      return
    }
    resolve([])
  } catch {
    ElMessage.error('网络错误')
    resolve([])
  }
}

/** el-tree 懒加载入口（el-tree 声明的是宽松的 Node/TreeNodeData，这里收窄为 ObjTreeNode）；子节点渲染后自适应面板宽度 */
function loadObjNode(node: unknown, resolve: unknown) {
  return loadObjNodeImpl(node as { level: number; data: ObjTreeNode }, (data: ObjTreeNode[]) => {
    ;(resolve as (data: ObjTreeNode[]) => void)(data)
    autoFitObjExplorerWidth()
  })
}

/** 对象树节点悬浮提示：表/字段显示中文注释 */
function objNodeTitle(data: ObjTreeNode): string {
  const suffix = data.suffix ? `\n${data.suffix}` : ''
  const desc = data.description ? `\n${data.description}` : ''
  return `${data.label}${suffix}${desc}`
}

function objIcon(type: ObjTreeNode['type']) {
  switch (type) {
    case 'database': return '🗄️'
    case 'folder': return '📁'
    case 'table': return '📋'
    case 'view': return '👁️'
    case 'proc': return '⚙️'
    case 'func': return 'ƒ'
    case 'column': return '▪'
    case 'tableFolder': return '📂'
    case 'childItem': return '▫'
  }
}

/** 单击节点（保留扩展用） */
function onObjNodeClick(_data: unknown) {
  // 不再联动切换工具栏的数据库下拉
}

/** 双击节点：将名称插入编辑器光标处 */
function onObjNodeDblClick(data: ObjTreeNode) {
  if (data.type === 'table' || data.type === 'view' || data.type === 'proc' || data.type === 'func') {
    insertToEditor(quoteFullName(data.db, data.schema, data.name))
  } else if (data.type === 'column') {
    insertToEditor(data.label)
  }
}

// 对象节点右键菜单
const objCtxMenu = ref({ visible: false, x: 0, y: 0 })
const objCtxNode = ref<ObjTreeNode | null>(null)

function onObjNodeContextMenu(e: MouseEvent, data: ObjTreeNode) {
  if (data.type !== 'table' && data.type !== 'view' && data.type !== 'proc' && data.type !== 'func') return
  e.preventDefault()
  ctxMenu.value.visible = false
  headerCtxMenu.value.visible = false
  objCtxNode.value = data
  objCtxMenu.value = { visible: true, x: e.clientX, y: e.clientY }
}

// ========== 生成语句弹窗（单块/多块复制） ==========
interface ScriptBlock { text: string; checked: boolean }
const scriptDialogVisible = ref(false)
const scriptDialogTitle = ref('')
const scriptBlocks = ref<ScriptBlock[]>([])

function openScriptDialog(title: string, statements: string[]) {
  scriptDialogTitle.value = title
  scriptBlocks.value = statements.map(text => ({ text, checked: false }))
  scriptDialogVisible.value = true
}

const checkedScriptCount = computed(() => scriptBlocks.value.filter(b => b.checked).length)

/** 复制勾选的多个语句块（空行分隔拼接） */
function copyCheckedScripts() {
  const texts = scriptBlocks.value.filter(b => b.checked).map(b => b.text)
  if (!texts.length) { ElMessage.warning('请先勾选语句'); return }
  copyToClipboard(texts.join('\n\n'), `已复制 ${texts.length} 段语句`)
}

function copyAllScripts() {
  copyToClipboard(scriptBlocks.value.map(b => b.text).join('\n\n'), '已复制全部语句')
}

/** 右键查询数据：全部或前 N 行，直接执行并在结果区展示（执行中显示查询中状态） */
function objQueryData(top?: number) {
  const d = objCtxNode.value
  objCtxMenu.value.visible = false
  if (!d) return
  const fullName = quoteFullName(d.db, d.schema, d.name)
  let sql: string
  if (!top) {
    sql = `SELECT * FROM ${fullName};`
  } else if (activeDsType.value === 'sqlserver') {
    sql = `SELECT TOP ${top} * FROM ${fullName};`
  } else if (activeDsType.value === 'oracle') {
    sql = `SELECT * FROM ${fullName} WHERE ROWNUM <= ${top};`
  } else {
    sql = `SELECT * FROM ${fullName} LIMIT ${top};`
  }
  executeSqlText(sql)
}

/** 生成 SELECT 查询语句（带完整列名），弹窗展示 */
async function objGenSelect() {
  const d = objCtxNode.value
  objCtxMenu.value.visible = false
  if (!d) return
  try {
    const data = await httpGet<{ columns: { name: string }[] }>('/api/Common/SqlQuery/GetSchemaColumns', { dataSource: activeDataSource.value, database: d.db!, schema: d.schema!, name: d.name! })
    const cols = data.columns.map(c => quoteColumn(c.name))
    const colList = cols.length ? cols.join(', ') : '*'
    const fullName = quoteFullName(d.db, d.schema, d.name)
    const sql = `SELECT ${colList}\nFROM ${fullName};`
    openScriptDialog(`查询语句 - ${d.name}`, [sql])
  } catch (e) { ElMessage.error((e as Error).message || '获取列信息失败') }
}

/** 生成建表/创建语句，弹窗展示 */
async function objGenCreate() {
  const d = objCtxNode.value
  objCtxMenu.value.visible = false
  if (!d) return
  try {
    const data = await httpGet<{ script: string }>('/api/Common/SqlQuery/GetCreateScript', { dataSource: activeDataSource.value, database: d.db!, schema: d.schema!, name: d.name!, type: d.type })
    openScriptDialog(`${d.type === 'table' ? '建表语句' : '创建语句'} - ${d.name}`, [data.script])
  } catch (e) { ElMessage.error((e as Error).message || '生成脚本失败') }
}

/** 生成修改语句（新增/修改/删除字段模板，均含注释处理），弹窗展示 */
async function objGenAlter() {
  const d = objCtxNode.value
  objCtxMenu.value.visible = false
  if (!d) return
  try {
    const data = await httpGet<{ statements: string[] }>('/api/Common/SqlQuery/GetAlterScript', { dataSource: activeDataSource.value, database: d.db!, schema: d.schema!, name: d.name! })
    openScriptDialog(`修改语句 - ${d.name}`, data.statements)
  } catch (e) { ElMessage.error((e as Error).message || '生成脚本失败') }
}

/** 生成表全部语句（建表/约束/索引/触发器/注释等），弹窗展示 */
async function objGenAll() {
  const d = objCtxNode.value
  objCtxMenu.value.visible = false
  if (!d) return
  try {
    const data = await httpGet<{ statements: string[] }>('/api/Common/SqlQuery/GetAllScript', { dataSource: activeDataSource.value, database: d.db!, schema: d.schema!, name: d.name! })
    openScriptDialog(`所有语句 - ${d.name}`, data.statements)
  } catch (e) { ElMessage.error((e as Error).message || '生成脚本失败') }
}

function objCopyName() {
  const d = objCtxNode.value
  objCtxMenu.value.visible = false
  if (!d) return
  copyToClipboard(quoteFullName(d.db, d.schema, d.name), '名称已复制')
}

function insertToEditor(text: string) {
  const ed = editor.value
  if (!ed) return
  const sel = ed.getSelection()
  if (!sel) return
  ed.executeEdits('obj-tree', [{ range: sel, text, forceMoveMarkers: true }])
  ed.focus()
}

// ========== 多页签管理 ==========
const tabs = ref<SqlTab[]>([])
const activeTabId = ref('')
let tabCounter = 0

const activeTab = computed(() => tabs.value.find(t => t.id === activeTabId.value) ?? null)
const executing = computed(() => activeTab.value?.executing ?? false)

// 当前活动页签的结果集
const resultSets = computed(() => activeTab.value?.resultSets ?? [])
const activeResultIdx = computed(() => activeTab.value?.activeResultIdx ?? 0)
const result = computed(() => resultSets.value[activeResultIdx.value] ?? null)
const tableColumns = computed(() => result.value?.columns ?? [])
const errorMsg = computed(() => activeTab.value?.errorMsg ?? '')
const executionTime = computed(() => activeTab.value?.executionTime ?? 0)

// 根据内容估算列宽（采样前 50 行），列宽总和超出容器时 el-table 才会出现横向滚动条
function estimateTextWidth(text: string): number {
  let w = 0
  for (const ch of text) w += ch.charCodeAt(0) > 255 ? 13 : 7.5
  return Math.ceil(w)
}

const colWidths = computed<Record<string, number>>(() => {
  const map: Record<string, number> = {}
  if (!result.value) return map
  const { columns, rows } = result.value
  const sample = rows.slice(0, 50)
  for (const col of columns) {
    let max = estimateTextWidth(col)
    for (const row of sample) {
      const v = row[col]
      if (v == null) continue
      const w = estimateTextWidth(String(v))
      if (w > max) max = w
    }
    // 加单元格内边距，限制在 80~400 之间，超长内容靠 tooltip 展示
    map[col] = Math.min(Math.max(max + 24, 80), 400)
  }
  return map
})

function createTab(name?: string, content?: string, dataSource?: string): SqlTab {
  const id = `sq-${++tabCounter}-${Date.now()}`
  const model = markRaw(monaco.editor.createModel(content ?? '', 'sql'))
  const tab: SqlTab = {
    id, name: name ?? `查询 ${tabs.value.length + 1}`,
    dataSource: dataSource ?? activeDataSource.value,
    model, executing: false, abortController: null, resultSets: [], activeResultIdx: 0, errorMsg: '', executionTime: 0,
    lastSql: '', page: 1,
  }
  tabs.value.push(tab)
  return tab
}

function switchTab(id: string) {
  if (id === activeTabId.value) return
  // 保存当前视图状态
  if (activeTab.value) {
    activeTab.value.viewState = editor.value?.saveViewState() ?? undefined
  }
  activeTabId.value = id
  const tab = tabs.value.find(t => t.id === id)
  if (tab) {
    // 同步页签绑定的数据源
    if (!tab.dataSource) tab.dataSource = activeDataSource.value
    else if (tab.dataSource !== activeDataSource.value) activeDataSource.value = tab.dataSource
    editor.value?.setModel(tab.model)
    if (tab.viewState) editor.value?.restoreViewState(tab.viewState)
    editor.value?.focus()
  }
  // 切换页签时重置选择状态，并恢复该页签的页码
  selectedRows.value = new Set()
  selectedCol.value = null
  currentPage.value = tab?.page ?? 1
  debouncedSave()
}

function closeTab(id: string) {
  const idx = tabs.value.findIndex(t => t.id === id)
  if (idx < 0) return
  const tab = tabs.value[idx]
  tab.model.dispose()
  tabs.value.splice(idx, 1)

  if (tabs.value.length === 0) {
    const newTab = createTab()
    switchTab(newTab.id)
  } else if (activeTabId.value === id) {
    const next = tabs.value[idx] || tabs.value[idx - 1]
    switchTab(next.id)
  }
  debouncedSave()
}

function addNewTab() {
  const tab = createTab()
  switchTab(tab.id)
}

// 双击标签重命名
const renamingTabId = ref('')
const renameInput = ref('')

function startRename(id: string) {
  const tab = tabs.value.find(t => t.id === id)
  if (!tab) return
  renamingTabId.value = id
  renameInput.value = tab.name
}

function finishRename() {
  if (!renamingTabId.value) return
  const tab = tabs.value.find(t => t.id === renamingTabId.value)
  if (tab && renameInput.value.trim()) {
    tab.name = renameInput.value.trim()
  }
  renamingTabId.value = ''
  debouncedSave()
}

const auth = useAuthStore()

// ========== localStorage 持久化 ==========
// 按登录账号隔离：切换用户后自动使用新账号的独立缓存，不会继承上一位用户的数据源/页签
function storageKey() {
  return `sqlQuery:state:${auth.currentAccount || 'guest'}`
}

interface PersistedTab {
  id: string
  name: string
  content: string
  dataSource?: string
}

function saveState() {
  try {
    const data = {
      tabs: tabs.value.map(t => ({
        id: t.id, name: t.name, content: t.model.getValue(), dataSource: t.dataSource,
      })),
      activeTabId: activeTabId.value,
      dataSource: activeDataSource.value,
      database: activeDatabase.value,
    }
    localStorage.setItem(storageKey(), JSON.stringify(data))
  } catch { /* ignore */ }
}

function restoreState(): boolean {
  try {
    const raw = localStorage.getItem(storageKey())
    if (!raw) return false
    const data = JSON.parse(raw) as { tabs: PersistedTab[]; activeTabId: string; dataSource?: string; database?: string }
    if (!Array.isArray(data.tabs) || data.tabs.length === 0) return false

    // 数据源列表加载完成后，校验持久化的数据源对当前用户是否仍可用
    const availableNames = new Set(dataSources.value.map(d => d.name))
    const validDataSource = data.dataSource && availableNames.has(data.dataSource)
      ? data.dataSource
      : activeDataSource.value || dataSources.value[0]?.name || ''

    activeDataSource.value = validDataSource
    if (data.database) activeDatabase.value = data.database

    const idMap = new Map<string, string>()
    for (const saved of data.tabs) {
      const ds = saved.dataSource && availableNames.has(saved.dataSource)
        ? saved.dataSource
        : validDataSource
      const tab = createTab(saved.name, saved.content || '', ds)
      idMap.set(saved.id, tab.id)
    }
    const activeId = idMap.get(data.activeTabId) || tabs.value[0]?.id || ''
    if (activeId) switchTab(activeId)
    return true
  } catch { return false }
}

let saveTimer: ReturnType<typeof setTimeout> | null = null
function debouncedSave() {
  if (saveTimer) clearTimeout(saveTimer)
  saveTimer = setTimeout(saveState, 1000)
}

// ========== 分页（服务端分页：每页数据由后端返回，内存中只有当前页） ==========
const pageSize = ref(100)
const currentPage = ref(1)
const totalRows = computed(() => result.value?.totalRows ?? 0)
const totalPages = computed(() => Math.ceil(totalRows.value / pageSize.value))
const pagedRows = computed(() => result.value?.rows ?? [])

// 翻页时重新执行 SQL 拉取目标页（needTotal=false 避免重复扫描计数）
watch(currentPage, page => {
  const tab = activeTab.value
  if (!tab || tab.page === page) return
  tab.page = page
  fetchPage(page)
})

// 切换每页条数时：重置到第 1 页并重新查询
watch(pageSize, () => {
  const tab = activeTab.value
  if (!tab || !tab.lastSql) return
  currentPage.value = 1
  tab.page = 1
  fetchPage(1, true)
})

async function fetchPage(page: number, needTotal = false) {
  const tab = activeTab.value
  if (!tab || !tab.lastSql) return
  tab.executing = true
  const startTime = Date.now()
  try {
    const data = await httpPost<{ resultSets?: QueryResult[] }>('/api/Common/SqlQuery/Execute', {
      sql: tab.lastSql,
      dataSource: tab.dataSource || activeDataSource.value,
      database: activeDatabase.value,
      page, pageSize: pageSize.value, needTotal,
    })
    // 只替换各结果集的当前页数据，总数沿用首次执行统计的值（翻页响应中 totalRows 为 -1）
    const newSets = (data.resultSets ?? []) as QueryResult[]
    newSets.forEach((rs, i) => {
      const old = tab.resultSets[i]
      if (old) {
        old.rows = rs.rows
        // needTotal 时更新总行数（切换每页条数时需重新计算）
        if (needTotal && rs.totalRows >= 0) old.totalRows = rs.totalRows
      }
    })
    selectedRows.value = new Set()
    selectedCol.value = null
    selectedCell.value = null
  } catch (e) {
    ElMessage.error('翻页异常：' + (e as Error).message)
  } finally {
    tab.executionTime = Date.now() - startTime
    tab.executing = false
  }
}

function switchResultSet(idx: number) {
  // 各结果集共用同一页窗口（后端对每个结果集取相同页码），切换时不重置页码
  if (activeTab.value) activeTab.value.activeResultIdx = idx
  selectedRows.value = new Set()
  selectedCol.value = null
}

// ========== 选择（行/列/单元格） ==========
// 选中状态仅用于复制与计数；高亮通过直接操作 DOM 实现，
// 避免每次点选都触发整个表格重新渲染（大数据量卡顿的主因）
const selectedRows = ref<Set<number>>(new Set())
const selectedCol = ref<string | null>(null)
const selectedCell = ref<{ row: number; col: string } | null>(null)

const resultTableWrap = ref<HTMLElement>()
let lastCellEl: HTMLElement | null = null
let colStyleEl: HTMLStyleElement | null = null

function applyCellHighlight(cell: HTMLElement | null) {
  lastCellEl?.classList.remove('cell-selected')
  lastCellEl = cell
  cell?.classList.add('cell-selected')
}

/** 列高亮：注入一条针对 el-table 列 id 类名的全局样式规则，零重渲染 */
function applyColHighlight(columnId: string | null) {
  if (!colStyleEl) {
    colStyleEl = document.createElement('style')
    document.head.appendChild(colStyleEl)
  }
  colStyleEl.textContent = columnId
    ? `.sql-query .result-table-wrap td.${columnId}{background-color:#ecf5ff!important}.sql-query .result-table-wrap th.${columnId}{background-color:#d9ecff!important;color:#409eff}`
    : ''
}

/** 按当前页重新应用行选中高亮 */
function applyRowHighlights() {
  const trs = resultTableWrap.value?.querySelectorAll('.el-table__body tbody tr')
  trs?.forEach((tr, i) => tr.classList.toggle('row-selected', selectedRows.value.has(getGlobalIdx(i))))
}

// 翻页/切换结果集后：清除残留单元格高亮，并按新页重新应用行高亮
watch(pagedRows, () => {
  applyCellHighlight(null)
  nextTick(applyRowHighlights)
})
// 外部重置列选中（切页签/重新执行等）时同步清除列高亮
watch(selectedCol, v => { if (!v) applyColHighlight(null) })

function toggleRow(globalIdx: number, event: MouseEvent) {
  selectedCol.value = null
  selectedCell.value = null
  applyCellHighlight(null)
  if (event.ctrlKey || event.metaKey) {
    if (selectedRows.value.has(globalIdx)) selectedRows.value.delete(globalIdx)
    else selectedRows.value.add(globalIdx)
  } else if (event.shiftKey && selectedRows.value.size > 0) {
    const arr = [...selectedRows.value]
    const last = arr[arr.length - 1]
    const [from, to] = last < globalIdx ? [last, globalIdx] : [globalIdx, last]
    for (let i = from; i <= to; i++) selectedRows.value.add(i)
  } else {
    selectedRows.value = new Set([globalIdx])
  }
  applyRowHighlights()
}

function selectCol(col: string, columnId: string) {
  selectedRows.value = new Set()
  selectedCell.value = null
  applyRowHighlights()
  applyCellHighlight(null)
  const next = selectedCol.value === col ? null : col
  selectedCol.value = next
  applyColHighlight(next ? columnId : null)
}

function selectCell(globalIdx: number, col: string, cellEl: HTMLElement) {
  selectedRows.value = new Set()
  selectedCol.value = null
  applyRowHighlights()
  selectedCell.value = { row: globalIdx, col }
  applyCellHighlight(cellEl)
}

// ========== el-table 事件处理 ==========
// 服务端分页后内存中只有当前页数据，选择/复制均基于页内局部索引
function getGlobalIdx(rowIndex: number) {
  return rowIndex
}

/** index 列显示跨页连续序号 */
function rowIndexLabel(i: number) {
  return (currentPage.value - 1) * pageSize.value + i + 1
}

function onTableCellClick(row: Record<string, unknown>, column: { property: string }, cell: HTMLElement, event: MouseEvent) {
  const rowIndex = pagedRows.value.indexOf(row)
  if (rowIndex < 0) return
  if (!column.property) {
    // index 列，选中行
    toggleRow(getGlobalIdx(rowIndex), event)
    return
  }
  selectCell(getGlobalIdx(rowIndex), column.property, cell)
}

function onTableCellContextMenu(row: Record<string, unknown>, column: { property: string }, _cell: HTMLElement, event: MouseEvent) {
  if (!column.property) return
  const text = String(row[column.property] ?? '')
  onCellContextMenu(event, text, column.property)
}

function onTableHeaderClick(column: { property: string; id: string }, event: MouseEvent) {
  if (!column.property) return
  selectCol(column.property, column.id)
}

function onTableHeaderContextMenu(column: { property: string }, event: MouseEvent) {
  if (!column.property) return
  onHeaderContextMenu(event, column.property)
}

// ========== 右键菜单 ==========
const ctxMenu = ref({ visible: false, x: 0, y: 0 })
const ctxCellText = ref('')
const ctxCellCol = ref('')

// 表头右键菜单
const headerCtxMenu = ref({ visible: false, x: 0, y: 0, col: '' })

function onCellContextMenu(e: MouseEvent, text: string, col?: string) {
  e.preventDefault()
  headerCtxMenu.value.visible = false
  ctxCellText.value = text
  ctxCellCol.value = col ?? ''
  ctxMenu.value = { visible: true, x: e.clientX, y: e.clientY }
}

function onHeaderContextMenu(e: MouseEvent, col: string) {
  e.preventDefault()
  ctxMenu.value.visible = false
  headerCtxMenu.value = { visible: true, x: e.clientX, y: e.clientY, col }
}

function closeCtxMenu() {
  ctxMenu.value.visible = false
  headerCtxMenu.value.visible = false
  objCtxMenu.value.visible = false
}

// 表头菜单动作
function headerCopyName() {
  copyToClipboard(headerCtxMenu.value.col, `列名 "${headerCtxMenu.value.col}" 已复制`)
  closeCtxMenu()
}

function headerCopyColData() {
  if (!result.value) return
  const col = headerCtxMenu.value.col
  const lines = [col]
  result.value.rows.forEach(row => {
    lines.push(row[col] == null ? '' : String(row[col]))
  })
  copyToClipboard(lines.join('\n'), `列 "${col}" 当前页数据已复制（${result.value.rows.length} 行）`)
  closeCtxMenu()
}

function headerCopyAllNames() {
  copyColumnNames()
  closeCtxMenu()
}

function ctxCopyCell() {
  copyToClipboard(ctxCellText.value, '已复制')
  closeCtxMenu()
}

function ctxCopyCellColName() {
  if (ctxCellCol.value) {
    copyToClipboard(ctxCellCol.value, `列名 "${ctxCellCol.value}" 已复制`)
  }
  closeCtxMenu()
}

function ctxCopyRow() {
  if (!result.value || selectedRows.value.size === 0) {
    // 没有选中行时复制当前单元格所在行
    if (selectedCell.value && result.value) {
      const row = result.value.rows[selectedCell.value.row]
      if (row) {
        const text = result.value.columns.map(c => row[c] == null ? '' : String(row[c])).join('\t')
        copyToClipboard(text, '已复制行')
      }
    }
  } else {
    copySelectedRows()
  }
  closeCtxMenu()
}

function ctxCopyCol() {
  if (selectedCol.value) {
    copySelectedCol()
  } else if (selectedCell.value && result.value) {
    const col = selectedCell.value.col
    const lines = [col]
    result.value.rows.forEach(row => {
      lines.push(row[col] == null ? '' : String(row[col]))
    })
    copyToClipboard(lines.join('\n'), `列 "${col}" 已复制`)
  }
  closeCtxMenu()
}

function ctxCopyColNames() {
  copyColumnNames()
  closeCtxMenu()
}

// ========== 复制功能 ==========
function copyToClipboard(text: string, msg: string) {
  navigator.clipboard.writeText(text).then(() => ElMessage.success(msg))
}

function copyColumnNames() {
  if (!result.value) return
  copyToClipboard(result.value.columns.join(', '), '列名已复制')
}

function copyCurrentPage() {
  if (!result.value) return
  const { columns } = result.value
  const lines = [columns.join('\t')]
  pagedRows.value.forEach(row => {
    lines.push(columns.map(col => row[col] == null ? '' : String(row[col])).join('\t'))
  })
  copyToClipboard(lines.join('\n'), `当前页 ${pagedRows.value.length} 行已复制`)
}

function copySelectedRows() {
  if (!result.value || selectedRows.value.size === 0) return
  const { columns, rows } = result.value
  const lines = [columns.join('\t')]
  const sorted = [...selectedRows.value].sort((a, b) => a - b)
  sorted.forEach(idx => {
    const row = rows[idx]
    if (row) lines.push(columns.map(col => row[col] == null ? '' : String(row[col])).join('\t'))
  })
  copyToClipboard(lines.join('\n'), `已复制 ${sorted.length} 行`)
}

function copySelectedCol() {
  if (!result.value || !selectedCol.value) return
  const col = selectedCol.value
  const lines = [col]
  result.value.rows.forEach(row => {
    lines.push(row[col] == null ? '' : String(row[col]))
  })
  copyToClipboard(lines.join('\n'), `列 "${col}" 已复制（当前页 ${result.value.rows.length} 行）`)
}

// ========== 导出 Excel ==========
const exporting = ref(false)

/** 根据 SQL 语句提取有意义的文件名 */
function generateExcelName(sql: string): string {
  const cleaned = sql.replace(/[\r\n]+/g, ' ').trim()
  // 尝试提取表名
  const fromMatch = cleaned.match(/\bFROM\s+[\[`"']?([\w.]+)[\]`"']?/i)
  const tableName = fromMatch ? fromMatch[1].replace(/^\w+\./, '') : ''
  // 判断查询类型
  let prefix = '查询结果'
  if (/^\s*SELECT\s+COUNT\s*\(/i.test(cleaned)) prefix = '统计结果'
  else if (/^\s*SELECT\s+TOP\s+/i.test(cleaned)) prefix = '前 N 条'
  else if (/^\s*SELECT/i.test(cleaned)) prefix = '查询结果'
  const name = tableName ? `${tableName}_${prefix}` : prefix
  const ts = new Date().toISOString().slice(0, 10).replace(/-/g, '')
  return `${name}_${ts}`
}

async function exportExcel() {
  const tab = activeTab.value
  if (!tab || !tab.lastSql) {
    ElMessage.warning('请先执行查询')
    return
  }
  exporting.value = true
  try {
    const data = await httpPost<{ columns: string[]; rows: Record<string, unknown>[] }>('/api/Common/SqlQuery/ExportData',
      { sql: tab.lastSql, dataSource: tab.dataSource, database: activeDatabase.value })
    const { columns, rows } = data
    if (!rows.length) {
      ElMessage.warning('查询结果为空，无法导出')
      return
    }
    // 转换为工作表：日期字段保留日期格式，其余字段强制为文本（避免 long 类型变科学计数法）
    const isDateValue = (v: unknown): boolean => {
      if (typeof v !== 'string') return false
      // ISO 日期格式或常见日期格式
      return /^\d{4}-\d{2}-\d{2}(T|\s)/.test(v) || /^\d{4}\/\d{2}\/\d{2}/.test(v)
    }
    const ws: XLSX.WorkSheet = {}
    const range = { s: { c: 0, r: 0 }, e: { c: columns.length - 1, r: rows.length } }
    // 表头
    columns.forEach((col, c) => {
      ws[XLSX.utils.encode_cell({ r: 0, c })] = { t: 's', v: col }
    })
    // 数据行
    rows.forEach((row, r) => {
      columns.forEach((col, c) => {
        const v = row[col]
        const cell: XLSX.CellObject = { t: 's', v: '' }
        if (v == null) {
          cell.v = ''
        } else if (isDateValue(v)) {
          // 日期字段保留日期格式
          cell.t = 's'
          cell.v = String(v).replace('T', ' ').replace(/\.\d+$/, '')
        } else {
          cell.t = 's'
          cell.v = String(v)
        }
        ws[XLSX.utils.encode_cell({ r: r + 1, c })] = cell
      })
    })
    ws['!ref'] = XLSX.utils.encode_range(range)
    // 自动计算列宽：取每列最大内容长度（采样前100行 + 表头）
    const colWidths = columns.map((col, c) => {
      let maxLen = col.length
      const sampleCount = Math.min(rows.length, 100)
      for (let r = 0; r < sampleCount; r++) {
        const v = rows[r][col]
        const len = v == null ? 0 : String(v).length
        if (len > maxLen) maxLen = len
      }
      return { wch: Math.min(Math.max(maxLen + 2, 8), 60) }
    })
    ws['!cols'] = colWidths
    const wb = XLSX.utils.book_new()
    XLSX.utils.book_append_sheet(wb, ws, 'Sheet1')
    const fileName = generateExcelName(tab.lastSql) + '.xlsx'
    XLSX.writeFile(wb, fileName)
    ElMessage.success(`已导出 ${rows.length} 行数据`)
  } catch (e) {
    ElMessage.error('导出异常：' + (e as Error).message)
  } finally {
    exporting.value = false
  }
}

// ========== 历史记录 ==========
const history = ref<{ sql: string; time: string }[]>([])
const historyVisible = ref(false)

function addHistory(sql: string) {
  const now = new Date()
  const time = `${now.getHours().toString().padStart(2, '0')}:${now.getMinutes().toString().padStart(2, '0')}:${now.getSeconds().toString().padStart(2, '0')}`
  history.value.unshift({ sql, time })
  if (history.value.length > 50) history.value.length = 50
}

function loadHistory(sql: string) {
  editor.value?.setValue(sql)
  historyVisible.value = false
}

// ========== 快捷输入管理 ==========
interface SnippetItem {
  id: number
  shortcut: string
  expansion: string
  remark: string | null
  sortOrder: number
}

const snippets = ref<SnippetItem[]>([])
const snippetDialogVisible = ref(false)
const snippetForm = ref({ id: 0, shortcut: '', expansion: '', remark: '', sortOrder: 0 })
const isEditingSnippet = computed(() => snippetForm.value.id > 0)

async function loadSnippets() {
  try {
    snippets.value = await httpGet<SnippetItem[]>('/api/Common/SqlQuery/GetSnippets')
  } catch { /* ignore */ }
}

function openSnippetDialog() {
  snippetDialogVisible.value = true
  loadSnippets()
}

function resetSnippetForm() {
  snippetForm.value = { id: 0, shortcut: '', expansion: '', remark: '', sortOrder: 0 }
}

function editSnippet(row: SnippetItem) {
  snippetForm.value = { id: row.id, shortcut: row.shortcut, expansion: row.expansion, remark: row.remark ?? '', sortOrder: row.sortOrder }
}

async function saveSnippet() {
  const { id, shortcut, expansion, remark, sortOrder } = snippetForm.value
  if (!shortcut.trim() || !expansion.trim()) {
    ElMessage.warning('快捷输入和展开内容不能为空')
    return
  }
  const isEdit = id > 0
  try {
    await httpPost(isEdit ? '/api/Common/SqlQuery/UpdateSnippet' : '/api/Common/SqlQuery/AddSnippet',
      { id, shortcut: shortcut.trim(), expansion, remark: remark || null, sortOrder })
    ElMessage.success(isEdit ? '已保存' : '已添加')
    resetSnippetForm()
    await loadSnippets()
    registerSnippetCompletion()
  } catch (e) { ElMessage.error((e as Error).message || '操作失败') }
}

async function removeSnippet(row: SnippetItem) {
  try {
    await ElMessageBox.confirm(`确定删除快捷输入 "${row.shortcut}" ？`, '提示', { type: 'warning' })
  } catch { return }
  try {
    await httpPost('/api/Common/SqlQuery/RemoveSnippet', { id: row.id })
    ElMessage.success('已删除')
    await loadSnippets()
    registerSnippetCompletion()
  } catch (e) { ElMessage.error((e as Error).message || '删除失败') }
}

async function resetSnippets() {
  let password = ''
  try {
    const { value } = await ElMessageBox.prompt('请输入登录密码以确认重置', '重置快捷输入', {
      inputType: 'password',
      inputPlaceholder: '请输入密码',
      confirmButtonText: '确认重置',
      cancelButtonText: '取消',
      type: 'warning',
      inputValidator: (val) => !!val?.trim() || '密码不能为空',
    })
    password = value
  } catch { return }
  try {
    await httpPost('/api/Common/SqlQuery/ResetSnippets', { password })
    ElMessage.success('已重置为初始数据')
    resetSnippetForm()
    await loadSnippets()
    registerSnippetCompletion()
  } catch (e) { ElMessage.error((e as Error).message || '重置失败') }
}

// 注册 Monaco 快捷输入补全提供者
let snippetDisposable: monaco.IDisposable | null = null

function registerSnippetCompletion() {
  if (snippetDisposable) {
    snippetDisposable.dispose()
    snippetDisposable = null
  }
  const items = snippets.value
  if (items.length === 0) return
  snippetDisposable = monaco.languages.registerCompletionItemProvider('sql', {
    triggerCharacters: [],
    provideCompletionItems(model, position) {
      // 获取当前行光标前的文本匹配快捷缩写
      const lineContent = model.getLineContent(position.lineNumber)
      const textBeforeCursor = lineContent.substring(0, position.column - 1)
      // 取最后一个单词作为匹配前缀
      const wordMatch = textBeforeCursor.match(/(\w+)$/)
      const prefix = wordMatch ? wordMatch[1].toLowerCase() : ''
      if (!prefix) return { suggestions: [] }
      const wordStart = position.column - prefix.length
      const range = new monaco.Range(position.lineNumber, wordStart, position.lineNumber, position.column)
      const suggestions: monaco.languages.CompletionItem[] = items
        .filter(s => s.shortcut.toLowerCase().startsWith(prefix))
        .map((s, i) => ({
          label: s.shortcut,
          kind: monaco.languages.CompletionItemKind.Snippet,
          detail: s.remark || s.expansion,
          documentation: s.expansion,
          insertText: s.expansion,
          range,
          sortText: String(i).padStart(4, '0'),
        }))
      return { suggestions }
    }
  })
}

// ========== SQL 查询收藏 ==========
interface FavoriteItem {
  id: number
  name: string
  sqlContent: string
  remark: string | null
  dataSource: string | null
  sortOrder: number
}

const favorites = ref<FavoriteItem[]>([])
const favDialogVisible = ref(false)
const favSaveVisible = ref(false)
const favForm = ref({ id: 0, name: '', sqlContent: '', remark: '', dataSource: '', sortOrder: 0 })

async function loadFavorites() {
  try {
    favorites.value = await httpGet<FavoriteItem[]>('/api/Common/SqlQuery/GetFavorites')
  } catch { /* ignore */ }
}

function openFavoriteDialog() {
  favDialogVisible.value = true
  loadFavorites()
}

function openSaveFavDialog() {
  const sql = editor.value?.getValue() || ''
  if (!sql.trim()) {
    ElMessage.warning('编辑器内容为空，无法收藏')
    return
  }
  favForm.value = { id: 0, name: '', sqlContent: sql, remark: '', dataSource: activeDataSource.value || '', sortOrder: 0 }
  favSaveVisible.value = true
}

async function saveFavorite() {
  const { name, sqlContent } = favForm.value
  if (!name.trim()) {
    ElMessage.warning('请输入收藏名称')
    return
  }
  try {
    await httpPost('/api/Common/SqlQuery/AddFavorite', favForm.value)
    ElMessage.success('已收藏')
    favSaveVisible.value = false
    favForm.value = { id: 0, name: '', sqlContent: '', remark: '', dataSource: '', sortOrder: 0 }
    await loadFavorites()
  } catch (e) { ElMessage.error((e as Error).message || '收藏失败') }
}

function loadFavorite(fav: FavoriteItem) {
  const tab = tabs.value.find(t => t.id === activeTabId.value)
  if (tab) {
    tab.model.setValue(fav.sqlContent)
    if (fav.dataSource && dataSources.value.some(ds => ds.name === fav.dataSource)) {
      activeDataSource.value = fav.dataSource
    }
    favDialogVisible.value = false
    ElMessage.success(`已加载: ${fav.name}`)
  }
}

async function removeFavorite(fav: FavoriteItem) {
  try {
    await ElMessageBox.confirm(`确定删除收藏 "${fav.name}" ？`, '提示', { type: 'warning' })
  } catch { return }
  try {
    await httpPost('/api/Common/SqlQuery/RemoveFavorite', { id: fav.id })
    ElMessage.success('已删除')
    await loadFavorites()
  } catch (e) { ElMessage.error((e as Error).message || '删除失败') }
}

// ========== 生命周期 ==========
function onBeforeUnload() { saveState() }

onMounted(async () => {
  window.addEventListener('beforeunload', onBeforeUnload)
  window.addEventListener('click', closeCtxMenu)
  document.addEventListener('fullscreenchange', onFsChange)
  await loadDataSources()
  nextTick(() => {
    if (!editorEl.value) return
    editor.value = monaco.editor.create(editorEl.value, {
      value: '',
      language: 'sql',
      theme: 'vs',
      minimap: { enabled: false },
      automaticLayout: true,
      fontSize: 14,
      tabSize: 2,
      wordWrap: 'on',
      lineNumbers: 'on',
      scrollBeyondLastLine: false,
    })
    editor.value.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.Enter, () => { executeQuery() })
    editor.value.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyN, () => { addNewTab() })

    // 内容变化时防抖持久化
    editor.value.onDidChangeModelContent(() => { debouncedSave() })

    // 加载快捷输入配置并注册补全提供者
    loadSnippets().then(() => { registerSnippetCompletion() })

    // 恢复或创建初始页签
    if (!restoreState()) {
      const tab = createTab()
      switchTab(tab.id)
    }
  })
})

onBeforeUnmount(() => {
  saveState()
  window.removeEventListener('beforeunload', onBeforeUnload)
  window.removeEventListener('click', closeCtxMenu)
  document.removeEventListener('fullscreenchange', onFsChange)
  if (document.fullscreenElement) document.exitFullscreen()
  tabs.value.forEach(t => t.model.dispose())
  editor.value?.dispose()
  editor.value = undefined
  snippetDisposable?.dispose()
  snippetDisposable = null
  colStyleEl?.remove()
  colStyleEl = null
  lastCellEl = null
})

onDeactivated(() => { saveState() })
onActivated(() => { editor.value?.layout() })

// ========== 执行查询 ==========
async function executeQuery() {
  const sql = getExecutableSql()
  if (!sql.trim()) {
    ElMessage.warning('请输入 SQL 语句')
    return
  }
  await executeSqlText(sql)
}

/** 执行指定 SQL 并展示结果（编辑器执行与对象树右键查询共用，执行中结果区展示查询中状态） */
async function executeSqlText(sql: string) {
  const tab = activeTab.value
  if (!tab) return

  // 如果上一次还在执行，先取消
  if (tab.abortController) {
    tab.abortController.abort()
    tab.abortController = null
  }

  const abortController = new AbortController()
  tab.abortController = abortController
  tab.executing = true
  tab.errorMsg = ''
  tab.resultSets = []
  tab.activeResultIdx = 0
  selectedRows.value = new Set()
  selectedCol.value = null
  tab.page = 1
  currentPage.value = 1
  const startTime = Date.now()

  try {
    const data = await httpPost<{ resultSets?: any[]; affectedRows?: number }>('/api/Common/SqlQuery/Execute',
      { sql, dataSource: activeDataSource.value, database: activeDatabase.value, page: 1, pageSize: pageSize.value, needTotal: true },
      abortController.signal)
    tab.resultSets = data.resultSets ?? []
    tab.activeResultIdx = 0
    tab.lastSql = sql
    addHistory(sql)
    if (tab.resultSets.length === 0) {
      // 非查询语句（本地库支持增删改/DDL）：展示受影响行数
      const affected = typeof data.affectedRows === 'number' ? data.affectedRows : -1
      ElMessage.success(affected >= 0 ? `执行成功，受影响 ${affected} 行` : '执行成功')
    } else {
      const totalCount = tab.resultSets.reduce((s, r) => s + r.totalRows, 0)
      ElMessage.success(`执行成功，${tab.resultSets.length} 个结果集，共 ${totalCount} 行`)
    }
  } catch (e) {
    if ((e as Error).name === 'AbortError') {
      tab.errorMsg = '查询已取消'
    } else if (e instanceof ApiError) {
      tab.errorMsg = (e.responseBody.message as string) || e.message
    } else {
      tab.errorMsg = '请求异常：' + (e as Error).message
    }
  } finally {
    tab.executionTime = Date.now() - startTime
    tab.executing = false
    tab.abortController = null
  }
}

/** 取消当前正在执行的查询（真实取消数据库端执行） */
function cancelQuery() {
  const tab = activeTab.value
  if (!tab || !tab.abortController) return
  tab.abortController.abort()
}

function getExecutableSql(): string {
  const ed = editor.value
  if (!ed) return ''
  const selection = ed.getSelection()
  if (selection && !selection.isEmpty()) {
    return ed.getModel()?.getValueInRange(selection) ?? ''
  }
  return ed.getValue()
}

// ========== 查询锁表弹窗 ==========
const lockDialogVisible = ref(false)
const lockSql = ref('')
const lockKillSql = ref('')
const lockLoading = ref(false)
const lockResult = ref<{ columns: string[]; rows: Record<string, unknown>[] } | null>(null)
const lockError = ref('')

function queryLocks() {
  const dsType = activeDsType.value
  let querySql = ''
  let killSql = ''
  if (dsType === 'mysql') {
    querySql = `SELECT
    r.trx_id AS '事务ID',
    r.trx_mysql_thread_id AS '进程ID',
    r.trx_state AS '事务状态',
    r.trx_started AS '开始时间',
    TIMESTAMPDIFF(SECOND, r.trx_started, NOW()) AS '持续秒数',
    r.trx_wait_started AS '等待开始时间',
    p.USER AS '用户',
    p.HOST AS '主机',
    p.DB AS '数据库',
    LEFT(p.INFO, 200) AS 'SQL语句',
    r.trx_rows_locked AS '锁定行数',
    r.trx_tables_in_use AS '使用表数'
FROM information_schema.INNODB_TRX r
LEFT JOIN information_schema.PROCESSLIST p ON r.trx_mysql_thread_id = p.ID
ORDER BY r.trx_started`
    killSql = `-- 将下方 <进程ID> 替换为上方查询结果中的"进程ID"列的值\nKILL <进程ID>;`
  } else if (dsType === 'postgresql') {
    querySql = `SELECT
    pid AS "进程ID",
    usename AS "用户",
    datname AS "数据库",
    application_name AS "应用",
    client_addr AS "客户端IP",
    wait_event_type AS "等待类型",
    wait_event AS "等待事件",
    state AS "状态",
    NOW() - query_start AS "持续时间",
    LEFT(query, 200) AS "SQL语句"
FROM pg_stat_activity
WHERE wait_event_type = 'Lock'
ORDER BY query_start`
    killSql = `-- 将下方 <进程ID> 替换为上方查询结果中的 pid 列的值\nSELECT pg_terminate_backend(<进程ID>);`
  } else if (dsType === 'oracle') {
    querySql = `SELECT
    s.sid AS "SID",
    s.serial# AS "Serial#",
    s.username AS "用户",
    s.machine AS "机器",
    l.type AS "锁类型",
    o.object_name AS "对象名",
    s.status AS "状态",
    s.sql_id AS "SQL_ID"
FROM v$lock l
JOIN v$session s ON l.sid = s.sid
LEFT JOIN dba_objects o ON l.id1 = o.object_id
WHERE l.block > 0 OR l.request > 0
ORDER BY l.block DESC, s.sid`
    killSql = `-- 将 <SID> 和 <Serial#> 替换为上方查询结果中的值\nALTER SYSTEM KILL SESSION '<SID>,<Serial#>' IMMEDIATE;`
  } else {
    // SQL Server：resource_associated_entity_id 对 KEY/PAGE/RID 锁是 bigint 的 hobt_id，
    // 直接传入 OBJECT_NAME(int) 会算术溢出，需按资源类型分流，非 OBJECT 锁经 sys.partitions 反查
    querySql = `SELECT
    req.session_id AS '会话ID',
    ses.login_name AS '登录名',
    ses.host_name AS '主机名',
    DB_NAME(req.database_id) AS '数据库',
    req.status AS '请求状态',
    req.blocking_session_id AS '阻塞来源会话ID',
    req.wait_type AS '等待类型',
    req.wait_time / 1000 AS '等待时间(秒)',
    CASE locks.resource_type
        WHEN 'OBJECT' THEN OBJECT_NAME(CONVERT(int, locks.resource_associated_entity_id), req.database_id)
        ELSE OBJECT_NAME(prt.object_id, req.database_id)
    END AS '锁定对象',
    locks.resource_type AS '资源类型',
    locks.request_mode AS '锁模式',
    locks.request_status AS '锁状态',
    LEFT(st.text, 200) AS 'SQL语句'
FROM sys.dm_exec_requests req
INNER JOIN sys.dm_tran_locks locks ON req.session_id = locks.request_session_id
LEFT JOIN sys.dm_exec_sessions ses ON req.session_id = ses.session_id
LEFT JOIN sys.partitions prt ON prt.hobt_id = locks.resource_associated_entity_id
    AND locks.resource_type IN ('KEY', 'PAGE', 'RID', 'HOBT')
OUTER APPLY sys.dm_exec_sql_text(req.sql_handle) st
WHERE req.blocking_session_id > 0
   OR locks.request_status = 'WAIT'
ORDER BY req.blocking_session_id DESC, req.session_id`
    killSql = `-- 将下方 <会话ID> 替换为上方查询结果中的"会话ID"或"阻塞来源会话ID"列的值\nKILL <会话ID>;`
  }

  lockSql.value = querySql
  lockKillSql.value = killSql
  lockResult.value = null
  lockError.value = ''
  lockDialogVisible.value = true
  // 自动执行查询
  executeLockQuery()
}

async function executeLockQuery() {
  lockLoading.value = true
  lockResult.value = null
  lockError.value = ''
  try {
    const data = await httpPost<{ resultSets?: { columns: string[]; rows: Record<string, unknown>[] }[] }>('/api/Common/SqlQuery/Execute',
      { sql: lockSql.value, dataSource: activeDataSource.value, database: activeDatabase.value, page: 1, pageSize: 500, needTotal: true })
    const rs = (data.resultSets ?? [])[0]
    if (rs) {
      lockResult.value = { columns: rs.columns, rows: rs.rows ?? [] }
    } else {
      lockError.value = '无结果'
    }
  } catch (e) {
    lockError.value = (e instanceof ApiError ? (e.responseBody.message as string) : null) || (e as Error).message
  } finally {
    lockLoading.value = false
  }
}

async function lockExplainPlan() {
  planVisible.value = true
  planLoading.value = true
  planText.value = ''
  planStatements.value = []
  try {
    const data = await httpPost<{ plan?: string; statements?: PlanStatement[] }>('/api/Common/SqlQuery/ExplainPlan',
      { sql: lockSql.value, dataSource: activeDataSource.value, database: activeDatabase.value })
    planText.value = data.plan || '无执行计划'
    if (data.statements && data.statements.length > 0) {
      planStatements.value = data.statements
    }
  } catch (e) {
    planText.value = '✗ ' + ((e instanceof ApiError ? (e.responseBody.message as string) : null) || (e as Error).message)
  } finally {
    planLoading.value = false
  }
}

function copyLockSql() {
  const fullSql = `-- ===== 查询当前锁表信息 =====\n${lockSql.value};\n\n-- ===== 杀死指定锁表进程 =====\n${lockKillSql.value}`
  copyToClipboard(fullSql, '锁表语句已复制')
}

function formatSql() {
  const ed = editor.value
  if (!ed) return
  const raw = ed.getValue()
  if (!raw.trim()) return
  const keywords = ['SELECT', 'FROM', 'WHERE', 'AND', 'OR', 'LEFT JOIN', 'RIGHT JOIN', 'INNER JOIN', 'OUTER JOIN', 'CROSS JOIN', 'JOIN', 'ON', 'ORDER BY', 'GROUP BY', 'HAVING', 'LIMIT', 'OFFSET', 'UNION', 'INSERT', 'UPDATE', 'SET', 'VALUES', 'INTO']
  let formatted = raw.replace(/\s+/g, ' ').trim()
  keywords.forEach(kw => {
    const re = new RegExp(`\\b(${kw})\\b`, 'gi')
    formatted = formatted.replace(re, '\n$1')
  })
  formatted = formatted.replace(/,\s*/g, ',\n    ')
  formatted = formatted.replace(/^\n/, '').trim()
  ed.setValue(formatted)
  ElMessage.success('已格式化')
}

function compressSql() {
  const ed = editor.value
  if (!ed) return
  const model = ed.getModel()
  if (!model) return
  const selection = ed.getSelection()
  // 判断是否有选中文本
  const hasSelection = selection && !selection.isEmpty()
  const raw = hasSelection ? model.getValueInRange(selection!) : ed.getValue()
  if (!raw.trim()) return
  // 压缩：保留注释，只合并非注释部分的多余空白
  const compressed = raw
    .split('\n')
    .map(line => {
      const trimmed = line.trim()
      // 保留单行注释（--开头）
      if (trimmed.startsWith('--')) return trimmed
      // 非注释行：合并多余空白为单空格
      return trimmed.replace(/\s+/g, ' ')
    })
    .filter(line => line.length > 0)
    .join('\n')
    // 相邻的非注释行合并为同一行
    .replace(/\n(?!--)/g, ' ')
    .replace(/ {2,}/g, ' ')
    .trim()
  if (hasSelection) {
    ed.executeEdits('compressSql', [{ range: selection!, text: compressed }])
  } else {
    ed.setValue(compressed)
  }
  ElMessage.success('已压缩')
}

/** 压缩 SQL 文本（去注释、合并空白）—— 纯函数，用于执行计划显示 */
function compressSqlText(sql: string): string {
  return sql
    .replace(/--[^\n]*/g, '')
    .replace(/\/\*[\s\S]*?\*\//g, '')
    .replace(/\s+/g, ' ')
    .trim()
}

// ========== 执行计划 ==========
const planVisible = ref(false)
const planText = ref('')
const planLoading = ref(false)
interface PlanOperator {
  physicalOp: string
  logicalOp: string
  costPercent: number
  estimatedRows: string
  estimatedRowsRead: string
  executions: string
  objectName: string
  nodeId: string
  estIoCost: string
  estCpuCost: string
  estSubtreeCost: string
  avgRowSize: string
  objectFullName: string
  outputColumns: string[]
}
interface PlanStatement {
  index: number
  sql: string
  costPercent: number
  subtreeCost: number
  estimatedRows: string
  operators: PlanOperator[]
}
const planStatements = ref<PlanStatement[]>([])

async function showExplainPlan() {
  const sql = getExecutableSql()
  if (!sql.trim()) {
    ElMessage.warning('请输入 SQL 语句')
    return
  }
  planVisible.value = true
  planLoading.value = true
  planText.value = ''
  planStatements.value = []
  try {
    const data = await httpPost<{ plan?: string; statements?: PlanStatement[] }>('/api/Common/SqlQuery/ExplainPlan',
      { sql, dataSource: activeDataSource.value, database: activeDatabase.value })
    planText.value = data.plan || '无执行计划'
    if (data.statements && data.statements.length > 0) {
      planStatements.value = data.statements
    }
  } catch (e) {
    planText.value = '✗ ' + ((e instanceof ApiError ? (e.responseBody.message as string) : null) || (e as Error).message)
  } finally {
    planLoading.value = false
  }
}

function clearEditor() {
  editor.value?.setValue('')
  if (activeTab.value) {
    activeTab.value.resultSets = []
    activeTab.value.activeResultIdx = 0
    activeTab.value.errorMsg = ''
  }
}

// ========== 单元格详情（已改为悬浮 tooltip 展示，保留复制功能供右键菜单使用） ==========
</script>

<template>
  <div class="sql-query">
    <!-- 页签栏 -->
    <div class="sql-tabs-bar">
      <div class="sql-tabs-scroll">
        <div
          v-for="tab in tabs" :key="tab.id"
          class="sql-tab" :class="{ active: tab.id === activeTabId }"
          @click="switchTab(tab.id)"
          @dblclick.stop="startRename(tab.id)"
        >
          <template v-if="renamingTabId === tab.id">
            <input
              class="tab-rename-input"
              v-model="renameInput"
              @blur="finishRename"
              @keyup.enter="finishRename"
              @keyup.escape="renamingTabId = ''"
              @click.stop
              autofocus
            />
          </template>
          <template v-else>
            <span class="sql-tab-name">{{ tab.name }}</span>
            <span v-if="tab.dataSource" class="sql-tab-ds" :title="'数据源：' + tab.dataSource">[{{ tab.dataSource }}]</span>
            <span v-if="tabs.length > 1" class="sql-tab-close" @click.stop="closeTab(tab.id)">&times;</span>
          </template>
        </div>
      </div>
      <button class="tab-add-btn" @click="addNewTab" title="新建查询 (Ctrl+N)">+</button>
    </div>

    <!-- 工具栏 -->
    <div class="toolbar">
      <el-button v-if="$has('sql-query:execute')" type="primary" size="small" @click="executeQuery" :disabled="executing" :loading="executing">
        {{ executing ? '执行中...' : '▶ 执行' }}
      </el-button>
      <el-button v-show="executing" size="small" type="danger" @click="cancelQuery">■ 取消执行</el-button>
      <el-button v-show="['sqlserver','mysql','postgresql','oracle'].includes(activeDsType)" size="small" type="warning" @click="queryLocks">🔒 查询锁表</el-button>
      <el-button size="small" @click="formatSql">格式化</el-button>
      <el-button size="small" @click="compressSql">压缩</el-button>
      <el-button size="small" @click="showExplainPlan">执行计划</el-button>
      <el-button size="small" @click="clearEditor">清空</el-button>
      <el-button size="small" @click="historyVisible = !historyVisible">历史</el-button>
      <el-button size="small" @click="openSnippetDialog">快捷输入</el-button>
      <el-button size="small" @click="openFavoriteDialog">收藏</el-button>
      <span class="toolbar-sep"></span>
      <span class="ctl-label">数据源</span>
      <el-select v-model="activeDataSource" size="small" style="width: 190px;">
        <el-option v-for="ds in dataSources" :key="ds.name" :label="`${ds.name} (${dbTypeLabel(ds.dbType)})`" :value="ds.name">
          <span>{{ ds.name }}</span>
          <span class="ds-opt-type">{{ dbTypeLabel(ds.dbType) }}</span>
        </el-option>
      </el-select>
      <el-button size="small" @click="dsDialogVisible = true" title="管理数据源" :icon="Setting" circle />
      <template v-if="databases.length">
        <span class="ctl-label">数据库</span>
        <el-select v-model="activeDatabase" size="small" style="width: 150px;" clearable filterable placeholder="默认库" title="执行 SQL 时的目标数据库（空 = 连接串默认库）">
          <el-option v-for="db in databases" :key="db" :label="db" :value="db" />
        </el-select>
      </template>
      <el-button size="small" @click="objExplorerVisible = !objExplorerVisible">{{ objExplorerVisible ? '隐藏对象' : '对象浏览' }}</el-button>
      <span class="ctl-label">每页</span>
      <el-select v-model="pageSize" size="small" style="width: 80px;">
        <el-option :value="50" label="50" />
        <el-option :value="100" label="100" />
        <el-option :value="200" label="200" />
        <el-option :value="500" label="500" />
      </el-select>
      <span class="hint">Ctrl+Enter 执行 | Ctrl+N 新建 | 选中文本可单独执行 | 双击标签重命名</span>
    </div>

    <!-- 历史记录面板 -->
    <div v-if="historyVisible" class="history-panel">
      <div v-if="!history.length" class="history-empty">暂无历史</div>
      <div v-for="(h, i) in history" :key="i" class="history-item" @click="loadHistory(h.sql)">
        <span class="history-time">{{ h.time }}</span>
        <span class="history-sql">{{ h.sql.slice(0, 120) }}</span>
      </div>
    </div>

    <!-- 主区域：左侧对象树 + 右侧编辑器/结果 -->
    <div class="main-area">
      <!-- 对象资源管理器 -->
      <div v-show="objExplorerVisible" ref="objExplorerEl" class="obj-explorer" :style="{ width: objExplorerWidth + 'px' }">
        <div class="obj-explorer-header">
          <span class="obj-explorer-title" :title="activeDataSource">{{ activeDataSource || '未选择数据源' }}</span>
          <el-button size="small" text :icon="Refresh" @click="reloadObjTree" title="刷新" />
        </div>
        <el-tree
          ref="objTreeRef"
          :key="objTreeKey"
          class="obj-tree"
          lazy
          :load="loadObjNode"
          :props="{ isLeaf: 'leaf' }"
          node-key="id"
          highlight-current
          :filter-node-method="filterObjNode"
          @node-click="onObjNodeClick"
        >
          <template #default="{ data }">
            <span class="obj-node" @dblclick.stop="onObjNodeDblClick(data)" @contextmenu="onObjNodeContextMenu($event, data)">
              <span class="obj-icon">{{ objIcon(data.type) }}</span>
              <span class="obj-label" :title="objNodeTitle(data)">{{ data.label }}</span>
              <template v-if="data.type === 'folder'">
                <el-icon
                  class="folder-filter-btn"
                  :class="{ active: !!folderFilters[data.id] || folderFilterOpenId === data.id }"
                  :title="folderFilters[data.id] ? '筛选中：' + folderFilters[data.id] : '筛选该文件夹'"
                  @click.stop="toggleFolderFilter(data.id)"
                ><Search /></el-icon>
                <span v-if="folderFilterOpenId === data.id" class="folder-filter-box" @click.stop @dblclick.stop>
                  <input
                    :ref="focusFolderFilter"
                    v-model="folderFilters[data.id]"
                    class="folder-filter-input"
                    placeholder="筛选"
                    @keyup.escape="closeFolderFilter(data.id)"
                    @blur="onFolderFilterBlur"
                  />
                  <!-- mousedown.prevent：清除时不夺焦点，避免触发 blur 收起输入框 -->
                  <span
                    v-if="folderFilters[data.id]"
                    class="folder-filter-clear"
                    title="清除筛选"
                    @mousedown.prevent.stop="clearFolderFilter(data.id)"
                  >×</span>
                </span>
                <!-- 失焦后有内容时以标签形式继续展示筛选内容，点击可重新编辑，× 清除（文字过长省略，× 始终可见） -->
                <span
                  v-else-if="folderFilters[data.id]"
                  class="folder-filter-tag"
                  :title="'筛选中：' + folderFilters[data.id] + '（点击编辑）'"
                  @click.stop="toggleFolderFilter(data.id)"
                  @dblclick.stop
                ><span class="folder-filter-tag-text">{{ folderFilters[data.id] }}</span><span class="folder-filter-tag-close" title="清除筛选" @click.stop="clearFolderFilter(data.id)">×</span></span>
              </template>
              <span v-if="data.suffix && folderFilterOpenId !== data.id && !folderFilters[data.id]" class="obj-suffix">({{ data.suffix }})</span>
            </span>
          </template>
        </el-tree>
      </div>
      <!-- 拖拽分隔条 -->
      <div v-show="objExplorerVisible" class="obj-resizer" @mousedown="startObjResize"></div>

      <div class="main-right">
    <!-- SQL 编辑器 -->
    <div v-show="layoutMode !== 'resultMax'" ref="editorWrapEl" class="editor-wrap" :class="{ 'editor-fullscreen': isEditorFullscreen }" :style="editorStyle">
      <div ref="editorEl" class="sql-editor"></div>
      <el-button class="editor-fullscreen-btn" :icon="FullScreen" circle size="small" :title="isEditorFullscreen ? '退出全屏' : '全屏编辑'" @click="toggleEditorFullscreen" />
    </div>

    <!-- 水平分隔条：拖拽调高（全屏下拖拽可直接恢复分屏）+ 全屏切换 -->
    <div class="h-resizer" @mousedown="startEditorResize">
      <button class="h-resizer-btn" :class="{ active: layoutMode === 'resultMax' }" @mousedown.stop @click.stop="toggleResultMax" :title="layoutMode === 'resultMax' ? '还原' : '结果区全屏'">▲</button>
      <button class="h-resizer-btn" :class="{ active: layoutMode === 'editorMax' }" @mousedown.stop @click.stop="toggleEditorMax" :title="layoutMode === 'editorMax' ? '还原' : '编辑器全屏'">▼</button>
    </div>

    <!-- 结果区 -->
    <div v-show="layoutMode !== 'editorMax'" ref="resultAreaEl" class="result-area" :class="{ 'result-fullscreen': isResultFullscreen }">
      <el-button
        class="result-fullscreen-btn"
        :icon="FullScreen"
        circle
        size="small"
        :title="isResultFullscreen ? '退出全屏' : '结果区全屏'"
        @click="toggleResultFullscreen"
      />
      <!-- 执行中状态 -->
      <div v-if="executing" class="result-loading">
        <span class="loading-spinner"></span>
        <span>正在执行查询...</span>
      </div>

      <!-- 错误提示 -->
      <div v-else-if="errorMsg" class="result-error">
        <span class="error-icon">✗</span> {{ errorMsg }}
      </div>

      <!-- 无结果占位 -->
      <div v-else-if="!result && !executing" class="result-empty">
        按 Ctrl+Enter 执行查询
      </div>

      <!-- 多结果集标签 -->
      <div v-if="resultSets.length > 1" class="result-tabs">
        <button
          v-for="(rs, idx) in resultSets" :key="idx"
          class="result-tab" :class="{ active: activeResultIdx === idx }"
          @click="switchResultSet(idx)"
        >结果集 {{ idx + 1 }} <span class="tab-count">({{ rs.totalRows }}行)</span></button>
      </div>

      <!-- 统计 + 复制操作栏 -->
      <div v-if="result" class="result-stats">
        <span>共 <b>{{ totalRows }}</b> 行</span>
        <span class="time-badge">耗时 {{ executionTime }}ms</span>
        <span class="toolbar-sep"></span>
        <el-button size="small" text @click="copyColumnNames">复制列名</el-button>
        <el-button size="small" text @click="copyCurrentPage">复制当前页</el-button>
        <el-button size="small" text @click="copySelectedRows" :disabled="selectedRows.size === 0">复制选中行</el-button>
        <el-button size="small" text @click="copySelectedCol" :disabled="!selectedCol">复制选中列</el-button>
        <el-button size="small" text type="success" @click="exportExcel" :loading="exporting">导出 Excel</el-button>
        <span v-if="selectedRows.size" class="sel-info">已选 {{ selectedRows.size }} 行</span>
        <span v-if="selectedCol" class="sel-info">已选列: {{ selectedCol }}</span>
      </div>

      <!-- 分页 -->
      <div v-if="result && totalPages > 1" class="pagination">
        <el-pagination
          v-model:current-page="currentPage"
          :page-size="pageSize"
          :total="totalRows"
          layout="prev, pager, next, jumper, ->, total"
          small
          background
        />
      </div>

      <!-- 数据表格 -->
      <div v-if="result && tableColumns.length" ref="resultTableWrap" class="result-table-wrap">
        <el-table
          :data="pagedRows"
          border
          stripe
          size="small"
          :height="'100%'"
          @cell-click="onTableCellClick"
          @cell-contextmenu="onTableCellContextMenu"
          @header-click="onTableHeaderClick"
          @header-contextmenu="onTableHeaderContextMenu"
          style="font-family: 'Consolas', monospace; font-size: 12px;"
        >
          <el-table-column type="index" label="#" width="60" align="center" fixed :index="rowIndexLabel" />
          <el-table-column
            v-for="col in tableColumns" :key="col"
            :prop="col"
            :label="col"
            :min-width="colWidths[col] || 80"
            show-overflow-tooltip
          >
            <template #header>
              <span @dblclick.stop="copyToClipboard(col, '列名 ' + col + ' 已复制')">{{ col }}</span>
            </template>
          </el-table-column>
        </el-table>
      </div>
    </div>
      </div><!-- /main-right -->
    </div><!-- /main-area -->

    <!-- 执行计划弹层 -->
    <CommonDialog v-model="planVisible" title="执行计划" width="85%" :close-on-click-modal="true" class="stretch-dialog">
      <div v-loading="planLoading" style="min-height: 150px; max-height: 70vh; overflow: auto;">
        <!-- 结构化显示（SQL Server XML 解析成功时） -->
        <template v-if="planStatements.length">
          <div v-for="stmt in planStatements" :key="stmt.index" class="plan-stmt">
            <div class="plan-stmt-header">
              <span class="plan-stmt-title">查询 {{ stmt.index }}：查询开销（占总批）：{{ stmt.costPercent }}%</span>
            </div>
            <div class="plan-stmt-sql">{{ compressSqlText(stmt.sql) }}</div>
            <div class="plan-ops">
              <div v-for="(op, idx) in stmt.operators" :key="idx" class="plan-op-node">
                <el-popover placement="bottom" :width="420" trigger="hover" :show-after="200">
                  <template #reference>
                    <div class="plan-op-box">
                      <div class="plan-op-icon">🗂️</div>
                      <div class="plan-op-info">
                        <div class="plan-op-name">{{ op.physicalOp }} ({{ op.logicalOp }})</div>
                        <div v-if="op.objectName" class="plan-op-object">{{ op.objectName }}</div>
                        <div class="plan-op-detail">开销：{{ op.costPercent }} %</div>
                        <div class="plan-op-detail">估计行数：{{ op.estimatedRows }}</div>
                        <div v-if="op.estimatedRowsRead" class="plan-op-detail">读取行数：{{ op.estimatedRowsRead }}</div>
                      </div>
                    </div>
                  </template>
                  <!-- 悬浮详情内容 -->
                  <div class="plan-tooltip">
                    <div class="plan-tooltip-title">{{ op.physicalOp }} ({{ op.logicalOp }})</div>
                    <table class="plan-tooltip-table">
                      <tr><td class="plan-tooltip-label">物理运算</td><td>{{ op.physicalOp }}</td></tr>
                      <tr><td class="plan-tooltip-label">逻辑操作</td><td>{{ op.logicalOp }}</td></tr>
                      <tr v-if="op.estIoCost"><td class="plan-tooltip-label">估计 I/O 开销</td><td>{{ op.estIoCost }}</td></tr>
                      <tr v-if="op.estCpuCost"><td class="plan-tooltip-label">估计 CPU 开销</td><td>{{ op.estCpuCost }}</td></tr>
                      <tr v-if="op.estSubtreeCost"><td class="plan-tooltip-label">估计运算符开销</td><td>{{ op.estSubtreeCost }} ({{ op.costPercent }}%)</td></tr>
                      <tr><td class="plan-tooltip-label">估计子树大小</td><td>{{ op.estSubtreeCost }}</td></tr>
                      <tr><td class="plan-tooltip-label">估计执行次数</td><td>{{ op.executions }}</td></tr>
                      <tr><td class="plan-tooltip-label">估计行数</td><td>{{ op.estimatedRows }}</td></tr>
                      <tr v-if="op.estimatedRowsRead"><td class="plan-tooltip-label">要读取的预计行数</td><td>{{ op.estimatedRowsRead }}</td></tr>
                      <tr v-if="op.avgRowSize"><td class="plan-tooltip-label">估计行大小</td><td>{{ op.avgRowSize }} 字节</td></tr>
                      <tr v-if="op.nodeId"><td class="plan-tooltip-label">节点 ID</td><td>{{ op.nodeId }}</td></tr>
                    </table>
                    <template v-if="op.objectFullName">
                      <div class="plan-tooltip-section">对象</div>
                      <div class="plan-tooltip-mono">{{ op.objectFullName }}</div>
                    </template>
                    <template v-if="op.outputColumns && op.outputColumns.length">
                      <div class="plan-tooltip-section">输出列表</div>
                      <div class="plan-tooltip-mono">{{ op.outputColumns.join(', ') }}</div>
                    </template>
                  </div>
                </el-popover>
                <div v-if="idx < stmt.operators.length - 1" class="plan-op-arrow">→</div>
              </div>
            </div>
          </div>
        </template>
        <!-- 回退：纯文本显示（MySQL/PG/SQLite 或 XML 解析失败） -->
        <pre v-else class="plan-text">{{ planText }}</pre>
      </div>
    </CommonDialog>

    <!-- 查询锁表弹窗 -->
    <CommonDialog v-model="lockDialogVisible" title="🔒 查询锁表" width="90%" :close-on-click-modal="true" class="stretch-dialog">
      <div class="lock-dialog-body">
        <!-- 工具栏 -->
        <div class="lock-toolbar">
          <el-button size="small" type="primary" @click="executeLockQuery" :loading="lockLoading">刷新查询</el-button>
          <el-button size="small" @click="lockExplainPlan">执行计划</el-button>
          <el-button size="small" @click="copyLockSql">复制语句</el-button>
        </div>
        <!-- SQL 语句展示 -->
        <details class="lock-sql-details">
          <summary>查询语句 / 杀死语句</summary>
          <div class="lock-sql-section">
            <div class="lock-sql-label">查询锁表语句：</div>
            <pre class="lock-sql-code">{{ lockSql }};</pre>
            <div class="lock-sql-label" style="margin-top:10px;">杀死锁表进程语句：</div>
            <pre class="lock-sql-code">{{ lockKillSql }}</pre>
          </div>
        </details>
        <!-- 查询结果 -->
        <div v-loading="lockLoading" class="lock-result-area">
          <div v-if="lockError" class="lock-error">✗ {{ lockError }}</div>
          <template v-else-if="lockResult">
            <div class="lock-result-info">共 {{ lockResult.rows.length }} 行</div>
            <div class="lock-table-wrap">
              <el-table :data="lockResult.rows" border stripe size="small" max-height="400" style="font-family: 'Consolas', monospace; font-size: 12px;">
                <el-table-column type="index" label="#" width="50" align="center" />
                <el-table-column v-for="col in lockResult.columns" :key="col" :prop="col" :label="col" min-width="120" />
              </el-table>
            </div>
          </template>
          <div v-else-if="!lockLoading" class="lock-empty">暂无数据</div>
        </div>
      </div>
    </CommonDialog>

    <!-- 数据源管理弹层 -->
    <CommonDialog v-model="dsDialogVisible" title="数据源管理" width="1100px" :close-on-click-modal="true" @close="resetDsForm">
      <!-- 名称搜索 -->
      <el-input v-model="dsSearchKeyword" size="small" placeholder="搜索数据源名称" clearable style="width: 220px; margin-bottom: 10px;" />
      <!-- 已有数据源列表 -->
      <el-table :data="filteredDataSources" border size="small" max-height="320" style="margin-bottom: 16px;" :row-class-name="dsRowClass">
        <el-table-column prop="name" label="名称" width="130" show-overflow-tooltip />
        <el-table-column label="类型" width="130">
          <template #default="{ row }">{{ dbTypeLabel(row.dbType) }}</template>
        </el-table-column>
        <el-table-column prop="connectionString" label="连接字符串" show-overflow-tooltip />
        <el-table-column label="操作" width="190" align="center" :show-overflow-tooltip="false">
          <template #default="{ row }">
            <div class="ds-op-btns">
              <el-button v-if="$has('sql-query:test-connection')" size="small" text @click="testDataSourceRow(row as DataSourceItem)" :loading="testingRowName === row.name">测试连接</el-button>
              <!-- 内置数据源（ConvenientSystemDb）不允许修改删除，不显示按钮 -->
              <template v-if="!row.isBuiltIn">
                <el-button size="small" text type="primary" @click="startEditDs(row as DataSourceItem)">修改</el-button>
                <el-button v-if="$has('sql-query:delete-datasource')" size="small" text type="danger" @click="removeDataSource(row as DataSourceItem)">删除</el-button>
              </template>
            </div>
          </template>
        </el-table-column>
      </el-table>
      <!-- 新增/编辑表单 -->
      <div class="ds-form-title">{{ isEditingDs ? '修改数据源' : '新增数据源' }}</div>
      <div class="ds-add-form">
        <div class="ds-form-row">
          <el-input v-model="newDsName" size="small" placeholder="名称" style="width: 130px;" />
          <el-select v-model="newDsDbType" size="small" style="width: 130px;" placeholder="数据库类型" @change="onDbTypeChange">
            <el-option v-for="opt in dbTypeOptions" :key="opt.value" :label="opt.label" :value="opt.value" />
          </el-select>
          <el-button v-if="$has('sql-query:test-connection')" size="small" @click="testDataSource" :loading="testingDs">测试连接</el-button>
          <el-button v-if="$has('sql-query:save-datasource')" size="small" type="primary" @click="addDataSource">{{ isEditingDs ? '保存' : '添加' }}</el-button>
          <el-button v-if="isEditingDs" size="small" @click="resetDsForm">取消</el-button>
        </div>
        <el-input
          v-model="newDsConnStr"
          type="textarea"
          :autosize="{ minRows: 3, maxRows: 8 }"
          placeholder="连接字符串"
        />
      </div>
    </CommonDialog>

    <!-- 生成语句弹窗：每段语句可单独复制，勾选后可合并复制 -->
    <CommonDialog v-model="scriptDialogVisible" :title="scriptDialogTitle" width="70%" :close-on-click-modal="true" class="stretch-dialog">
      <div class="script-block-list">
        <div v-for="(b, i) in scriptBlocks" :key="i" class="script-block">
          <div class="script-block-head">
            <el-checkbox v-model="b.checked" size="small">选择</el-checkbox>
            <el-button size="small" text type="primary" @click="copyToClipboard(b.text, '语句已复制')">复制</el-button>
          </div>
          <pre class="script-block-text">{{ b.text }}</pre>
        </div>
      </div>
      <template #footer>
        <el-button size="small" :disabled="checkedScriptCount === 0" @click="copyCheckedScripts">复制选中（{{ checkedScriptCount }}）</el-button>
        <el-button size="small" type="primary" @click="copyAllScripts">复制全部</el-button>
        <el-button size="small" @click="scriptDialogVisible = false">关闭</el-button>
      </template>
    </CommonDialog>

    <!-- 单元格右键菜单 -->
    <div
      v-if="ctxMenu.visible"
      class="cell-ctx-menu"
      :style="{ left: ctxMenu.x + 'px', top: ctxMenu.y + 'px' }"
      @contextmenu.prevent
    >
      <div class="ctx-item" @click="ctxCopyCell">复制单元格</div>
      <div class="ctx-item" @click="ctxCopyRow">复制整行</div>
      <div class="ctx-item" @click="ctxCopyCol">复制整列</div>
      <div class="ctx-item" @click="ctxCopyCellColName">复制列名</div>
      <div class="ctx-separator"></div>
      <div class="ctx-item" @click="ctxCopyColNames">复制全部列名</div>
    </div>
    <!-- 表头右键菜单 -->
    <div
      v-if="headerCtxMenu.visible"
      class="cell-ctx-menu"
      :style="{ left: headerCtxMenu.x + 'px', top: headerCtxMenu.y + 'px' }"
      @contextmenu.prevent
    >
      <div class="ctx-item" @click="headerCopyName">复制列名 "{{ headerCtxMenu.col }}"</div>
      <div class="ctx-item" @click="headerCopyColData">复制该列数据</div>
      <div class="ctx-separator"></div>
      <div class="ctx-item" @click="headerCopyAllNames">复制全部列名</div>
    </div>
    <!-- 对象树右键菜单 -->
    <div
      v-if="objCtxMenu.visible"
      class="cell-ctx-menu"
      :style="{ left: objCtxMenu.x + 'px', top: objCtxMenu.y + 'px' }"
      @contextmenu.prevent
    >
      <template v-if="objCtxNode?.type === 'table' || objCtxNode?.type === 'view'">
        <div class="ctx-item" @click="objQueryData()">查询数据</div>
        <div class="ctx-item" @click="objQueryData(100)">查询前 100 行</div>
        <div class="ctx-separator"></div>
      </template>
      <div v-if="objCtxNode?.type === 'table' || objCtxNode?.type === 'view'" class="ctx-item" @click="objGenSelect">生成查询语句</div>
      <div class="ctx-item" @click="objGenCreate">{{ objCtxNode?.type === 'table' ? '生成建表语句' : '生成创建语句' }}</div>
      <div v-if="objCtxNode?.type === 'table'" class="ctx-item" @click="objGenAlter">生成修改语句</div>
      <div v-if="objCtxNode?.type === 'table'" class="ctx-item" @click="objGenAll">生成所有语句</div>
      <div class="ctx-separator"></div>
      <div class="ctx-item" @click="objCopyName">复制完整名称</div>
    </div>
  </div>

    <!-- 快捷输入管理弹窗 -->
    <CommonDialog v-model="snippetDialogVisible" title="快捷输入管理" width="750px" :close-on-click-modal="true" @close="resetSnippetForm">
      <el-table :data="snippets" border size="small" max-height="300" style="margin-bottom: 16px;">
        <el-table-column prop="shortcut" label="快捷缩写" width="100" />
        <el-table-column prop="expansion" label="展开内容" />
        <el-table-column prop="remark" label="备注" width="120" />
        <el-table-column prop="sortOrder" label="排序" width="60" align="center" />
        <el-table-column label="操作" width="120" align="center" :show-overflow-tooltip="false">
          <template #default="{ row }">
            <div class="ds-op-btns">
              <el-button size="small" text type="primary" @click="editSnippet(row as SnippetItem)">修改</el-button>
              <el-button size="small" text type="danger" @click="removeSnippet(row as SnippetItem)">删除</el-button>
            </div>
          </template>
        </el-table-column>
      </el-table>
      <div class="ds-form-title">{{ isEditingSnippet ? '修改快捷输入' : '新增快捷输入' }}</div>
      <div class="ds-add-form">
        <div class="ds-form-row">
          <el-input v-model="snippetForm.shortcut" size="small" placeholder="快捷缩写（如 sf）" style="width: 120px;" />
          <el-input v-model="snippetForm.remark" size="small" placeholder="备注（可选）" style="width: 140px;" />
          <el-input-number v-model="snippetForm.sortOrder" size="small" :min="0" :max="9999" controls-position="right" style="width: 100px;" placeholder="排序" />
          <el-button size="small" type="primary" @click="saveSnippet">{{ isEditingSnippet ? '保存' : '添加' }}</el-button>
          <el-button v-if="isEditingSnippet" size="small" @click="resetSnippetForm">取消</el-button>
        </div>
        <el-input
          v-model="snippetForm.expansion"
          type="textarea"
          :autosize="{ minRows: 2, maxRows: 6 }"
          placeholder="展开内容（如 SELECT * FROM ）"
        />
      </div>
      <template #footer>
        <div style="display: flex; justify-content: space-between; align-items: center;">
          <span class="snippet-hint">提示：在编辑器中输入快捷缩写后按 Enter 选择即可展开</span>
          <el-button size="small" type="danger" @click="resetSnippets">重置为初始数据</el-button>
        </div>
      </template>
    </CommonDialog>

<!-- SQL 查询收藏列表弹窗 -->
    <CommonDialog v-model="favDialogVisible" title="SQL 查询收藏" width="600px" :close-on-click-modal="true">
      <div style="margin-bottom: 12px;">
        <el-button size="small" type="primary" @click="openSaveFavDialog">➕ 收藏当前 SQL</el-button>
      </div>
      <el-table :data="favorites" border size="small" max-height="350">
        <el-table-column prop="name" label="名称" min-width="120" />
        <el-table-column prop="remark" label="备注" width="120" show-overflow-tooltip />
        <el-table-column prop="dataSource" label="数据源" width="100" show-overflow-tooltip />
        <el-table-column label="操作" width="120" fixed="right">
          <template #default="{ row }">
            <el-button link size="small" type="primary" @click="loadFavorite(row as FavoriteItem)">加载</el-button>
            <el-button link size="small" type="danger" @click="removeFavorite(row as FavoriteItem)">删除</el-button>
          </template>
        </el-table-column>
        <template #empty>
          <div style="padding: 20px; color: #909399;">暂无收藏，点击“收藏当前 SQL”添加</div>
        </template>
      </el-table>
    </CommonDialog>

<!-- 保存收藏弹窗 -->
    <CommonDialog v-model="favSaveVisible" title="保存 SQL 收藏" width="450px" :close-on-click-modal="true">
      <div style="display: flex; flex-direction: column; gap: 12px;">
        <el-input v-model="favForm.name" placeholder="收藏名称（必填）" />
        <el-input v-model="favForm.remark" placeholder="备注（可选）" />
        <el-input v-model="favForm.dataSource" placeholder="绑定数据源（可选，加载时自动切换）" />
      </div>
      <template #footer>
        <el-button @click="favSaveVisible = false">取消</el-button>
        <el-button type="primary" @click="saveFavorite">保存</el-button>
      </template>
    </CommonDialog>
</template>

<style scoped>
.sql-query {
  display: flex;
  flex-direction: column;
  height: 100%;
  overflow: hidden;
  background: #fff;
}

/* 页签栏 */
.sql-tabs-bar {
  display: flex;
  align-items: stretch;
  background: #f0f2f5;
  border-bottom: 1px solid #e4e7ed;
  flex-shrink: 0;
  min-height: 34px;
}
.sql-tabs-scroll {
  display: flex;
  align-items: flex-end;
  gap: 1px;
  padding: 4px 8px 0;
  overflow-x: auto;
  overflow-y: hidden;
  flex: 1;
}
.sql-tab {
  display: flex;
  align-items: center;
  gap: 4px;
  padding: 5px 12px;
  border-radius: 4px 4px 0 0;
  cursor: pointer;
  font-size: 13px;
  white-space: nowrap;
  color: #606266;
  border: 1px solid transparent;
  border-bottom: none;
  position: relative;
  top: 1px;
  user-select: none;
  background: #e8eaed;
  transition: all .12s;
}
.sql-tab:hover { background: #fff; }
.sql-tab.active {
  background: #fff;
  border-color: #e4e7ed;
  color: #409eff;
  font-weight: 500;
}
.sql-tab-name {
  max-width: 140px;
  overflow: hidden;
  text-overflow: ellipsis;
}
.sql-tab-ds {
  font-size: 11px;
  color: #909399;
  max-width: 110px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.sql-tab.active .sql-tab-ds { color: #79bbff; }
.sql-tab-close {
  font-size: 15px;
  color: #909399;
  border-radius: 50%;
  width: 16px;
  height: 16px;
  display: flex;
  align-items: center;
  justify-content: center;
  line-height: 1;
}
.sql-tab-close:hover { background: #dcdfe6; color: #f56c6c; }
.tab-rename-input {
  width: 180px;
  border: 1px solid #409eff;
  border-radius: 2px;
  padding: 1px 4px;
  font-size: 12px;
  outline: none;
}
.tab-add-btn {
  border: none;
  background: transparent;
  font-size: 18px;
  color: #606266;
  cursor: pointer;
  padding: 0 12px;
  align-self: center;
  transition: color .15s;
}
.tab-add-btn:hover { color: #409eff; }

.toolbar {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 8px 12px;
  border-bottom: 1px solid #e4e7ed;
  flex-shrink: 0;
  background: #f5f7fa;
  flex-wrap: wrap;
}
.toolbar-sep { width: 1px; height: 20px; background: #dcdfe6; flex-shrink: 0; }
.ctl-label { font-size: 12px; color: #606266; white-space: nowrap; }
.hint { font-size: 12px; color: #909399; margin-left: auto; }

.result-tabs {
  display: flex;
  gap: 0;
  padding: 0 12px;
  background: #f5f7fa;
  border-bottom: 1px solid #e4e7ed;
}
.result-tab {
  padding: 6px 16px;
  border: none;
  background: transparent;
  font-size: 13px;
  color: #606266;
  cursor: pointer;
  border-bottom: 2px solid transparent;
  transition: all .15s;
}
.result-tab:hover { color: #409eff; }
.result-tab.active { color: #409eff; border-bottom-color: #409eff; font-weight: 500; }
.tab-count { font-size: 11px; color: #909399; margin-left: 2px; }

.history-panel { max-height: 150px; overflow-y: auto; border-bottom: 1px solid #e4e7ed; background: #fafafa; }
.history-empty { padding: 10px; color: #909399; font-size: 12px; text-align: center; }
.history-item { padding: 6px 12px; cursor: pointer; font-size: 12px; border-bottom: 1px solid #f2f6fc; display: flex; gap: 10px; transition: background .1s; }
.history-item:hover { background: #ecf5ff; }
.history-time { color: #909399; flex-shrink: 0; }
.history-sql { color: #303133; font-family: monospace; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }

.sql-editor { height: 100%; min-height: 0; border-bottom: 1px solid #e4e7ed; }

/* 编辑器容器 + 全屏按钮 */
.editor-wrap { position: relative; flex-shrink: 0; }
.editor-wrap.editor-fullscreen {
  background: #fff;
  padding: 12px;
}
.editor-fullscreen-btn {
  position: absolute;
  top: 6px;
  right: 6px;
  z-index: 10;
  opacity: 0.5;
  transition: opacity 0.2s;
}
.editor-fullscreen-btn:hover {
  opacity: 1;
}

/* 水平分隔条：拖拽调整编辑器高度 + 全屏按钮 */
.h-resizer {
  height: 10px;
  flex-shrink: 0;
  cursor: row-resize;
  background: #f5f7fa;
  border-bottom: 1px solid #e4e7ed;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 16px;
  transition: background .15s;
}
.h-resizer:hover { background: #ecf5ff; }
.h-resizer-btn {
  border: none;
  background: transparent;
  color: #909399;
  font-size: 9px;
  line-height: 1;
  cursor: pointer;
  padding: 0 8px;
}
.h-resizer-btn:hover { color: #409eff; }
.h-resizer-btn.active { color: #409eff; font-weight: bold; }

/* 主区域：左树右主区 */
.main-area { flex: 1; display: flex; overflow: hidden; min-height: 0; }
.main-right { flex: 1; display: flex; flex-direction: column; overflow: hidden; min-width: 0; }

/* 对象资源管理器 */
.obj-explorer {
  flex-shrink: 0;
  border-right: 1px solid #e4e7ed;
  display: flex;
  flex-direction: column;
  background: #fafbfc;
}
.obj-resizer {
  width: 5px;
  flex-shrink: 0;
  cursor: col-resize;
  background: transparent;
  transition: background .15s;
}
.obj-resizer:hover { background: #c6e2ff; }
.obj-explorer-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 4px 8px;
  border-bottom: 1px solid #e4e7ed;
  font-size: 12px;
  color: #606266;
  flex-shrink: 0;
}
.obj-explorer-title {
  font-weight: 600;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.obj-tree {
  flex: 1;
  overflow: auto;
  background: transparent;
  font-size: 12px;
}
.obj-node {
  display: flex;
  align-items: center;
  gap: 4px;
  overflow: hidden;
  flex: 1;
  min-width: 0;
}
.obj-icon { flex-shrink: 0; font-size: 12px; }
.obj-label { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.obj-suffix { color: #909399; font-size: 11px; flex-shrink: 0; }
.folder-filter-btn {
  flex-shrink: 0;
  padding: 0 2px;
  font-size: 16px;
  color: #c0c4cc;
  visibility: hidden;
  cursor: pointer;
}
.obj-node:hover .folder-filter-btn { visibility: visible; }
.folder-filter-btn:hover { color: #409eff; }
.folder-filter-btn.active { visibility: visible; color: #409eff; }
.folder-filter-box {
  flex: 1;
  min-width: 60px;
  display: inline-flex;
  align-items: center;
  gap: 2px;
  margin-right: 8px;
  border: 1px solid #409eff;
  border-radius: 2px;
  padding: 1px 4px;
  background: #fff;
}
.folder-filter-input {
  flex: 1;
  min-width: 0;
  border: none;
  font-size: 12px;
  outline: none;
  background: transparent;
}
.folder-filter-clear {
  flex-shrink: 0;
  padding: 0 2px;
  font-size: 13px;
  line-height: 1;
  color: #909399;
  cursor: pointer;
}
.folder-filter-clear:hover { color: #f56c6c; }
.folder-filter-tag {
  flex-shrink: 1;
  display: inline-flex;
  align-items: center;
  gap: 2px;
  max-width: 110px;
  min-width: 0;
  margin-right: 8px;
  padding: 0 4px;
  border: 1px solid #b3d8ff;
  border-radius: 2px;
  background: #ecf5ff;
  color: #409eff;
  font-size: 12px;
  white-space: nowrap;
  cursor: pointer;
}
/* 文字单独截断省略，避免把后面的 × 清除按钮一起裁掉 */
.folder-filter-tag-text {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
}
.folder-filter-tag-close {
  flex-shrink: 0;
  padding: 0 2px;
  font-size: 13px;
  line-height: 1;
  color: #909399;
}
.folder-filter-tag-close:hover { color: #f56c6c; }

.result-area { flex: 1; overflow: hidden; display: flex; flex-direction: column; position: relative; }

/* 结果区全屏按钮 */
.result-fullscreen-btn {
  position: absolute;
  top: 6px;
  right: 6px;
  z-index: 10;
  opacity: 0.5;
  transition: opacity 0.2s;
}
.result-fullscreen-btn:hover {
  opacity: 1;
}

/* 浏览器全屏状态下：结果区撑满屏幕，补充背景色和内边距 */
.result-area.result-fullscreen {
  background: #fff;
  padding: 12px;
}
.result-loading {
  padding: 30px;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 10px;
  color: #409eff;
  font-size: 14px;
}
.loading-spinner {
  width: 18px;
  height: 18px;
  border: 2px solid #409eff;
  border-top-color: transparent;
  border-radius: 50%;
  animation: spin .8s linear infinite;
}
@keyframes spin { to { transform: rotate(360deg); } }
.result-empty {
  padding: 30px;
  text-align: center;
  color: #909399;
  font-size: 13px;
}
.result-error { padding: 10px 14px; background: #fef0f0; color: #f56c6c; font-size: 13px; flex-shrink: 0; }
.error-icon { font-weight: bold; }

.result-stats {
  padding: 6px 12px;
  font-size: 12px;
  color: #606266;
  display: flex;
  align-items: center;
  gap: 8px;
  border-bottom: 1px solid #f2f6fc;
  flex-shrink: 0;
  flex-wrap: wrap;
}
.time-badge { color: #909399; }
.sel-info { color: #409eff; font-weight: 500; }

.pagination {
  padding: 5px 12px;
  display: flex;
  align-items: center;
  border-bottom: 1px solid #f2f6fc;
  flex-shrink: 0;
  font-size: 12px;
}

.result-table-wrap { flex: 1; overflow: hidden; }

/* el-table 选中样式覆盖（高亮类名由 JS 直接操作 DOM 添加，列高亮由动态样式规则实现） */
:deep(.el-table) {
  .row-selected td.el-table__cell { background-color: #d9ecff !important; }
  .el-table__cell.cell-selected { background-color: #d9ecff !important; outline: 2px solid #409eff; outline-offset: -1px; }
  th.el-table__cell { cursor: pointer; user-select: none; }
  td.el-table__cell { cursor: pointer; }
}

/* 执行计划 */
.plan-text {
  margin: 0;
  font-size: 12px;
  font-family: 'Consolas', monospace;
  white-space: pre-wrap;
  word-break: break-all;
  line-height: 1.6;
  color: #303133;
}
/* 执行计划 - 结构化显示 */
.plan-stmt {
  margin-bottom: 24px;
  padding-bottom: 16px;
  border-bottom: 1px solid #e4e7ed;
}
.plan-stmt:last-child { border-bottom: none; }
.plan-stmt-header {
  margin-bottom: 4px;
}
.plan-stmt-title {
  font-weight: bold;
  font-size: 13px;
  color: #c00;
}
.plan-stmt-sql {
  font-family: 'Consolas', monospace;
  font-size: 12px;
  color: #303133;
  margin-bottom: 12px;
  white-space: pre-wrap;
  word-break: break-all;
}
.plan-ops {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-start;
  gap: 4px;
}
.plan-op-node {
  display: flex;
  align-items: center;
  gap: 4px;
}
.plan-op-box {
  display: flex;
  align-items: flex-start;
  gap: 6px;
  border: 1px solid #dcdfe6;
  border-radius: 4px;
  padding: 6px 10px;
  background: #fafafa;
  min-width: 160px;
}
.plan-op-icon {
  font-size: 20px;
  line-height: 1;
  margin-top: 2px;
}
.plan-op-info {
  font-size: 11px;
  line-height: 1.5;
}
.plan-op-name {
  font-weight: bold;
  color: #303133;
  font-size: 12px;
}
.plan-op-object {
  color: #606266;
  font-family: 'Consolas', monospace;
}
.plan-op-detail {
  color: #909399;
}
.plan-op-arrow {
  font-size: 18px;
  color: #909399;
  padding: 0 2px;
}
/* 执行计划 - 悬浮详情 */
.plan-tooltip-title {
  font-weight: bold;
  font-size: 13px;
  margin-bottom: 8px;
  color: #303133;
}
.plan-tooltip-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 12px;
  margin-bottom: 8px;
}
.plan-tooltip-table td {
  padding: 2px 6px;
  border-bottom: 1px solid #f0f0f0;
  line-height: 1.6;
}
.plan-tooltip-label {
  font-weight: bold;
  white-space: nowrap;
  color: #303133;
  width: 130px;
}
.plan-tooltip-table td:last-child {
  color: #409eff;
  text-align: right;
}
.plan-tooltip-section {
  font-weight: bold;
  font-size: 12px;
  margin: 6px 0 2px;
  color: #303133;
}
.plan-tooltip-mono {
  font-family: 'Consolas', monospace;
  font-size: 11px;
  color: #606266;
  word-break: break-all;
  line-height: 1.5;
}

/* 生成语句弹窗 */
.script-block-list {
  max-height: 60vh;
  overflow: auto;
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.script-block {
  border: 1px solid #e4e7ed;
  border-radius: 4px;
  background: #fafafa;
}
.script-block-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 2px 10px;
  border-bottom: 1px solid #ebeef5;
  background: #f5f7fa;
  border-radius: 4px 4px 0 0;
}
.script-block-text {
  margin: 0;
  padding: 8px 10px;
  font-size: 12px;
  font-family: 'Consolas', monospace;
  white-space: pre-wrap;
  word-break: break-all;
  line-height: 1.6;
  color: #303133;
}

/* 右键菜单 */
.cell-ctx-menu {
  position: fixed;
  z-index: 9999;
  min-width: 130px;
  background: #fff;
  border: 1px solid #e4e7ed;
  border-radius: 4px;
  box-shadow: 0 2px 12px rgba(0,0,0,.12);
  padding: 4px 0;
  user-select: none;
}
.ctx-item {
  padding: 7px 16px;
  font-size: 13px;
  cursor: pointer;
  color: #606266;
  white-space: nowrap;
}
.ctx-item:hover { background: #ecf5ff; color: #409eff; }
.ctx-separator { height: 1px; margin: 4px 0; background: #e4e7ed; }

/* 数据源管理 */
.ds-opt-type {
  float: right;
  margin-left: 12px;
  font-size: 12px;
  color: #909399;
}
.ds-op-btns {
  display: flex;
  align-items: center;
  justify-content: center;
  flex-wrap: nowrap;
  gap: 4px;
}
.ds-op-btns .el-button { margin: 0; padding: 2px 6px; }
/* 内置数据源（ConvenientSystemDb）行置灰：不可修改删除 */
:deep(.ds-row-builtin td.el-table__cell) {
  color: #a8abb2;
  background-color: #f5f7fa !important;
}
.ds-form-title {
  font-size: 13px;
  font-weight: 600;
  color: #303133;
  margin-bottom: 10px;
}
.ds-add-form {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding-top: 10px;
  border-top: 1px solid #e4e7ed;
}
.ds-form-row {
  display: flex;
  gap: 8px;
  align-items: center;
}
.ds-add-form :deep(.el-textarea__inner) {
  font-family: 'Consolas', monospace;
  font-size: 12px;
}
/* 单元格详情弹窗 */
.cell-detail-body {
  max-height: 50vh;
  overflow: auto;
}
.cell-detail-content {
  margin: 0;
  font-family: 'Consolas', monospace;
  font-size: 13px;
  white-space: pre-wrap;
  word-break: break-all;
  line-height: 1.6;
  color: #303133;
  user-select: text;
}
/* 悬浮 tooltip 样式 */
/* 查询锁表弹窗 */
.lock-dialog-body {
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.lock-toolbar {
  display: flex;
  gap: 8px;
  align-items: center;
}
.lock-sql-details {
  border: 1px solid #e4e7ed;
  border-radius: 4px;
  padding: 8px 12px;
  background: #fafafa;
}
.lock-sql-details summary {
  cursor: pointer;
  font-size: 13px;
  font-weight: bold;
  color: #606266;
  user-select: none;
}
.lock-sql-section {
  margin-top: 8px;
}
.lock-sql-label {
  font-size: 12px;
  font-weight: bold;
  color: #303133;
  margin-bottom: 4px;
}
.lock-sql-code {
  margin: 0;
  font-family: 'Consolas', monospace;
  font-size: 12px;
  white-space: pre-wrap;
  word-break: break-all;
  line-height: 1.5;
  color: #303133;
  background: #f5f7fa;
  padding: 8px;
  border-radius: 4px;
  max-height: 200px;
  overflow: auto;
}
.lock-result-area {
  min-height: 100px;
}
.lock-result-info {
  font-size: 12px;
  color: #909399;
  margin-bottom: 6px;
}
.lock-table-wrap {
  max-height: 400px;
  overflow: auto;
}
.lock-error {
  color: #f56c6c;
  font-size: 13px;
  padding: 16px;
}
.lock-empty {
  color: #909399;
  font-size: 13px;
  text-align: center;
  padding: 32px;
}
/* 快捷输入 */
.snippet-hint {
  font-size: 12px;
  color: #909399;
}
</style>

<style>
/* SQL 结果表的悬浮提示改用等宽字体（浅色风格与宽高限制已由 main.css 全局统一，此处只补字体，
   且限定在结果表内，避免影响其它页面表格的提示样式）。浮层挂在 .el-table 根节点下，不能 scoped */
.result-table-wrap .el-table > .el-popper {
  font-family: 'Consolas', monospace;
  font-size: 12px;
}
</style>

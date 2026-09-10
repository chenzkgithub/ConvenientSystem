<template>
  <div class="api-spec-view">
    <div class="spec-header">
      <h2 class="spec-title">API 文档生成器</h2>
      <p class="spec-sub">选择 C# 项目的 Controller 源文件，生成 OpenAPI / Postman 等格式的 API 数据文件，导入 Apifox / Postman 等工具即用</p>
    </div>

    <!-- 格式选择 -->
    <div class="format-grid">
      <div
        v-for="f in formats"
        :key="f.format"
        class="format-card"
        :class="{ active: selectedFormat === f.format }"
        @click="selectedFormat = f.format"
      >
        <div class="format-icon">{{ f.icon || '📄' }}</div>
        <div class="format-info">
          <div class="format-name">{{ f.displayName }}</div>
          <div class="format-desc">{{ f.description }}</div>
        </div>
        <el-tag v-if="selectedFormat === f.format" size="small" type="success" class="format-check">已选</el-tag>
      </div>
    </div>

    <!-- 数据源 -->
    <div class="source-bar">
      <span class="source-label">解决方案</span>
      <el-input v-model="solutionPath" placeholder="解决方案文件（.sln/.slnx）或项目根目录" clearable @keyup.enter="scanSolution" />
      <el-button :loading="picking" @click="pickSolution">浏览...</el-button>
      <el-button type="primary" :loading="scanning" :disabled="scanning" @click="scanSolution">扫描</el-button>
      <span v-if="scanned" class="source-stats">{{ statsText }}</span>
    </div>

    <!-- 接口列表 -->
    <div v-if="solutionEndpoints.length > 0" class="endpoint-section">
      <div class="section-header">
        <h3 class="section-title">接口列表</h3>
        <div class="section-toolbar">
          <el-input v-model="searchKeyword" placeholder="搜索 Controller、方法、路径或说明..." clearable class="search-input" />
          <el-button @click="selectAll">全选全部</el-button>
          <el-button @click="selectInvert">反选全部</el-button>
          <el-button type="primary" plain @click="selectFiltered">仅选当前筛选</el-button>
        </div>
      </div>

      <div class="endpoint-list">
        <div v-for="g in filteredGroupedEndpoints" :key="g.name" class="endpoint-group">
          <div class="group-header" @click="toggleCollapse(g.name)">
            <el-checkbox
              :model-value="isGroupSelected(g.items)"
              :indeterminate="isGroupIndeterminate(g.items)"
              @click.stop
              @update:model-value="() => toggleGroup(g.items)"
            />
            <span class="group-arrow">{{ collapsedGroups.has(g.name) ? '▶' : '▼' }}</span>
            <span class="group-name">{{ g.name }}</span>
            <span class="group-count">{{ g.items.length }} 个接口</span>
          </div>
          <div v-show="!collapsedGroups.has(g.name)" class="group-body">
            <div
              v-for="ep in g.items"
              :key="keyOf(ep)"
              class="endpoint-row"
              :title="ep.summary || ep.actionName"
            >
              <el-checkbox
                :model-value="isSelected(ep)"
                @update:model-value="() => toggleEndpoint(ep)"
              />
              <span class="ep-method" :class="'m-' + ep.method.toLowerCase()">{{ ep.method }}</span>
              <span class="ep-path">{{ fullUrl(ep.path) }}</span>
              <span class="ep-summary">{{ ep.summary || ep.actionName }}</span>
              <el-tag v-if="ep.permission" size="small" type="warning" class="ep-perm">{{ ep.permission }}</el-tag>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- 生成设置 -->
    <div v-if="solutionEndpoints.length > 0" class="generate-bar">
      <span class="generate-label">文档标题</span>
      <el-input v-model="docTitle" placeholder="默认 ConvenientSystem API" style="width: 220px" />
      <span class="generate-label">服务器地址</span>
      <el-input v-model="baseUrl" placeholder="默认 http://localhost" style="width: 240px" />
      <el-tag size="small" type="primary">{{ currentFormatName }}</el-tag>
      <el-button
        type="primary"
        :disabled="!canGenerate"
        :loading="generating"
        @click="generate"
      >解析并生成</el-button>
      <el-button
        v-if="$has('api-spec:export') && preview"
        type="success"
        @click="download"
      >下载 {{ preview.fileName }}</el-button>
      <el-button v-if="preview" @click="copyContent">复制内容</el-button>
      <el-button
        type="success"
        :disabled="selectedEndpoints.length === 0 || scanning"
        :loading="apifoxImporting"
        @click="openApifoxImport"
      >导入 Apifox</el-button>
    </div>

    <!-- 预览 -->
    <div v-if="parsedDoc" class="preview-section">
      <el-alert
        v-for="w in warnings"
        :key="w"
        :title="w"
        type="warning"
        :closable="false"
        class="spec-alert"
      />

      <div class="preview-layout">
        <div class="preview-left">
          <div class="panel-head">已选接口摘要（{{ selectedEndpoints.length }} 个）</div>
          <div class="summary-list">
            <div v-for="g in selectedGroupedEndpoints" :key="g.name" class="summary-group">
              <div class="summary-group-name">{{ g.name }}（{{ g.items.length }}）</div>
              <div v-for="ep in g.items" :key="keyOf(ep)" class="summary-item" :title="ep.summary">
                <span class="ep-method" :class="'m-' + ep.method.toLowerCase()">{{ ep.method }}</span>
                <span class="ep-path">{{ ep.path }}</span>
              </div>
            </div>
            <div v-if="Object.keys(parsedDoc.types).length > 0" class="summary-group">
              <div class="summary-group-name">DTO 类型（{{ Object.keys(parsedDoc.types).length }} 个）</div>
              <div class="summary-item" v-for="t in Object.values(parsedDoc.types)" :key="t.name" :title="typeTitle(t)">
                <span class="ep-method" :class="t.isEnum ? 'm-enum' : 'm-dto'">{{ t.isEnum ? 'enum' : 'dto' }}</span>
                <span class="ep-path">{{ t.name }}</span>
              </div>
            </div>
          </div>
        </div>
        <div class="preview-right">
          <div class="panel-head">{{ preview ? preview.fileName : '生成内容' }}</div>
          <pre class="content-preview">{{ preview?.content || '点击「解析并生成」查看内容' }}</pre>
        </div>
      </div>
    </div>

    <div v-else-if="solutionEndpoints.length === 0 && scanned" class="empty-tip">
      未扫描到任何接口（需 public 方法带 [HttpGet] 等 HTTP 特性）
    </div>

    <div v-else class="empty-tip big">
      选择格式并填写数据源后，点击「扫描」解析接口
    </div>

    <el-dialog
      v-model="apifoxImportVisible"
      title="导入到 Apifox"
      width="560px"
      :close-on-click-modal="false"
      :append-to-body="true"
    >
      <el-alert
        title="Access Token 请在「个人配置」中维护；以下项目和导入选项仅用于本次导入，不会保存。"
        type="info"
        :closable="false"
        class="apifox-config-alert"
      />
      <el-form label-width="108px">
        <el-form-item label="项目 ID" required>
          <el-input v-model="apifoxImportForm.projectId" placeholder="如：4478210" />
        </el-form-item>
        <el-form-item label="接口目录 ID">
          <el-input v-model="apifoxImportForm.targetEndpointFolderId" placeholder="可选；不填导入项目根目录" />
        </el-form-item>
        <el-form-item label="分支 ID">
          <el-input v-model="apifoxImportForm.targetBranchId" placeholder="可选；不填导入主分支" />
        </el-form-item>
        <el-form-item label="重复接口处理">
          <el-select v-model="apifoxImportForm.endpointOverwriteBehavior" style="width: 100%">
            <el-option label="智能合并（推荐，保留已有手工配置）" value="AUTO_MERGE" />
            <el-option label="覆盖已有接口与数据模型" value="OVERWRITE_EXISTING" />
            <el-option label="保留已有内容，不更新" value="KEEP_EXISTING" />
            <el-option label="创建新接口，不匹配已有接口" value="CREATE_NEW" />
          </el-select>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button :disabled="apifoxImporting" @click="apifoxImportVisible = false">取消</el-button>
        <el-button type="primary" :loading="apifoxImporting" @click="importToApifox">开始导入</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { ElMessage } from 'element-plus'
import {
  getApiSpecFormats, scanApiSpecSolution, parseApiSpec, previewApiSpec, pickApiSpecSolution, importOpenApiToApifox,
  type ApiSpecFormatDto, type ApiSpecSolutionEndpointDto, type ApiSpecDocumentDto, type ApiSpecExportDto, type ApiSpecTypeDto,
  type ApifoxImportRequest,
} from '@/common/api/apiSpec'

const SOLUTION_KEY = 'api-spec:solution'
const BASE_URL_KEY = 'api-spec:baseUrl'

const formats = ref<ApiSpecFormatDto[]>([])
const selectedFormat = ref('')
const solutionPath = ref(localStorage.getItem(SOLUTION_KEY) || '')
const picking = ref(false)
const scanning = ref(false)
const scanned = ref(false)
const solutionEndpoints = ref<ApiSpecSolutionEndpointDto[]>([])
const selectedKeys = ref<Set<string>>(new Set())
const collapsedGroups = ref<Set<string>>(new Set())
const searchKeyword = ref('')
const docTitle = ref('')
const baseUrl = ref(localStorage.getItem(BASE_URL_KEY) || 'http://localhost')
const generating = ref(false)
const parsedDoc = ref<ApiSpecDocumentDto | null>(null)
const preview = ref<ApiSpecExportDto | null>(null)
const warnings = ref<string[]>([])

const apifoxImportVisible = ref(false)
const apifoxImporting = ref(false)
const apifoxImportForm = ref({
  projectId: '',
  targetEndpointFolderId: '',
  targetBranchId: '',
  endpointOverwriteBehavior: 'AUTO_MERGE',
})

const selectedEndpoints = computed(() => solutionEndpoints.value.filter(e => selectedKeys.value.has(keyOf(e))))
const canGenerate = computed(() => selectedFormat.value && selectedEndpoints.value.length > 0 && !generating.value && !scanning.value)
const currentFormatName = computed(() => formats.value.find(f => f.format === selectedFormat.value)?.displayName || selectedFormat.value)

const statsText = computed(() => {
  const controllers = new Set(solutionEndpoints.value.map(e => e.group)).size
  const total = solutionEndpoints.value.length
  const selected = selectedEndpoints.value.length
  return `共 ${controllers} 个 Controller · ${total} 个接口 · 已选 ${selected} 个`
})

const filteredEndpoints = computed(() => {
  const kw = searchKeyword.value.trim().toLowerCase()
  if (!kw) return solutionEndpoints.value
  return solutionEndpoints.value.filter(e =>
    e.group.toLowerCase().includes(kw) ||
    e.method.toLowerCase().includes(kw) ||
    e.path.toLowerCase().includes(kw) ||
    e.actionName.toLowerCase().includes(kw) ||
    (e.summary || '').toLowerCase().includes(kw)
  )
})

const filteredGroupedEndpoints = computed(() => groupBy(filteredEndpoints.value, e => e.group))
const selectedGroupedEndpoints = computed(() => groupBy(selectedEndpoints.value, e => e.group))

function groupBy(list: ApiSpecSolutionEndpointDto[], keyFn: (item: ApiSpecSolutionEndpointDto) => string) {
  const map = new Map<string, ApiSpecSolutionEndpointDto[]>()
  for (const item of list) {
    const key = keyFn(item)
    if (!map.has(key)) map.set(key, [])
    map.get(key)!.push(item)
  }
  return [...map.entries()].map(([name, items]) => ({ name, items }))
}

function keyOf(ep: ApiSpecSolutionEndpointDto): string {
  return ep.selectionKey
}

function isSelected(ep: ApiSpecSolutionEndpointDto): boolean {
  return selectedKeys.value.has(keyOf(ep))
}

function isGroupSelected(items: ApiSpecSolutionEndpointDto[]): boolean {
  return items.length > 0 && items.every(isSelected)
}

function isGroupIndeterminate(items: ApiSpecSolutionEndpointDto[]): boolean {
  const selectedCount = items.filter(isSelected).length
  return selectedCount > 0 && selectedCount < items.length
}

function toggleEndpoint(ep: ApiSpecSolutionEndpointDto) {
  const next = new Set(selectedKeys.value)
  const k = keyOf(ep)
  if (next.has(k)) next.delete(k)
  else next.add(k)
  selectedKeys.value = next
}

function toggleGroup(items: ApiSpecSolutionEndpointDto[]) {
  const allSelected = isGroupSelected(items)
  const next = new Set(selectedKeys.value)
  for (const ep of items) {
    const k = keyOf(ep)
    if (allSelected) next.delete(k)
    else next.add(k)
  }
  selectedKeys.value = next
}

function toggleCollapse(name: string) {
  const next = new Set(collapsedGroups.value)
  if (next.has(name)) next.delete(name)
  else next.add(name)
  collapsedGroups.value = next
}

function selectAll() {
  selectedKeys.value = new Set(solutionEndpoints.value.map(keyOf))
}

function selectInvert() {
  const selected = selectedKeys.value
  selectedKeys.value = new Set(solutionEndpoints.value.filter(ep => !selected.has(keyOf(ep))).map(keyOf))
}

function selectFiltered() {
  selectedKeys.value = new Set(filteredEndpoints.value.map(keyOf))
}

function openApifoxImport() {
  if (selectedEndpoints.value.length === 0) {
    ElMessage.warning('请至少勾选一个接口')
    return
  }
  apifoxImportForm.value = {
    projectId: '',
    targetEndpointFolderId: '',
    targetBranchId: '',
    endpointOverwriteBehavior: 'AUTO_MERGE',
  }
  apifoxImportVisible.value = true
}

function parseOptionalApifoxId(value: string, label: string): number | undefined | null {
  const text = value.trim()
  if (!text) return undefined
  if (!/^\d+$/.test(text) || !Number.isSafeInteger(Number(text)) || Number(text) <= 0) {
    ElMessage.warning(`${label}必须是正整数`)
    return null
  }
  return Number(text)
}

function buildApifoxImportRequest(content: string): ApifoxImportRequest | null {
  const projectId = apifoxImportForm.value.projectId.trim()
  if (!projectId) {
    ElMessage.warning('请填写 Apifox 项目 ID')
    return null
  }
  const endpointFolderId = parseOptionalApifoxId(apifoxImportForm.value.targetEndpointFolderId, '接口目录 ID')
  const branchId = parseOptionalApifoxId(apifoxImportForm.value.targetBranchId, '分支 ID')
  if (endpointFolderId === null || branchId === null) return null
  return {
    content,
    projectId,
    targetEndpointFolderId: endpointFolderId ?? null,
    targetBranchId: branchId ?? null,
    endpointOverwriteBehavior: apifoxImportForm.value.endpointOverwriteBehavior,
  }
}

async function importToApifox() {
  if (selectedEndpoints.value.length === 0) return ElMessage.warning('请至少勾选一个接口')
  const importRequest = buildApifoxImportRequest('')
  if (!importRequest) return

  apifoxImporting.value = true
  try {
    const openApi = await previewApiSpec(
      solutionRootDir(), selectedControllerFiles(), 'openapi3-json',
      docTitle.value.trim() || undefined, baseUrl.value.trim() || undefined,
      solutionPath.value.trim() || undefined, selectedSelectionKeys(),
    )
    importRequest.content = openApi.content
    const result = await importOpenApiToApifox(importRequest)
    const endpointChanges = result.endpointCreated + result.endpointUpdated
    const schemaChanges = result.schemaCreated + result.schemaUpdated
    apifoxImportVisible.value = false
    if (result.errors.length > 0 || result.endpointFailed > 0 || result.schemaFailed > 0) {
      ElMessage.warning(`Apifox 已完成导入：接口新增/更新 ${endpointChanges}，模型新增/更新 ${schemaChanges}；${result.errors[0] || '部分资源导入失败'}`)
    } else {
      ElMessage.success(`Apifox 导入成功：接口新增/更新 ${endpointChanges}，模型新增/更新 ${schemaChanges}`)
    }
  } catch {
    /* request.ts 已弹错误提示 */
  } finally {
    apifoxImporting.value = false
  }
}

onMounted(async () => {
  try {
    formats.value = await getApiSpecFormats()
    if (formats.value.length > 0) selectedFormat.value = formats.value[0].format
  } catch {
    /* 错误已由 request.ts 弹出提示 */
  }
})

async function pickSolution() {
  picking.value = true
  try {
    const res = await pickApiSpecSolution()
    if (res?.path) {
      solutionPath.value = res.path
      ElMessage.success('已选择解决方案，点击「扫描」解析接口')
    }
  } catch {
    ElMessage.warning('当前环境不支持选择文件，请手动输入路径')
  } finally {
    picking.value = false
  }
}

function solutionRootDir(): string {
  const p = solutionPath.value.trim().replace(/^"|"$/g, '')
  if (!/\.slnx?$/i.test(p)) return p
  const idx = Math.max(p.lastIndexOf('\\'), p.lastIndexOf('/'))
  return idx > 0 ? p.slice(0, idx) : p
}

function selectedControllerFiles(): string[] {
  return [...new Set(selectedEndpoints.value.map(e => e.file))]
}

function selectedSelectionKeys(): string[] {
  return selectedEndpoints.value.map(keyOf)
}

function fullUrl(path: string): string {
  return baseUrl.value.trim().replace(/\/+$/, '') + path
}

function clearScanState() {
  solutionEndpoints.value = []
  selectedKeys.value = new Set()
  collapsedGroups.value = new Set()
  parsedDoc.value = null
  preview.value = null
  warnings.value = []
  searchKeyword.value = ''
}

async function scanSolution() {
  const scanPath = solutionPath.value.trim()
  if (!scanPath) return ElMessage.warning('请填写解决方案文件路径或项目根目录')
  scanning.value = true
  scanned.value = false
  clearScanState()
  try {
    const endpoints = await scanApiSpecSolution(scanPath)
    if (scanPath !== solutionPath.value.trim()) return
    solutionEndpoints.value = endpoints
    scanned.value = true
    localStorage.setItem(SOLUTION_KEY, scanPath)
    selectedKeys.value = new Set(solutionEndpoints.value.map(keyOf))
    // 默认折叠所有分组
    collapsedGroups.value = new Set([...new Set(solutionEndpoints.value.map(e => e.group))])
  } finally {
    scanning.value = false
  }
}

async function generate() {
  if (selectedEndpoints.value.length === 0) return ElMessage.warning('请至少勾选一个接口')
  generating.value = true
  parsedDoc.value = null
  preview.value = null
  warnings.value = []
  try {
    parsedDoc.value = await parseApiSpec(
      solutionRootDir(), selectedControllerFiles(), docTitle.value.trim() || undefined,
      baseUrl.value.trim() || undefined, solutionPath.value.trim() || undefined,
    )
    await refreshPreview()
  } catch {
    /* 错误已由 request.ts 弹出提示 */
  } finally {
    generating.value = false
  }
}

let previewToken = 0

async function refreshPreview() {
  const token = ++previewToken
  try {
    const res = await previewApiSpec(
      solutionRootDir(), selectedControllerFiles(), selectedFormat.value,
      docTitle.value.trim() || undefined, baseUrl.value.trim() || undefined,
      solutionPath.value.trim() || undefined, selectedSelectionKeys(),
    )
    if (token !== previewToken) return
    preview.value = res
    warnings.value = [...new Set([...(parsedDoc.value?.warnings || []), ...(res?.warnings || [])])]
  } catch (e) {
    if (token === previewToken) throw e
  }
}

watch(selectedFormat, async () => {
  if (!preview.value || selectedEndpoints.value.length === 0) return
  try {
    await refreshPreview()
  } catch {
    /* 错误已由 request.ts 弹出提示 */
  }
})

watch(solutionPath, () => {
  if (solutionEndpoints.value.length === 0 && !scanned.value) return
  scanned.value = false
  clearScanState()
})

watch(baseUrl, v => localStorage.setItem(BASE_URL_KEY, v))

async function download() {
  if (!preview.value) return
  const blob = new Blob([preview.value.content], { type: preview.value.contentType })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = preview.value.fileName
  a.click()
  URL.revokeObjectURL(url)
}

async function copyContent() {
  if (!preview.value) return
  try {
    await navigator.clipboard.writeText(preview.value.content)
    ElMessage.success('已复制到剪贴板')
  } catch {
    ElMessage.error('复制失败：浏览器未授权剪贴板')
  }
}

function typeTitle(t: ApiSpecTypeDto) {
  return t.comment || (t.isEnum ? `枚举：${t.enumValues.join(' | ')}` : t.fields.map(f => `${f.name}: ${f.typeText}`).join('\n'))
}
</script>

<style scoped>
.api-spec-view {
  height: 100%;
  overflow-y: auto;
  box-sizing: border-box;
  padding: 20px;
  max-width: 1600px;
  margin: 0 auto;
}

.spec-header {
  margin-bottom: 20px;
}

.spec-title {
  margin: 0 0 6px;
  font-size: 22px;
  font-weight: 600;
}

.spec-sub {
  margin: 0;
  color: var(--text-sub, #909399);
  font-size: 13px;
}

/* 格式选择 */
.format-grid {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 12px;
  margin-bottom: 16px;
}

.format-card {
  position: relative;
  border: 1px solid var(--el-border-color);
  border-radius: 6px;
  padding: 12px;
  cursor: pointer;
  transition: all 0.15s;
  display: flex;
  align-items: flex-start;
  gap: 10px;
}

.format-card:hover {
  border-color: var(--el-color-primary-light-3);
  box-shadow: 0 2px 4px rgba(0, 0, 0, 0.05);
}

.format-card.active {
  border-color: var(--el-color-primary);
  box-shadow: 0 0 0 1px var(--el-color-primary) inset;
}

.format-icon {
  font-size: 20px;
  flex: none;
}

.format-info {
  flex: 1;
  min-width: 0;
}

.format-name {
  font-weight: 600;
  font-size: 13px;
  margin-bottom: 2px;
}

.format-desc {
  font-size: 12px;
  color: var(--text-sub, #909399);
  line-height: 1.4;
}

.format-check {
  position: absolute;
  top: 8px;
  right: 8px;
}

/* 数据源 */
.source-bar {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-bottom: 16px;
  padding: 12px;
  background: var(--el-fill-color-light);
  border-radius: 6px;
}

.source-label {
  font-size: 13px;
  font-weight: 600;
  white-space: nowrap;
}

.source-bar .el-input {
  flex: 1;
}

.source-stats {
  font-size: 13px;
  color: var(--text-sub, #909399);
  white-space: nowrap;
}

/* 接口列表 */
.endpoint-section {
  margin-bottom: 16px;
}

.section-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 12px;
}

.section-title {
  margin: 0;
  font-size: 16px;
  font-weight: 600;
}

.section-toolbar {
  display: flex;
  align-items: center;
  gap: 10px;
}

.search-input {
  width: 320px;
}

.endpoint-list {
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 6px;
  max-height: 480px;
  overflow: auto;
}

.endpoint-group + .endpoint-group {
  border-top: 1px solid var(--el-border-color-lighter);
}

.group-header {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 10px 12px;
  background: var(--el-fill-color-light);
  cursor: pointer;
  user-select: none;
  font-size: 13px;
}

.group-header:hover {
  background: var(--el-fill-color);
}

.group-arrow {
  font-size: 10px;
  color: var(--text-sub, #909399);
  width: 12px;
}

.group-name {
  font-weight: 600;
  color: var(--el-color-primary);
}

.group-count {
  margin-left: auto;
  font-size: 12px;
  color: var(--text-sub, #909399);
}

.group-body {
  padding: 4px 0;
}

.endpoint-row {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 8px 12px 8px 44px;
  font-size: 13px;
  transition: background 0.1s;
}

.endpoint-row:hover {
  background: var(--el-fill-color-light);
}

.ep-method {
  flex: none;
  width: 48px;
  text-align: center;
  border-radius: 3px;
  font-size: 11px;
  font-weight: 600;
  color: #fff;
  padding: 2px 0;
}

.m-get { background: #409eff; }
.m-post { background: #67c23a; }
.m-put { background: #e6a23c; }
.m-delete { background: #f56c6c; }
.m-patch { background: #909399; }
.m-head, .m-options { background: #909399; }
.m-dto { background: #7c3aed; }
.m-enum { background: #0ea5e9; }

.ep-path {
  flex: none;
  width: 340px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-family: Consolas, Monaco, monospace;
  font-size: 12px;
}

.ep-summary {
  flex: 1;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  color: var(--text-sub, #606266);
}

.ep-perm {
  flex: none;
}

/* 生成设置 */
.generate-bar {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 12px;
  background: var(--el-fill-color-light);
  border-radius: 6px;
  margin-bottom: 16px;
  flex-wrap: wrap;
}

.generate-label {
  font-size: 13px;
  font-weight: 600;
  white-space: nowrap;
}

/* 预览 */
.preview-section {
  margin-top: 16px;
}

.spec-alert {
  margin-bottom: 12px;
}

.preview-layout {
  display: grid;
  grid-template-columns: 320px 1fr;
  gap: 16px;
}

.panel-head {
  font-size: 13px;
  font-weight: 600;
  margin-bottom: 8px;
  color: var(--text-main, #303133);
}

.summary-list {
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 6px;
  max-height: 520px;
  overflow: auto;
  padding: 8px;
}

.summary-group {
  margin-bottom: 10px;
}

.summary-group-name {
  font-size: 12px;
  font-weight: 600;
  color: var(--el-color-primary);
  padding: 4px 0;
}

.summary-item {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 3px 0;
  font-size: 12px;
}

.summary-item .ep-path {
  width: auto;
  flex: 1;
}

.content-preview {
  margin: 0;
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 6px;
  background: var(--el-fill-color-blank);
  padding: 12px;
  max-height: 520px;
  overflow: auto;
  font-family: Consolas, Monaco, monospace;
  font-size: 12px;
  line-height: 1.55;
  white-space: pre;
  word-break: break-all;
}

html.dark .content-preview {
  background: #0f172a;
  color: #cbd5e1;
}

html.dark .endpoint-row:hover {
  background: #1e293b;
}

html.dark .group-header:hover {
  background: #1e293b;
}

.apifox-config-alert {
  margin-bottom: 18px;
}

.clear-token-button {
  margin-left: 10px;
}

.empty-tip {
  color: var(--text-sub, #909399);
  font-size: 13px;
  padding: 40px 0;
  text-align: center;
}

.empty-tip.big {
  padding: 80px 0;
  font-size: 14px;
}
</style>

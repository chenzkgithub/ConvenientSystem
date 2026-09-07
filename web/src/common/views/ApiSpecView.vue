<template>
  <div class="api-spec-view">
    <div class="spec-header">
      <div>
        <h2 class="spec-title">API 文档生成器</h2>
        <p class="spec-sub">选择 C# 项目的 Controller 源文件，生成 OpenAPI / Postman 等格式的 API 数据文件，导入 Apifox / Postman 等工具即用</p>
      </div>
    </div>

    <!-- ① 选格式 -->
    <el-card shadow="never" class="spec-section">
      <template #header><span class="section-step">①</span>选择目标格式</template>
      <div class="format-grid">
        <div
          v-for="f in formats"
          :key="f.format"
          class="format-card"
          :class="{ active: selectedFormat === f.format }"
          @click="selectedFormat = f.format"
        >
          <div class="format-name">{{ f.displayName }}</div>
          <div class="format-desc">{{ f.description }}</div>
          <el-tag v-if="selectedFormat === f.format" size="small" type="success" class="format-check">已选</el-tag>
        </div>
      </div>
    </el-card>

    <!-- ② 选源文件 -->
    <el-card shadow="never" class="spec-section">
      <template #header><span class="section-step">②</span>选择 Controller 源文件</template>
      <div class="root-row">
        <el-input v-model="rootDir" placeholder="C# 项目根目录（如 E:\A-Chenzk\Code\MyProject\ConvenientSystem）" clearable @keyup.enter="scan">
          <template #prepend>项目根目录</template>
        </el-input>
        <el-button type="primary" :loading="scanning" @click="scan">扫描</el-button>
      </div>
      <el-alert v-if="scanWarning" :title="scanWarning" type="warning" :closable="false" class="spec-alert" />

      <el-table
        v-if="controllerFiles.length > 0"
        :data="controllerFiles"
        size="small"
        max-height="260"
        @selection-change="onSelectionChange"
      >
        <el-table-column type="selection" width="42" />
        <el-table-column prop="controllerName" label="Controller" width="180" />
        <el-table-column prop="path" label="相对路径" min-width="320" show-overflow-tooltip />
        <el-table-column prop="endpointCount" label="接口数" width="80" align="center">
          <template #default="{ row }"><el-tag size="small" type="info">{{ row.endpointCount }}</el-tag></template>
        </el-table-column>
      </el-table>
      <div v-else-if="!scanning && scanned" class="empty-tip">该目录下未找到 *Controller.cs（已排除 bin/obj/node_modules 等）</div>
    </el-card>

    <!-- ③ 预览与导出 -->
    <el-card shadow="never" class="spec-section">
      <template #header><span class="section-step">③</span>预览与导出</template>
      <div class="option-row">
        <div class="option-item">
          <span class="option-label">文档标题</span>
          <el-input v-model="docTitle" placeholder="默认 ConvenientSystem API" style="width: 240px" />
        </div>
        <div class="option-item">
          <span class="option-label">服务器地址</span>
          <el-input v-model="baseUrl" placeholder="默认 http://localhost" style="width: 260px" />
        </div>
        <div class="option-actions">
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
        </div>
      </div>

      <el-alert
        v-for="w in warnings"
        :key="w"
        :title="w"
        type="warning"
        :closable="false"
        class="spec-alert"
      />

      <el-row v-if="parsedDoc" :gutter="14" class="preview-row">
        <el-col :span="9">
          <div class="panel-head">接口清单（{{ parsedDoc.endpoints.length }} 个）</div>
          <div class="endpoint-list">
            <template v-for="(group, gi) in groupedEndpoints" :key="gi">
              <div class="endpoint-group">{{ group.name }}（{{ group.items.length }}）</div>
              <div v-for="(ep, i) in group.items" :key="gi + '-' + i" class="endpoint-item" :title="ep.summary">
                <span class="ep-method" :class="'m-' + ep.method.toLowerCase()">{{ ep.method }}</span>
                <span class="ep-path">{{ ep.path }}</span>
                <el-tag v-if="ep.permission" size="small" type="warning" class="ep-perm">{{ ep.permission }}</el-tag>
              </div>
            </template>
            <div v-if="Object.keys(parsedDoc.types).length > 0" class="endpoint-group">
              DTO 类型（{{ Object.keys(parsedDoc.types).length }} 个）
            </div>
            <div class="endpoint-item type-item" v-for="t in Object.values(parsedDoc.types)" :key="t.name" :title="typeTitle(t)">
              <span class="ep-method" :class="t.isEnum ? 'm-enum' : 'm-dto'">{{ t.isEnum ? 'enum' : 'dto' }}</span>
              <span class="ep-path">{{ t.name }}</span>
              <span class="ep-fields">{{ t.isEnum ? t.enumValues.length + ' 个值' : t.fields.length + ' 个字段' }}</span>
            </div>
          </div>
        </el-col>
        <el-col :span="15">
          <div class="panel-head">{{ preview ? preview.fileName : '生成内容' }}</div>
          <pre class="content-preview">{{ preview?.content || '点击「解析并生成」查看内容' }}</pre>
        </el-col>
      </el-row>
      <div v-else class="empty-tip big">
        选择格式与 Controller 文件后，点击「解析并生成」
      </div>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { ElMessage } from 'element-plus'
import {
  getApiSpecFormats, scanApiSpecControllers, parseApiSpec, previewApiSpec,
  type ApiSpecFormatDto, type ApiSpecFileDto, type ApiSpecDocumentDto, type ApiSpecExportDto, type ApiSpecTypeDto,
} from '@/common/api/apiSpec'

const ROOT_DIR_KEY = 'api-spec:rootDir'

const formats = ref<ApiSpecFormatDto[]>([])
const selectedFormat = ref('')
const rootDir = ref(localStorage.getItem(ROOT_DIR_KEY) || '')
const scanning = ref(false)
const scanned = ref(false)
const scanWarning = ref('')
const controllerFiles = ref<ApiSpecFileDto[]>([])
const selectedFiles = ref<ApiSpecFileDto[]>([])
const docTitle = ref('')
const baseUrl = ref('')
const generating = ref(false)
// 命名避开全局 document（下载时需用 DOM API）
const parsedDoc = ref<ApiSpecDocumentDto | null>(null)
const preview = ref<ApiSpecExportDto | null>(null)
const warnings = ref<string[]>([])

const canGenerate = computed(() => selectedFormat.value && selectedFiles.value.length > 0 && !generating.value)

/** 接口按 Controller 分组展示 */
const groupedEndpoints = computed(() => {
  if (!parsedDoc.value) return []
  const map = new Map<string, typeof parsedDoc.value.endpoints>()
  for (const ep of parsedDoc.value.endpoints) {
    if (!map.has(ep.group)) map.set(ep.group, [])
    map.get(ep.group)!.push(ep)
  }
  return [...map.entries()].map(([name, items]) => ({ name, items }))
})

onMounted(async () => {
  try {
    formats.value = await getApiSpecFormats()
    if (formats.value.length > 0) selectedFormat.value = formats.value[0].format
  } catch {
    /* 错误已由 request.ts 弹出提示 */
  }
})

async function scan() {
  if (!rootDir.value.trim()) return ElMessage.warning('请填写项目根目录')
  scanning.value = true
  scanWarning.value = ''
  scanned.value = false
  parsedDoc.value = null
  preview.value = null
  try {
    controllerFiles.value = await scanApiSpecControllers(rootDir.value.trim())
    scanned.value = true
    localStorage.setItem(ROOT_DIR_KEY, rootDir.value.trim())
  } catch {
    controllerFiles.value = []
  } finally {
    scanning.value = false
  }
}

function onSelectionChange(rows: ApiSpecFileDto[]) {
  selectedFiles.value = rows
}

/** 解析接口清单 + 按当前格式生成预览内容 */
async function generate() {
  generating.value = true
  parsedDoc.value = null
  preview.value = null
  warnings.value = []
  const files = selectedFiles.value.map(f => f.path)
  try {
    parsedDoc.value = await parseApiSpec(rootDir.value.trim(), files, docTitle.value.trim() || undefined, baseUrl.value.trim() || undefined)
    await refreshPreview()
  } catch {
    /* 错误已由 request.ts 弹出提示 */
  } finally {
    generating.value = false
  }
}

/** 按当前选中格式生成预览（格式切换后可重复调用） */
async function refreshPreview() {
  const files = selectedFiles.value.map(f => f.path)
  preview.value = await previewApiSpec(
    rootDir.value.trim(), files, selectedFormat.value,
    docTitle.value.trim() || undefined, baseUrl.value.trim() || undefined,
  )
  warnings.value = [...(parsedDoc.value?.warnings || []), ...(preview.value?.warnings || [])]
}

/** 切换格式后自动刷新预览（已生成过时） */
watch(selectedFormat, async () => {
  if (!preview.value || selectedFiles.value.length === 0) return
  try {
    await refreshPreview()
  } catch {
    /* 错误已由 request.ts 弹出提示 */
  }
})

async function download() {
  if (!preview.value) return
  // 预览内容即文件内容，前端 Blob 直接触发下载（原生 a 标签无法携带 Authorization 头）
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
  padding: 16px;
  max-width: 1400px;
  margin: 0 auto;
}
.spec-header {
  margin-bottom: 14px;
}
.spec-title {
  margin: 0 0 4px;
  font-size: 20px;
}
.spec-sub {
  margin: 0;
  color: var(--text-sub, #909399);
  font-size: 13px;
}
.spec-section {
  margin-bottom: 14px;
}
.section-step {
  display: inline-block;
  width: 22px;
  height: 22px;
  line-height: 22px;
  text-align: center;
  border-radius: 50%;
  background: var(--el-color-primary);
  color: #fff;
  font-size: 13px;
  margin-right: 8px;
}

/* ① 格式卡片网格 */
.format-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(260px, 1fr));
  gap: 12px;
}
.format-card {
  position: relative;
  border: 1px solid var(--el-border-color);
  border-radius: 8px;
  padding: 12px 14px;
  cursor: pointer;
  transition: border-color 0.15s, box-shadow 0.15s;
}
.format-card:hover {
  border-color: var(--el-color-primary-light-3);
}
.format-card.active {
  border-color: var(--el-color-primary);
  box-shadow: 0 0 0 1px var(--el-color-primary) inset;
}
.format-name {
  font-weight: 600;
  margin-bottom: 4px;
}
.format-desc {
  font-size: 12px;
  color: var(--text-sub, #909399);
  line-height: 1.5;
}
.format-check {
  position: absolute;
  top: 10px;
  right: 10px;
}

/* ② 目录扫描 */
.root-row {
  display: flex;
  gap: 10px;
  margin-bottom: 10px;
}
.root-row .el-input {
  flex: 1;
}
.spec-alert {
  margin-bottom: 10px;
}
.empty-tip {
  color: var(--text-sub, #909399);
  font-size: 13px;
  padding: 18px 0;
  text-align: center;
}
.empty-tip.big {
  padding: 60px 0;
  font-size: 14px;
}

/* ③ 选项与预览 */
.option-row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 16px;
  margin-bottom: 12px;
}
.option-item {
  display: flex;
  align-items: center;
  gap: 8px;
}
.option-label {
  font-size: 13px;
  color: var(--text-main, #303133);
}
.option-actions {
  margin-left: auto;
  display: flex;
  gap: 8px;
}
.preview-row {
  margin-top: 4px;
}
.panel-head {
  font-size: 13px;
  font-weight: 600;
  margin-bottom: 8px;
  color: var(--text-main, #303133);
}
.endpoint-list {
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 6px;
  max-height: 520px;
  overflow: auto;
  padding: 6px;
}
.endpoint-group {
  font-size: 12px;
  font-weight: 600;
  color: var(--el-color-primary);
  padding: 8px 6px 4px;
}
.endpoint-item {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 4px 6px;
  border-radius: 4px;
  font-size: 12px;
}
.endpoint-item:hover {
  background: var(--el-fill-color-light);
}
.ep-method {
  flex: none;
  width: 46px;
  text-align: center;
  border-radius: 3px;
  font-size: 11px;
  font-weight: 600;
  color: #fff;
  padding: 1px 0;
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
  flex: 1;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-family: Consolas, Monaco, monospace;
}
.ep-perm {
  flex: none;
}
.ep-fields {
  flex: none;
  color: var(--text-sub, #909399);
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
html.dark .endpoint-item:hover {
  background: #1e293b;
}
</style>

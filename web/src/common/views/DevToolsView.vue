<script setup lang="ts">
import { onMounted, onBeforeUnmount, ref, shallowRef, computed, watch, nextTick } from 'vue'
import { ElMessage } from 'element-plus'
import { monaco } from '@/common/monacoSetup'
import { generateSnowflakeIds } from '@/common/api/devTools'

// ==================== 工具 Tab ====================
type ToolTab = 'json' | 'diff' | 'timestamp' | 'codec' | 'regex' | 'snowflake'
const activeToolTab = ref<ToolTab>('json')

// ==================== JSON 工具 ====================
const jsonEditorEl = ref<HTMLElement>()
const jsonEditor = shallowRef<monaco.editor.IStandaloneCodeEditor>()
const jsonMessage = ref('')
const jsonMessageType = ref<'success' | 'error'>('success')

function initJsonEditor() {
  if (!jsonEditorEl.value || jsonEditor.value) return
  jsonEditor.value = monaco.editor.create(jsonEditorEl.value, {
    value: '',
    language: 'json',
    theme: 'vs',
    minimap: { enabled: false },
    automaticLayout: true,
    fontSize: 14,
    tabSize: 2,
    wordWrap: 'on',
  })
}

function jsonFormat() {
  const ed = jsonEditor.value
  const raw = ed?.getValue() ?? ''
  if (!raw.trim()) return
  try {
    const obj = JSON.parse(raw)
    ed?.setValue(JSON.stringify(obj, null, 2))
    clearJsonMarkers()
    showJsonMsg('格式化成功', 'success')
  } catch (e) {
    locateJsonError(raw, (e as Error).message)
  }
}
function jsonCompress() {
  const ed = jsonEditor.value
  const raw = ed?.getValue() ?? ''
  if (!raw.trim()) return
  try {
    const obj = JSON.parse(raw)
    ed?.setValue(JSON.stringify(obj))
    clearJsonMarkers()
    showJsonMsg('压缩成功', 'success')
  } catch (e) {
    locateJsonError(raw, (e as Error).message)
  }
}
function jsonEscape() {
  const ed = jsonEditor.value
  const raw = ed?.getValue() ?? ''
  if (!raw.trim()) return
  ed?.setValue(JSON.stringify(raw))
  clearJsonMarkers()
  showJsonMsg('转义成功', 'success')
}
function jsonUnescape() {
  const ed = jsonEditor.value
  const raw = ed?.getValue() ?? ''
  if (!raw.trim()) return
  try {
    const parsed = JSON.parse(raw)
    if (typeof parsed === 'string') {
      ed?.setValue(parsed)
      clearJsonMarkers()
      showJsonMsg('反转义成功', 'success')
    } else {
      showJsonMsg('内容不是转义后的字符串', 'error')
    }
  } catch (e) {
    locateJsonError(raw, (e as Error).message)
  }
}

/** 统一的 JSON 错误定位：显示消息 + 跳转 + 标红 */
function locateJsonError(raw: string, msg: string) {
  showJsonMsg('JSON 解析失败：' + msg, 'error')
  const ed = jsonEditor.value
  if (!ed) return
  const pos = parseJsonErrorPosition(raw, msg)
  if (pos) {
    ed.setPosition({ lineNumber: pos.line, column: pos.column })
    ed.revealLineInCenter(pos.line)
    monaco.editor.setModelMarkers(ed.getModel()!, 'json-validate', [{
      startLineNumber: pos.line,
      startColumn: pos.column,
      endLineNumber: pos.line,
      endColumn: pos.column + 1,
      message: msg,
      severity: monaco.MarkerSeverity.Error,
    }])
  }
}

/** 清除错误标记 */
function clearJsonMarkers() {
  const ed = jsonEditor.value
  if (ed) monaco.editor.setModelMarkers(ed.getModel()!, 'json-validate', [])
}
function jsonValidate() {
  const ed = jsonEditor.value
  const raw = ed?.getValue() ?? ''
  if (!raw.trim()) { showJsonMsg('内容为空', 'error'); return }
  try {
    JSON.parse(raw)
    clearJsonMarkers()
    showJsonMsg('✓ JSON 格式正确', 'success')
  } catch (e) {
    locateJsonError(raw, (e as Error).message)
  }
}

/** 从 JSON.parse 错误信息中解析出行列位置 */
function parseJsonErrorPosition(raw: string, msg: string): { line: number; column: number } | null {
  const posMatch = msg.match(/position\s+(\d+)/)
  if (posMatch) {
    const offset = parseInt(posMatch[1])
    return offsetToLineCol(raw, offset)
  }
  const lcMatch = msg.match(/line\s+(\d+)\s+column\s+(\d+)/)
  if (lcMatch) {
    return { line: parseInt(lcMatch[1]), column: parseInt(lcMatch[2]) }
  }
  return null
}

/** 将字符偏移量转换为行列号 */
function offsetToLineCol(text: string, offset: number): { line: number; column: number } {
  let line = 1
  let col = 1
  for (let i = 0; i < offset && i < text.length; i++) {
    if (text[i] === '\n') { line++; col = 1 }
    else { col++ }
  }
  return { line, column: col }
}
function showJsonMsg(msg: string, type: 'success' | 'error') {
  jsonMessage.value = msg
  jsonMessageType.value = type
}

// ==================== 文本对比 ====================
const diffEditorEl = ref<HTMLElement>()
const diffEditor = shallowRef<monaco.editor.IDiffEditor>()
const diffOriginalText = ref('')
const diffModifiedText = ref('')

function initDiffEditor() {
  if (!diffEditorEl.value || diffEditor.value) return
  diffEditor.value = monaco.editor.createDiffEditor(diffEditorEl.value, {
    theme: 'vs',
    automaticLayout: true,
    renderSideBySide: true,
    minimap: { enabled: false },
    fontSize: 14,
    wordWrap: 'on',
    originalEditable: true,
  })
  updateDiffModels()
}
function updateDiffModels() {
  if (!diffEditor.value) return
  const original = monaco.editor.createModel(diffOriginalText.value, 'plaintext')
  const modified = monaco.editor.createModel(diffModifiedText.value, 'plaintext')
  diffEditor.value.setModel({ original, modified })
}
function diffCompare() {
  if (!diffEditor.value) return
  const model = diffEditor.value.getModel()
  if (model) {
    diffOriginalText.value = model.original.getValue()
    diffModifiedText.value = model.modified.getValue()
  }
  updateDiffModels()
}
function diffClear() {
  diffOriginalText.value = ''
  diffModifiedText.value = ''
  updateDiffModels()
}

// ==================== 时间戳转换 ====================
const tsInput = ref('')
const tsUnit = ref<'s' | 'ms'>('s')
const tsResult = ref('')
const tsResultType = ref<'success' | 'error'>('success')
const dtInput = ref('')
const dtResult = ref('')
const dtResultType = ref<'success' | 'error'>('success')

function tsToDate() {
  if (!tsInput.value.trim()) { tsResult.value = '请输入时间戳'; tsResultType.value = 'error'; return }
  const val = Number(tsInput.value)
  if (isNaN(val)) { tsResult.value = '无效数字'; tsResultType.value = 'error'; return }
  const ms = tsUnit.value === 's' ? val * 1000 : val
  const d = new Date(ms)
  if (isNaN(d.getTime())) { tsResult.value = '无效时间戳'; tsResultType.value = 'error'; return }
  tsResult.value = formatDate(d)
  tsResultType.value = 'success'
}
function dateToTs() {
  if (!dtInput.value.trim()) { dtResult.value = '请输入日期'; dtResultType.value = 'error'; return }
  const d = new Date(dtInput.value)
  if (isNaN(d.getTime())) { dtResult.value = '无效日期格式'; dtResultType.value = 'error'; return }
  dtResult.value = tsUnit.value === 's' ? String(Math.floor(d.getTime() / 1000)) : String(d.getTime())
  dtResultType.value = 'success'
}
function tsNow() {
  const now = Date.now()
  tsInput.value = tsUnit.value === 's' ? String(Math.floor(now / 1000)) : String(now)
  tsToDate()
}
function formatDate(d: Date): string {
  const pad = (n: number) => n.toString().padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`
}

// ==================== 编解码 ====================
type CodecType = 'base64' | 'url' | 'unicode' | 'html'
const codecType = ref<CodecType>('base64')
const codecInput = ref('')
const codecOutput = ref('')

function codecEncode() {
  const s = codecInput.value
  if (!s) { ElMessage.warning('请输入内容'); return }
  try {
    switch (codecType.value) {
      case 'base64': codecOutput.value = btoa(unescape(encodeURIComponent(s))); break
      case 'url': codecOutput.value = encodeURIComponent(s); break
      case 'unicode': codecOutput.value = Array.from(s).map(c => '\\u' + c.charCodeAt(0).toString(16).padStart(4, '0')).join(''); break
      case 'html': codecOutput.value = s.replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c] ?? c)); break
    }
    ElMessage.success('编码成功')
  } catch (e) {
    codecOutput.value = ''
    ElMessage.error('编码失败：' + (e as Error).message)
  }
}
function codecDecode() {
  const s = codecInput.value
  if (!s) { ElMessage.warning('请输入内容'); return }
  try {
    switch (codecType.value) {
      case 'base64': codecOutput.value = decodeURIComponent(escape(atob(s))); break
      case 'url': codecOutput.value = decodeURIComponent(s); break
      case 'unicode': codecOutput.value = s.replace(/\\u([0-9a-fA-F]{4})/g, (_, hex) => String.fromCharCode(parseInt(hex, 16))); break
      case 'html': {
        const el = document.createElement('div')
        el.innerHTML = s
        codecOutput.value = el.textContent ?? ''
        break
      }
    }
    ElMessage.success('解码成功')
  } catch (e) {
    codecOutput.value = ''
    ElMessage.error('解码失败：' + (e as Error).message)
  }
}
function codecSwap() {
  const tmp = codecInput.value
  codecInput.value = codecOutput.value
  codecOutput.value = tmp
  ElMessage.success('已互换')
}

// ==================== 正则测试 ====================
const regexPattern = ref('')
const regexFlags = ref('g')
const regexTestText = ref('')
const regexMatches = computed(() => {
  if (!regexPattern.value || !regexTestText.value) return []
  try {
    const re = new RegExp(regexPattern.value, regexFlags.value)
    const results: { match: string; index: number; groups: string[] }[] = []
    let m: RegExpExecArray | null
    const hasG = regexFlags.value.includes('g')
    while ((m = re.exec(regexTestText.value)) !== null) {
      results.push({ match: m[0], index: m.index, groups: m.slice(1) })
      if (!hasG) break
    }
    return results
  } catch { return [] }
})
const regexError = computed(() => {
  if (!regexPattern.value) return ''
  try { new RegExp(regexPattern.value, regexFlags.value); return '' } catch (e) { return (e as Error).message }
})

// ==================== 雪花ID ====================
const snowflakeCount = ref(1)
const snowflakeEpoch = ref('')
const snowflakeIds = ref<string[]>([])
const snowflakeLoading = ref(false)

async function generateSnowflake() {
  const n = Math.max(1, Math.min(1000, Number(snowflakeCount.value) || 1))
  snowflakeCount.value = n
  snowflakeLoading.value = true
  try {
    const res = await generateSnowflakeIds(n, snowflakeEpoch.value || undefined)
    snowflakeIds.value = res.ids
    ElMessage.success(`已生成 ${res.count} 个雪花ID`)
  } catch {
    /* request.ts 已弹错误提示 */
  } finally {
    snowflakeLoading.value = false
  }
}

function copySnowflakeId(id: string) {
  navigator.clipboard.writeText(id).then(() => {
    ElMessage.success(`已复制：${id}`)
  }).catch(() => {
    ElMessage.error('复制失败')
  })
}

function copyAllSnowflakeIds() {
  if (!snowflakeIds.value.length) return
  navigator.clipboard.writeText(snowflakeIds.value.join('\n')).then(() => {
    ElMessage.success(`已复制全部 ${snowflakeIds.value.length} 个ID`)
  }).catch(() => {
    ElMessage.error('复制失败')
  })
}

// ==================== 生命周期 ====================
onMounted(() => {
  nextTick(() => { initJsonEditor() })
})

watch(activeToolTab, (tab) => {
  nextTick(() => {
    if (tab === 'json') initJsonEditor()
    if (tab === 'diff') initDiffEditor()
  })
})

onBeforeUnmount(() => {
  jsonEditor.value?.dispose()
  diffEditor.value?.dispose()
})
</script>

<template>
  <div class="dev-tools">
    <el-tabs v-model="activeToolTab" class="tool-tabs">
      <!-- 文本对比 -->
      <el-tab-pane label="文本对比" name="diff" class="tool-pane">
        <div class="toolbar">
          <el-button @click="diffCompare">对比</el-button>
          <el-button @click="diffClear">清空</el-button>
          <el-text type="info" size="small">左侧为原始文本，右侧为修改后文本，直接编辑即可</el-text>
        </div>
        <div ref="diffEditorEl" class="editor-area"></div>
      </el-tab-pane>

      <!-- JSON 工具 -->
      <el-tab-pane label="JSON 工具" name="json" class="tool-pane">
        <div class="toolbar">
          <el-button @click="jsonFormat">格式化</el-button>
          <el-button @click="jsonCompress">压缩</el-button>
          <el-button @click="jsonEscape">转义</el-button>
          <el-button @click="jsonUnescape">反转义</el-button>
          <el-button @click="jsonValidate">校验</el-button>
          <el-text v-if="jsonMessage" :type="jsonMessageType === 'success' ? 'success' : 'danger'" size="small">{{ jsonMessage }}</el-text>
        </div>
        <div ref="jsonEditorEl" class="editor-area"></div>
      </el-tab-pane>

      <!-- 时间戳转换 -->
      <el-tab-pane label="时间戳" name="timestamp" class="tool-pane">
        <div class="ts-panel">
          <el-divider content-position="left">时间戳 → 日期</el-divider>
          <div class="ts-row">
            <el-input v-model="tsInput" placeholder="输入时间戳" style="width: 220px" @keyup.enter="tsToDate" />
            <el-select v-model="tsUnit" style="width: 110px">
              <el-option label="秒(s)" value="s" />
              <el-option label="毫秒(ms)" value="ms" />
            </el-select>
            <el-button type="primary" @click="tsToDate">转换</el-button>
            <el-button @click="tsNow">当前时间</el-button>
          </div>
          <el-alert v-if="tsResult" :title="tsResult" :type="tsResultType" show-icon :closable="false" />

          <el-divider content-position="left">日期 → 时间戳</el-divider>
          <div class="ts-row">
            <el-input v-model="dtInput" placeholder="如 2025-01-01 12:00:00" style="width: 320px" @keyup.enter="dateToTs" />
            <el-button type="primary" @click="dateToTs">转换</el-button>
          </div>
          <el-alert v-if="dtResult" :title="dtResult" :type="dtResultType" show-icon :closable="false" />
        </div>
      </el-tab-pane>

      <!-- 编解码 -->
      <el-tab-pane label="编解码" name="codec" class="tool-pane">
        <div class="codec-panel">
          <div class="toolbar">
            <el-select v-model="codecType" style="width: 130px">
              <el-option label="Base64" value="base64" />
              <el-option label="URL" value="url" />
              <el-option label="Unicode" value="unicode" />
              <el-option label="HTML 实体" value="html" />
            </el-select>
            <el-button @click="codecEncode">编码 ↓</el-button>
            <el-button @click="codecDecode">解码 ↓</el-button>
            <el-button @click="codecSwap">↕ 互换</el-button>
          </div>
          <el-input v-model="codecInput" type="textarea" :rows="6" placeholder="输入内容" class="codec-area" />
          <el-input v-model="codecOutput" type="textarea" :rows="6" placeholder="输出结果" readonly class="codec-area" />
        </div>
      </el-tab-pane>

      <!-- 正则测试 -->
      <el-tab-pane label="正则测试" name="regex" class="tool-pane">
        <div class="regex-panel">
          <div class="regex-row">
            <el-text class="regex-slash">/</el-text>
            <el-input v-model="regexPattern" placeholder="输入正则表达式" class="regex-input" />
            <el-text class="regex-slash">/</el-text>
            <el-input v-model="regexFlags" placeholder="flags" class="regex-flags" />
          </div>
          <el-text v-if="regexError" type="danger" size="small">{{ regexError }}</el-text>
          <el-input v-model="regexTestText" type="textarea" :rows="6" placeholder="输入测试文本" class="regex-text" />
          <div class="regex-results" v-if="regexMatches.length">
            <div class="regex-match" v-for="(m, i) in regexMatches" :key="i">
              <el-text type="info" size="small" class="match-index">#{{ i + 1 }}</el-text>
              <el-text type="warning" class="match-text">"{{ m.match }}"</el-text>
              <el-text type="info" size="small">位置 {{ m.index }}</el-text>
              <el-text v-if="m.groups.length" type="success" size="small">捕获组: {{ m.groups.join(', ') }}</el-text>
            </div>
          </div>
          <el-text v-else-if="regexPattern && regexTestText" type="info" size="small">无匹配</el-text>
        </div>
      </el-tab-pane>

      <!-- 雪花ID -->
      <el-tab-pane label="雪花ID" name="snowflake" class="tool-pane">
        <div class="snowflake-panel">
          <div class="toolbar">
            <el-text type="info" size="small">数量</el-text>
            <el-input-number v-model="snowflakeCount" :min="1" :max="1000" controls-position="right" style="width: 110px" />
            <el-text type="info" size="small">起始日期</el-text>
            <el-date-picker v-model="snowflakeEpoch" type="date" placeholder="不选则用默认纪元" format="YYYY-MM-DD" value-format="YYYY-MM-DD" style="width: 170px" />
            <el-button type="primary" :loading="snowflakeLoading" @click="generateSnowflake">生成</el-button>
            <el-button :disabled="!snowflakeIds.length" @click="copyAllSnowflakeIds">复制全部</el-button>
          </div>
          <div class="sf-results" v-if="snowflakeIds.length">
            <div class="sf-item" v-for="(id, i) in snowflakeIds" :key="i">
              <el-text type="info" size="small" class="sf-index">#{{ i + 1 }}</el-text>
              <span class="sf-id">{{ id }}</span>
              <el-button link type="primary" size="small" @click="copySnowflakeId(id)">复制</el-button>
            </div>
          </div>
          <el-empty v-else description="点击「生成」按钮创建雪花ID" :image-size="60" />
        </div>
      </el-tab-pane>
    </el-tabs>
  </div>
</template>

<style scoped>
.dev-tools {
  display: flex;
  flex-direction: column;
  height: 100%;
  overflow: hidden;
}

/* el-tabs 布局：纵向 flex，内容区撑满剩余空间 */
.tool-tabs {
  flex: 1;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}
.tool-tabs :deep(.el-tabs__header) {
  margin: 0;
  padding: 0 12px;
}
.tool-tabs :deep(.el-tabs__content) {
  flex: 1;
  overflow: hidden;
}
/* 激活的 pane 撑满内容区并纵向布局；非激活 pane 有内联 display:none 不受影响 */
.tool-tabs :deep(.el-tab-pane.tool-pane) {
  height: 100%;
  display: flex;
  flex-direction: column;
  overflow: hidden;
  padding: 10px 12px;
  gap: 8px;
}

/* 通用工具栏 */
.toolbar {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-shrink: 0;
}

/* Monaco 编辑器区域 */
.editor-area {
  flex: 1;
  border: 1px solid var(--el-border-color);
  border-radius: 4px;
  overflow: hidden;
  min-height: 200px;
}

/* 时间戳 */
.ts-panel {
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.ts-row {
  display: flex;
  align-items: center;
  gap: 8px;
}

/* 编解码 */
.codec-panel {
  display: flex;
  flex-direction: column;
  gap: 8px;
  flex: 1;
}
.codec-area {
  flex: 1;
  min-height: 80px;
}
.codec-area :deep(.el-textarea__inner) {
  font-family: 'Consolas', monospace;
  font-size: 13px;
  line-height: 1.5;
}

/* 正则 */
.regex-panel {
  display: flex;
  flex-direction: column;
  gap: 8px;
  flex: 1;
}
.regex-row {
  display: flex;
  align-items: center;
  gap: 4px;
  flex-shrink: 0;
}
.regex-slash {
  font-size: 18px;
  color: var(--el-text-color-secondary);
  font-family: monospace;
}
.regex-input {
  flex: 1;
}
.regex-flags {
  width: 80px;
}
.regex-text {
  flex: 1;
  min-height: 100px;
}
.regex-text :deep(.el-textarea__inner) {
  font-family: monospace;
  font-size: 13px;
  line-height: 1.6;
}
.regex-results {
  max-height: 200px;
  overflow-y: auto;
  border: 1px solid var(--el-border-color);
  border-radius: 4px;
}
.regex-match {
  padding: 6px 12px;
  border-bottom: 1px solid var(--el-border-color-lighter);
  display: flex;
  gap: 12px;
  align-items: center;
  font-size: 13px;
}
.regex-match:last-child {
  border-bottom: none;
}
.match-text {
  word-break: break-all;
}

/* 雪花ID */
.snowflake-panel {
  display: flex;
  flex-direction: column;
  gap: 10px;
  flex: 1;
}
.sf-results {
  flex: 1;
  overflow-y: auto;
  border: 1px solid var(--el-border-color);
  border-radius: 4px;
}
.sf-item {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 8px 12px;
  border-bottom: 1px solid var(--el-border-color-lighter);
}
.sf-item:last-child {
  border-bottom: none;
}
.sf-index {
  flex-shrink: 0;
}
.sf-id {
  color: var(--el-text-color-primary);
  font-family: 'Consolas', monospace;
  font-size: 14px;
  flex: 1;
  word-break: break-all;
}
</style>

<script setup lang="ts">
import { onMounted, onBeforeUnmount, ref, shallowRef, computed, watch, nextTick } from 'vue'
import { monaco } from '@/common/monacoSetup'

// ==================== 工具 Tab ====================
type ToolTab = 'json' | 'diff' | 'timestamp' | 'codec' | 'regex'
const activeToolTab = ref<ToolTab>('json')
const toolTabs: { key: ToolTab; label: string }[] = [
  { key: 'diff', label: '文本对比' },
  { key: 'json', label: 'JSON 工具' },
  { key: 'timestamp', label: '时间戳' },
  { key: 'codec', label: '编解码' },
  { key: 'regex', label: '正则测试' },
]

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
  // Chrome/Edge: "... at position 123"
  const posMatch = msg.match(/position\s+(\d+)/)
  if (posMatch) {
    const offset = parseInt(posMatch[1])
    return offsetToLineCol(raw, offset)
  }
  // Firefox: "... at line 5 column 10"
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

const codecMsg = ref('')
const codecMsgType = ref<'success' | 'error'>('success')

function codecEncode() {
  const s = codecInput.value
  if (!s) { codecMsg.value = '请输入内容'; codecMsgType.value = 'error'; return }
  try {
    switch (codecType.value) {
      case 'base64': codecOutput.value = btoa(unescape(encodeURIComponent(s))); break
      case 'url': codecOutput.value = encodeURIComponent(s); break
      case 'unicode': codecOutput.value = Array.from(s).map(c => '\\u' + c.charCodeAt(0).toString(16).padStart(4, '0')).join(''); break
      case 'html': codecOutput.value = s.replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c] ?? c)); break
    }
    codecMsg.value = '编码成功'; codecMsgType.value = 'success'
  } catch (e) {
    codecOutput.value = ''
    codecMsg.value = '编码失败：' + (e as Error).message; codecMsgType.value = 'error'
  }
}
function codecDecode() {
  const s = codecInput.value
  if (!s) { codecMsg.value = '请输入内容'; codecMsgType.value = 'error'; return }
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
    codecMsg.value = '解码成功'; codecMsgType.value = 'success'
  } catch (e) {
    codecOutput.value = ''
    codecMsg.value = '解码失败：' + (e as Error).message; codecMsgType.value = 'error'
  }
}
function codecSwap() {
  const tmp = codecInput.value
  codecInput.value = codecOutput.value
  codecOutput.value = tmp
  codecMsg.value = '已互换'; codecMsgType.value = 'success'
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
    // 防无限循环（无 g 标志只取一次）
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
    <!-- 工具 Tab 栏 -->
    <div class="tool-tabs">
      <button
        v-for="t in toolTabs" :key="t.key"
        :class="['tab-btn', { active: activeToolTab === t.key }]"
        @click="activeToolTab = t.key"
      >{{ t.label }}</button>
    </div>

    <!-- JSON 工具 -->
    <div v-show="activeToolTab === 'json'" class="tool-panel">
      <div class="toolbar">
        <button class="act-btn" @click="jsonFormat">格式化</button>
        <button class="act-btn" @click="jsonCompress">压缩</button>
        <button class="act-btn" @click="jsonEscape">转义</button>
        <button class="act-btn" @click="jsonUnescape">反转义</button>
        <button class="act-btn" @click="jsonValidate">校验</button>
        <span v-if="jsonMessage" :class="['msg', jsonMessageType]">{{ jsonMessage }}</span>
      </div>
      <div ref="jsonEditorEl" class="editor-area"></div>
    </div>

    <!-- 文本对比 -->
    <div v-show="activeToolTab === 'diff'" class="tool-panel">
      <div class="toolbar">
        <button class="act-btn" @click="diffCompare">对比</button>
        <button class="act-btn" @click="diffClear">清空</button>
        <span class="hint">左侧为原始文本，右侧为修改后文本，直接编辑即可</span>
      </div>
      <div ref="diffEditorEl" class="editor-area"></div>
    </div>

    <!-- 时间戳转换 -->
    <div v-show="activeToolTab === 'timestamp'" class="tool-panel ts-panel">
      <div class="ts-section">
        <h4>时间戳 → 日期</h4>
        <div class="ts-row">
          <input v-model="tsInput" placeholder="输入时间戳" class="ts-input" @keyup.enter="tsToDate" />
          <select v-model="tsUnit" class="ts-select">
            <option value="s">秒(s)</option>
            <option value="ms">毫秒(ms)</option>
          </select>
          <button class="act-btn" @click="tsToDate">转换</button>
          <button class="act-btn" @click="tsNow">当前时间</button>
        </div>
        <div :class="['ts-result', tsResultType]" v-if="tsResult">{{ tsResult }}</div>
      </div>
      <div class="ts-section">
        <h4>日期 → 时间戳</h4>
        <div class="ts-row">
          <input v-model="dtInput" placeholder="如 2025-01-01 12:00:00" class="ts-input wide" @keyup.enter="dateToTs" />
          <button class="act-btn" @click="dateToTs">转换</button>
        </div>
        <div :class="['ts-result', dtResultType]" v-if="dtResult">{{ dtResult }}</div>
      </div>
    </div>

    <!-- 编解码 -->
    <div v-show="activeToolTab === 'codec'" class="tool-panel codec-panel">
      <div class="toolbar">
        <select v-model="codecType" class="codec-select">
          <option value="base64">Base64</option>
          <option value="url">URL</option>
          <option value="unicode">Unicode</option>
          <option value="html">HTML 实体</option>
        </select>
        <button class="act-btn" @click="codecEncode">编码 ↓</button>
        <button class="act-btn" @click="codecDecode">解码 ↓</button>
        <button class="act-btn" @click="codecSwap">↕ 互换</button>
        <span v-if="codecMsg" :class="['msg', codecMsgType]">{{ codecMsg }}</span>
      </div>
      <textarea v-model="codecInput" class="codec-area" placeholder="输入内容"></textarea>
      <textarea v-model="codecOutput" class="codec-area" placeholder="输出结果" readonly></textarea>
    </div>

    <!-- 正则测试 -->
    <div v-show="activeToolTab === 'regex'" class="tool-panel regex-panel">
      <div class="regex-row">
        <span class="regex-slash">/</span>
        <input v-model="regexPattern" class="regex-input" placeholder="输入正则表达式" />
        <span class="regex-slash">/</span>
        <input v-model="regexFlags" class="regex-flags" placeholder="flags" />
      </div>
      <div class="regex-error" v-if="regexError">{{ regexError }}</div>
      <textarea v-model="regexTestText" class="regex-text" placeholder="输入测试文本"></textarea>
      <div class="regex-results" v-if="regexMatches.length">
        <div class="regex-match" v-for="(m, i) in regexMatches" :key="i">
          <span class="match-index">#{{ i + 1 }}</span>
          <span class="match-text">"{{ m.match }}"</span>
          <span class="match-pos">位置 {{ m.index }}</span>
          <span v-if="m.groups.length" class="match-groups">捕获组: {{ m.groups.join(', ') }}</span>
        </div>
      </div>
      <div class="regex-no-match" v-else-if="regexPattern && regexTestText">无匹配</div>
    </div>
  </div>
</template>

<style scoped>
.dev-tools {
  display: flex;
  flex-direction: column;
  height: 100%;
  overflow: hidden;
  background: #fff;
}
.tool-tabs {
  display: flex;
  gap: 2px;
  padding: 8px 12px 0;
  border-bottom: 1px solid #e4e7ed;
  background: #f5f7fa;
  flex-shrink: 0;
}
.tab-btn {
  padding: 7px 18px;
  border: none;
  background: transparent;
  cursor: pointer;
  font-size: 13px;
  border-radius: 4px 4px 0 0;
  color: #606266;
  transition: all .15s;
}
.tab-btn.active {
  background: #fff;
  color: #409eff;
  font-weight: 600;
  box-shadow: 0 -1px 3px rgba(0,0,0,.06);
}
.tab-btn:hover:not(.active) { background: #ecf5ff; }

.tool-panel {
  flex: 1;
  display: flex;
  flex-direction: column;
  overflow: hidden;
  padding: 10px 12px;
}
.toolbar {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 8px;
  flex-shrink: 0;
}
.act-btn {
  padding: 5px 14px;
  border: 1px solid #dcdfe6;
  background: #fff;
  border-radius: 4px;
  cursor: pointer;
  font-size: 13px;
  color: #606266;
  transition: all .15s;
}
.act-btn:hover { border-color: #409eff; color: #409eff; }
.msg { font-size: 12px; margin-left: 8px; }
.msg.success { color: #67c23a; }
.msg.error { color: #f56c6c; }
.hint { font-size: 12px; color: #909399; margin-left: 8px; }

.editor-area {
  flex: 1;
  border: 1px solid #e4e7ed;
  border-radius: 4px;
  overflow: hidden;
  min-height: 200px;
}

/* 时间戳 */
.ts-panel { gap: 20px; padding-top: 20px; }
.ts-section h4 { margin: 0 0 10px; font-size: 14px; color: #303133; }
.ts-row { display: flex; gap: 8px; align-items: center; }
.ts-input { padding: 6px 10px; border: 1px solid #dcdfe6; border-radius: 4px; font-size: 14px; width: 220px; }
.ts-input.wide { width: 300px; }
.ts-select { padding: 6px; border: 1px solid #dcdfe6; border-radius: 4px; font-size: 13px; }
.ts-result { margin-top: 8px; padding: 8px 12px; border-radius: 4px; font-size: 14px; font-family: monospace; }
.ts-result.success { background: #f0f9eb; color: #67c23a; }
.ts-result.error { background: #fef0f0; color: #f56c6c; }

/* 编解码 */
.codec-panel { gap: 8px; }
.codec-select { padding: 5px 10px; border: 1px solid #dcdfe6; border-radius: 4px; font-size: 13px; }
.codec-area {
  flex: 1;
  min-height: 100px;
  padding: 10px;
  border: 1px solid #e4e7ed;
  border-radius: 4px;
  font-size: 13px;
  font-family: 'Consolas', monospace;
  resize: none;
  line-height: 1.5;
}

/* 正则 */
.regex-panel { gap: 10px; }
.regex-row { display: flex; align-items: center; gap: 4px; }
.regex-slash { font-size: 18px; color: #909399; font-family: monospace; }
.regex-input { flex: 1; padding: 6px 10px; border: 1px solid #dcdfe6; border-radius: 4px; font-size: 14px; font-family: monospace; }
.regex-flags { width: 60px; padding: 6px 10px; border: 1px solid #dcdfe6; border-radius: 4px; font-size: 14px; font-family: monospace; }
.regex-error { color: #f56c6c; font-size: 12px; }
.regex-text {
  min-height: 120px;
  padding: 10px;
  border: 1px solid #e4e7ed;
  border-radius: 4px;
  font-size: 13px;
  font-family: monospace;
  resize: none;
  line-height: 1.6;
}
.regex-results { max-height: 200px; overflow-y: auto; }
.regex-match { padding: 6px 10px; border-bottom: 1px solid #f2f6fc; display: flex; gap: 12px; align-items: center; font-size: 13px; }
.match-index { color: #909399; font-weight: 600; }
.match-text { color: #e6a23c; font-family: monospace; word-break: break-all; }
.match-pos { color: #909399; font-size: 12px; }
.match-groups { color: #67c23a; font-size: 12px; }
.regex-no-match { color: #909399; font-size: 13px; padding: 8px; }
</style>

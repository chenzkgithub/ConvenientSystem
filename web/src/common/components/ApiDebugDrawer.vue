<template>
  <el-drawer
    :model-value="modelValue"
    title="接口调试"
    size="720px"
    :close-on-click-modal="true"
    @update:model-value="emit('update:modelValue', $event)"
  >
    <div class="debug-drawer">
      <!-- 请求行 -->
      <div class="req-line">
        <el-select v-model="method" class="req-method">
          <el-option v-for="m in METHODS" :key="m" :label="m" :value="m" />
        </el-select>
        <el-input
          v-model="url"
          class="req-url"
          placeholder="http://localhost:8030/api/..."
          spellcheck="false"
          @keyup.enter="send"
        />
        <el-button type="primary" :loading="sending" @click="send">发送</el-button>
      </div>
      <div class="req-opts">
        <el-checkbox v-model="attachToken">附带当前登录 Token</el-checkbox>
        <span class="req-opts-gap" />
        <span class="req-opts-label">超时</span>
        <el-input-number v-model="timeoutSec" :min="1" :max="120" size="small" class="timeout-input" />
        <span class="req-opts-unit">秒</span>
      </div>

      <!-- 请求头 -->
      <div class="block-head">
        <span>请求头</span>
        <el-button link type="primary" size="small" @click="addHeaderRow">+ 添加</el-button>
      </div>
      <div v-for="(row, i) in headerRows" :key="i" class="header-row">
        <el-input v-model="row.key" placeholder="Header 名，如 X-Custom" size="small" spellcheck="false" />
        <el-input v-model="row.value" placeholder="值" size="small" spellcheck="false" />
        <el-button link type="danger" size="small" @click="removeHeaderRow(i)">删除</el-button>
      </div>

      <!-- 请求体 -->
      <div class="block-head"><span>请求体</span></div>
      <el-input
        v-model="body"
        type="textarea"
        :rows="5"
        class="body-input"
        placeholder='{"key":"value"}（缺省 Content-Type 时按 application/json 发送）'
        spellcheck="false"
      />

      <!-- 响应 -->
      <div v-if="response" class="resp-section">
        <div class="resp-status">
          <el-tag :type="statusTagType" size="small" class="resp-code">{{ statusTagText }}</el-tag>
          <span class="resp-meta">{{ response.elapsedMs }} ms</span>
          <span class="resp-meta">{{ response.body.length }} 字符</span>
          <el-button link type="primary" size="small" class="resp-copy" @click="copyResponse">复制响应体</el-button>
        </div>
        <el-alert v-if="response.error" :title="response.error" type="error" :closable="false" class="resp-error" />
        <el-collapse v-if="headerEntries.length" class="resp-headers">
          <el-collapse-item :title="`响应头（${headerEntries.length}）`" name="headers">
            <div v-for="[k, v] in headerEntries" :key="k" class="resp-header-row">
              <span class="resp-header-k">{{ k }}</span>
              <span class="resp-header-v">{{ v }}</span>
            </div>
          </el-collapse-item>
        </el-collapse>
        <pre class="resp-body">{{ prettyBody || '（空响应体）' }}</pre>
      </div>
      <div v-else class="resp-empty">填写地址后点击「发送」，支持 GET / POST / PUT / DELETE / PATCH / HEAD / OPTIONS</div>

      <!-- 历史记录 -->
      <div class="block-head">
        <span>历史记录（{{ history.length }}）</span>
        <el-button v-if="history.length" link type="danger" size="small" @click="clearHistory">清空</el-button>
      </div>
      <div v-if="history.length" class="history-list">
        <div v-for="(h, i) in history" :key="i" class="history-item" :title="h.url" @click="restoreHistory(h)">
          <span class="hist-method" :class="'hm-' + h.method.toLowerCase()">{{ h.method }}</span>
          <span class="history-url">{{ h.url }}</span>
          <span class="history-time">{{ formatTime(h.time) }}</span>
        </div>
      </div>
      <div v-else class="history-empty">暂无历史，发送请求后自动记录最近 50 条</div>
    </div>
  </el-drawer>
</template>

<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { ElMessage } from 'element-plus'
import { getAuthToken } from '@/api/request'
import { sendApiDebug, type ApiDebugResponse } from '@/common/api/apiSpec'

const props = defineProps<{
  modelValue: boolean
  /** 打开抽屉时预填的接口（接口列表「调试」按钮传入；自由调试不传）。 */
  init?: { method: string; url: string } | null
}>()
const emit = defineEmits<{ 'update:modelValue': [value: boolean] }>()

const METHODS = ['GET', 'POST', 'PUT', 'DELETE', 'PATCH', 'HEAD', 'OPTIONS']
const HISTORY_KEY = 'api-debug-history-v1'
const HISTORY_MAX = 50

interface DebugHistoryItem {
  time: number
  method: string
  url: string
  headers: Record<string, string>
  body: string
}

const method = ref('GET')
const url = ref('')
const body = ref('')
/** 目标服务超时（秒），换算毫秒随请求发送 */
const timeoutSec = ref(30)
/** 发送时自动注入 Authorization: Bearer <当前登录 Token>（用户已手动填写 Authorization 时不覆盖） */
const attachToken = ref(true)
const headerRows = ref([{ key: '', value: '' }])
const sending = ref(false)
const response = ref<ApiDebugResponse | null>(null)
const history = ref<DebugHistoryItem[]>(loadHistory())

// 打开抽屉时按入口预填（带 init 视为调试新接口：清空请求体与请求头，避免误带上一次的参数）
watch(() => props.modelValue, open => {
  if (!open || !props.init) return
  method.value = (props.init.method || 'GET').toUpperCase()
  url.value = props.init.url
  body.value = ''
  headerRows.value = [{ key: '', value: '' }]
})

const statusTagType = computed(() => {
  const c = response.value?.statusCode ?? 0
  if (c === 0) return 'danger'
  if (c < 300) return 'success'
  if (c < 400) return 'warning'
  return 'danger'
})
const statusTagText = computed(() => {
  const r = response.value
  if (!r) return ''
  if (r.statusCode === 0) return '网络错误'
  return `${r.statusCode} ${r.statusText}`.trim()
})
const headerEntries = computed(() => Object.entries(response.value?.responseHeaders ?? {}))
const prettyBody = computed(() => {
  const b = response.value?.body
  if (!b) return ''
  try {
    return JSON.stringify(JSON.parse(b), null, 2)
  } catch {
    return b
  }
})

function addHeaderRow() {
  headerRows.value.push({ key: '', value: '' })
}
function removeHeaderRow(i: number) {
  headerRows.value.splice(i, 1)
  if (headerRows.value.length === 0) headerRows.value.push({ key: '', value: '' })
}

async function send() {
  const target = url.value.trim()
  if (!target) return ElMessage.warning('请填写目标接口地址')
  const headers: Record<string, string> = {}
  for (const r of headerRows.value) {
    const k = r.key.trim()
    if (k) headers[k] = r.value
  }
  const token = getAuthToken()
  if (attachToken.value && token && !Object.keys(headers).some(k => k.toLowerCase() === 'authorization')) {
    headers.Authorization = `Bearer ${token}`
  }
  sending.value = true
  try {
    response.value = await sendApiDebug({
      method: method.value,
      url: target,
      headers,
      body: body.value || undefined,
      timeoutMs: (timeoutSec.value || 30) * 1000,
    })
    saveHistory()
  } catch {
    /* 错误已由 request.ts 弹出提示 */
  } finally {
    sending.value = false
  }
}

async function copyResponse() {
  const text = prettyBody.value || response.value?.body || ''
  if (!text) return ElMessage.warning('响应体为空')
  try {
    await navigator.clipboard.writeText(text)
    ElMessage.success('已复制到剪贴板')
  } catch {
    ElMessage.error('复制失败：浏览器未授权剪贴板')
  }
}

function loadHistory(): DebugHistoryItem[] {
  try {
    const raw = localStorage.getItem(HISTORY_KEY)
    if (!raw) return []
    const list = JSON.parse(raw)
    return Array.isArray(list) ? list : []
  } catch {
    return []
  }
}

function saveHistory() {
  const item: DebugHistoryItem = {
    time: Date.now(),
    method: method.value,
    url: url.value.trim(),
    headers: Object.fromEntries(headerRows.value.filter(r => r.key.trim()).map(r => [r.key.trim(), r.value])),
    body: body.value,
  }
  // 同方法同地址去重：移除旧记录再置顶
  history.value = history.value.filter(h => !(h.method === item.method && h.url === item.url))
  history.value.unshift(item)
  history.value = history.value.slice(0, HISTORY_MAX)
  try {
    localStorage.setItem(HISTORY_KEY, JSON.stringify(history.value))
  } catch {
    /* 存储超限等异常忽略 */
  }
}

function restoreHistory(h: DebugHistoryItem) {
  method.value = h.method
  url.value = h.url
  body.value = h.body
  const rows = Object.entries(h.headers || {}).map(([key, value]) => ({ key, value }))
  headerRows.value = rows.length ? rows : [{ key: '', value: '' }]
}

function clearHistory() {
  history.value = []
  try {
    localStorage.removeItem(HISTORY_KEY)
  } catch {
    /* 忽略 */
  }
}

function formatTime(t: number): string {
  return new Date(t).toLocaleString('zh-CN', { hour12: false })
}
</script>

<style scoped>
.debug-drawer {
  display: flex;
  flex-direction: column;
  gap: 8px;
  font-size: 13px;
}

.req-line {
  display: flex;
  gap: 8px;
}
.req-method { width: 110px; flex: none; }
.req-url { flex: 1; }

.req-opts {
  display: flex;
  align-items: center;
  gap: 8px;
  color: var(--text-sub, #606266);
}
.req-opts-gap { flex: 1; }
.req-opts-label { font-size: 12px; }
.timeout-input { width: 100px; }
.req-opts-unit { font-size: 12px; }

.block-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  font-weight: 600;
  margin-top: 8px;
}

.header-row {
  display: flex;
  gap: 8px;
  align-items: center;
}
.header-row .el-input:first-child { width: 200px; flex: none; }
.header-row .el-input:nth-child(2) { flex: 1; }

.body-input :deep(textarea) {
  font-family: Consolas, Monaco, monospace;
  font-size: 12px;
}

.resp-section {
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 6px;
  padding: 10px 12px;
  margin-top: 8px;
}
.resp-status {
  display: flex;
  align-items: center;
  gap: 10px;
}
.resp-code { font-weight: 600; }
.resp-meta { font-size: 12px; color: var(--text-sub, #909399); }
.resp-copy { margin-left: auto; }
.resp-error { margin-top: 8px; }
.resp-headers { margin-top: 8px; --el-collapse-header-height: 34px; }
.resp-headers :deep(.el-collapse-item__header) { font-size: 12px; }
.resp-header-row {
  display: flex;
  gap: 12px;
  font-size: 12px;
  padding: 2px 0;
}
.resp-header-k {
  flex: none;
  width: 220px;
  color: var(--el-color-primary);
  font-family: Consolas, Monaco, monospace;
  overflow: hidden;
  text-overflow: ellipsis;
}
.resp-header-v {
  flex: 1;
  color: var(--text-sub, #606266);
  word-break: break-all;
}
.resp-body {
  margin: 8px 0 0;
  max-height: 320px;
  overflow: auto;
  background: var(--el-fill-color-blank);
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 4px;
  padding: 10px;
  font-family: Consolas, Monaco, monospace;
  font-size: 12px;
  line-height: 1.55;
  white-space: pre-wrap;
  word-break: break-all;
}

html.dark .resp-body {
  background: #0f172a;
  color: #cbd5e1;
}

.resp-empty {
  margin-top: 8px;
  padding: 24px 0;
  text-align: center;
  color: var(--text-sub, #909399);
  font-size: 12px;
  border: 1px dashed var(--el-border-color);
  border-radius: 6px;
}

.history-list {
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 6px;
  max-height: 200px;
  overflow: auto;
}
.history-item {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 6px 10px;
  font-size: 12px;
  cursor: pointer;
}
.history-item:hover { background: var(--el-fill-color-light); }
.history-item + .history-item { border-top: 1px solid var(--el-border-color-lighter); }
.hist-method {
  flex: none;
  width: 52px;
  text-align: center;
  border-radius: 3px;
  font-size: 10px;
  font-weight: 600;
  color: #fff;
  padding: 2px 0;
}
.hm-get { background: #409eff; }
.hm-post { background: #67c23a; }
.hm-put { background: #e6a23c; }
.hm-delete { background: #f56c6c; }
.hm-patch { background: #909399; }
.hm-head, .hm-options { background: #909399; }
.history-url {
  flex: 1;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-family: Consolas, Monaco, monospace;
}
.history-time {
  flex: none;
  color: var(--text-sub, #909399);
  font-size: 11px;
}
.history-empty {
  color: var(--text-sub, #909399);
  font-size: 12px;
  padding: 10px 0;
  text-align: center;
}
</style>

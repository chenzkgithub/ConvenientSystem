<script setup lang="ts">
/**
 * 实时日志查看器：轮询获取内存中的最近日志，支持关键字过滤和级别筛选。
 */
import { onMounted, onBeforeUnmount, ref, watch, nextTick } from 'vue'
import { ElMessage } from 'element-plus'
import { Refresh, Delete } from '@element-plus/icons-vue'
import { httpGet, httpPost } from '@/api/request'

interface LogItem {
  timestamp: string
  level: string
  category: string
  message: string
  exception: string | null
}

const loading = ref(false)
const logs = ref<LogItem[]>([])
const keyword = ref('')
const levelFilter = ref('')
const autoRefresh = ref(true)
const scrollEl = ref<HTMLElement>()
let timer: ReturnType<typeof setInterval> | null = null

async function load() {
  loading.value = true
  try {
    const params = new URLSearchParams()
    params.set('count', '200')
    if (keyword.value.trim()) params.set('keyword', keyword.value.trim())
    if (levelFilter.value) params.set('level', levelFilter.value)
    logs.value = await httpGet<LogItem[]>(`/api/Common/LogViewer/GetLogs?${params}`)
    nextTick(scrollToBottom)
  } catch (e) {
    console.error('加载日志失败', e)
  } finally {
    loading.value = false
  }
}

function scrollToBottom() {
  if (scrollEl.value) {
    scrollEl.value.scrollTop = scrollEl.value.scrollHeight
  }
}

async function clearLogs() {
  try {
    await httpPost('/api/Common/LogViewer/ClearLogs', {})
    logs.value = []
    ElMessage.success('已清空')
  } catch (e) {
    ElMessage.error((e as Error).message || '清空失败')
  }
}

function levelColor(level: string): string {
  switch (level) {
    case 'Critical': return '#dc2626'
    case 'Error': return '#ef4444'
    case 'Warning': return '#f59e0b'
    case 'Information': return '#3b82f6'
    case 'Debug': return '#6b7280'
    default: return '#94a3b8'
  }
}

function formatTime(ts: string): string {
  return ts.replace('T', ' ').substring(0, 23)
}

watch(autoRefresh, (val) => {
  if (val) startTimer()
  else stopTimer()
})

watch([keyword, levelFilter], () => { load() })

function startTimer() {
  stopTimer()
  timer = setInterval(load, 5000)
}

function stopTimer() {
  if (timer) { clearInterval(timer); timer = null }
}

onMounted(() => { load(); startTimer() })
onBeforeUnmount(stopTimer)
</script>

<template>
  <div class="log-viewer-page">
    <div class="page-header">
      <h2>实时日志</h2>
      <div class="header-actions">
        <el-input v-model="keyword" placeholder="搜索关键字" size="small" clearable style="width: 180px;" />
        <el-select v-model="levelFilter" placeholder="全部级别" size="small" clearable style="width: 120px;">
          <el-option label="Warning" value="Warning" />
          <el-option label="Error" value="Error" />
          <el-option label="Critical" value="Critical" />
          <el-option label="Information" value="Information" />
        </el-select>
        <el-switch v-model="autoRefresh" active-text="自动刷新" inactive-text="" size="small" />
        <el-button :icon="Refresh" size="small" @click="load" :loading="loading">刷新</el-button>
        <el-button v-if="$has('log-viewer:clear')" :icon="Delete" size="small" type="danger" @click="clearLogs">清空</el-button>
      </div>
    </div>

    <div class="log-container" ref="scrollEl">
      <div v-if="logs.length === 0 && !loading" class="log-empty">
        暂无日志记录（记录 Information 及以上级别）
      </div>
      <div v-for="(log, idx) in logs" :key="idx" class="log-entry" :class="'level-' + log.level.toLowerCase()">
        <span class="log-time">{{ formatTime(log.timestamp) }}</span>
        <span class="log-level" :style="{ color: levelColor(log.level) }">[{{ log.level }}]</span>
        <span class="log-cat">{{ log.category }}</span>
        <span class="log-msg">{{ log.message }}</span>
        <div v-if="log.exception" class="log-exception">{{ log.exception }}</div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.log-viewer-page {
  display: flex;
  flex-direction: column;
  height: 100%;
  overflow: hidden;
  padding: 12px 16px;
}
.page-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 12px;
  flex-shrink: 0;
}
.page-header h2 {
  font-size: 18px;
  font-weight: 600;
  color: var(--text-main, #0f172a);
  margin: 0;
}
.header-actions {
  display: flex;
  align-items: center;
  gap: 8px;
}
.log-container {
  flex: 1;
  overflow-y: auto;
  background: #1e1e2e;
  border-radius: var(--radius, 12px);
  padding: 12px 16px;
  font-family: 'Consolas', 'Courier New', monospace;
  font-size: 12px;
  line-height: 1.6;
}
.log-empty {
  text-align: center;
  color: #6b7280;
  padding: 40px;
}
.log-entry {
  padding: 2px 0;
  word-break: break-all;
  border-bottom: 1px solid rgba(255,255,255,0.05);
}
.log-time {
  color: #6b7280;
  margin-right: 8px;
}
.log-level {
  font-weight: 600;
  margin-right: 8px;
}
.log-cat {
  color: #8b5cf6;
  margin-right: 8px;
}
.log-msg {
  color: #e2e8f0;
}
.log-exception {
  color: #f87171;
  padding: 4px 0 4px 20px;
  white-space: pre-wrap;
  font-size: 11px;
}
.log-entry.level-error .log-msg { color: #fca5a5; }
.log-entry.level-critical .log-msg { color: #fca5a5; font-weight: 600; }
.log-entry.level-warning .log-msg { color: #fde68a; }
</style>

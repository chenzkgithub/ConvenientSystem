<script setup lang="ts">
/**
 * Hangfire 定时任务面板：展示周期任务列表，支持手动触发与执行日志查看。
 */
import { onMounted, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Refresh, VideoPlay, Document } from '@element-plus/icons-vue'
import { httpGet, httpPost } from '@/api/request'
import CommonDialog from '@/common/components/CommonDialog.vue'

interface JobItem {
  id: string
  cron: string
  nextExecution: string | null
  lastExecution: string | null
  lastState: string | null
  paused: boolean
  queue: string | null
  description: string | null
}

interface ExecutionLog {
  jobId: string
  state: string
  jobType: string | null
  methodName: string | null
  arguments: string | null
  startedAt: string | null
  error: string | null
  durationMs: number | null
}

const loading = ref(false)
const jobs = ref<JobItem[]>([])

// ===== 执行日志弹窗 =====
const logVisible = ref(false)
const logLoading = ref(false)
const logJobTitle = ref('')
const logRows = ref<ExecutionLog[]>([])

async function load() {
  loading.value = true
  try {
    jobs.value = await httpGet<JobItem[]>('/api/Common/HangfireJob/GetRecurringJobs')
  } catch (e) {
    console.error('加载 Hangfire 任务失败', e)
  } finally {
    loading.value = false
  }
}

async function triggerJob(job: JobItem) {
  try {
    await ElMessageBox.confirm(`确定手动触发任务 "${job.description || job.id}" ？`, '确认触发', { type: 'info' })
  } catch { return }
  try {
    await httpPost('/api/Common/HangfireJob/TriggerJob', { jobId: job.id })
    ElMessage.success('已触发任务')
    // 延迟刷新列表
    setTimeout(load, 1500)
  } catch (e) {
    ElMessage.error((e as Error).message || '触发失败')
  }
}

async function openLog(job: JobItem) {
  logJobTitle.value = job.id
  logRows.value = []
  logVisible.value = true
  logLoading.value = true
  try {
    logRows.value = await httpGet<ExecutionLog[]>(
      '/api/Common/HangfireJob/GetExecutionHistory',
      { recurringJobId: job.id },
    )
  } catch (e) {
    ElMessage.error((e as Error).message || '加载执行日志失败')
  } finally {
    logLoading.value = false
  }
}

function stateTagType(state: string | null): 'success' | 'danger' | 'warning' | 'info' {
  if (!state) return 'info'
  if (state === 'Succeeded') return 'success'
  if (state === 'Failed') return 'danger'
  if (state === 'Processing') return 'warning'
  return 'info'
}

/** 格式化参数 JSON 为可读多行 */
function formatArgs(raw: string | null): string {
  if (!raw) return '-'
  try {
    const arr = JSON.parse(raw)
    if (Array.isArray(arr) && arr.length === 1 && typeof arr[0] !== 'object') return String(arr[0])
    return JSON.stringify(arr, null, 2)
  } catch {
    return raw
  }
}

onMounted(load)
</script>

<template>
  <div class="hangfire-page" v-loading="loading">
    <div class="page-header">
      <h2>定时任务管理</h2>
      <el-button :icon="Refresh" size="small" @click="load" :loading="loading">刷新</el-button>
    </div>

    <div class="table-card">
      <el-table :data="jobs" border stripe size="small" empty-text="暂无定时任务">
        <el-table-column prop="id" label="任务标识" min-width="160" show-overflow-tooltip />
        <el-table-column prop="description" label="任务类型" min-width="180" show-overflow-tooltip />
        <el-table-column prop="cron" label="Cron 表达式" width="140" />
        <el-table-column prop="queue" label="队列" width="100" />
        <el-table-column prop="lastState" label="上次状态" width="100">
          <template #default="{ row }">
            <el-tag v-if="row.lastState" :type="stateTagType(row.lastState)" size="small">
              {{ row.lastState }}
            </el-tag>
            <span v-else class="text-muted">-</span>
          </template>
        </el-table-column>
        <el-table-column prop="lastExecution" label="上次执行" width="170" />
        <el-table-column prop="nextExecution" label="下次执行" width="170" />
        <el-table-column label="操作" width="150" fixed="right">
          <template #default="{ row }">
            <el-button link size="small" type="primary" :icon="Document" @click="openLog(row as JobItem)">日志</el-button>
            <el-button v-if="$has('hangfire-jobs:trigger')" link size="small" type="primary" :icon="VideoPlay" @click="triggerJob(row as JobItem)">触发</el-button>
          </template>
        </el-table-column>
      </el-table>
    </div>

    <!-- 执行日志弹窗 -->
    <CommonDialog v-model="logVisible" :title="`执行日志 - ${logJobTitle}`" width="900px" :close-on-click-modal="false" destroy-on-close>
      <el-table :data="logRows" border stripe size="small" v-loading="logLoading"
        empty-text="暂无执行记录" max-height="60vh"
        row-key="jobId" :default-expand-all="false">
        <el-table-column type="expand">
          <template #default="{ row }">
            <div class="log-expand">
              <div v-if="row.arguments" class="log-section">
                <div class="log-label">请求入参</div>
                <pre class="log-code">{{ formatArgs(row.arguments) }}</pre>
              </div>
              <div v-if="row.error" class="log-section">
                <div class="log-label log-label-danger">异常信息</div>
                <pre class="log-code log-error">{{ row.error }}</pre>
              </div>
              <div class="log-section">
                <div class="log-label">调用信息</div>
                <span class="log-meta">{{ row.methodName || '-' }}()</span>
                <span class="log-meta">日志 ID: {{ row.jobId }}</span>
              </div>
            </div>
          </template>
        </el-table-column>
        <el-table-column prop="state" label="状态" width="110"
          :filters="[
            { text: 'Succeeded', value: 'Succeeded' },
            { text: 'Failed', value: 'Failed' },
            { text: 'Processing', value: 'Processing' },
          ]"
          :filter-method="(value: string, row: ExecutionLog) => row.state === value">
          <template #default="{ row }">
            <el-tag :type="stateTagType(row.state)" size="small">{{ row.state }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="startedAt" label="执行时间" width="170" />
        <el-table-column prop="methodName" label="调用方法" min-width="160" show-overflow-tooltip>
          <template #default="{ row }">
            {{ row.methodName || '-' }}
            <span v-if="row.arguments" class="text-muted">({{ row.arguments.length > 30 ? row.arguments.slice(0, 30) + '…' : row.arguments }})</span>
          </template>
        </el-table-column>
        <el-table-column prop="durationMs" label="耗时" width="90" align="right">
          <template #default="{ row }">
            {{ row.durationMs != null ? row.durationMs + ' ms' : '-' }}
          </template>
        </el-table-column>
      </el-table>
    </CommonDialog>
  </div>
</template>

<style scoped>
.hangfire-page {
  padding: 20px;
  height: 100%;
  overflow-y: auto;
}
.page-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 16px;
}
.page-header h2 {
  font-size: 18px;
  font-weight: 600;
  color: var(--text-main, #0f172a);
  margin: 0;
}
.table-card {
  background: var(--surface, #fff);
  border: 1px solid var(--border, #e2e8f0);
  border-radius: var(--radius, 12px);
  overflow: hidden;
}
.text-muted {
  color: var(--text-sub, #94a3b8);
  font-size: 12px;
}

/* 执行日志展开区域 */
.log-expand {
  padding: 12px 20px;
}
.log-section {
  margin-bottom: 10px;
}
.log-label {
  font-weight: 600;
  font-size: 13px;
  color: var(--text-main, #303133);
  margin-bottom: 4px;
}
.log-label-danger {
  color: #f56c6c;
}
.log-code {
  background: #f5f7fa;
  border: 1px solid #e4e7ed;
  border-radius: 4px;
  padding: 8px 12px;
  font-size: 12px;
  line-height: 1.6;
  white-space: pre-wrap;
  word-break: break-all;
  max-height: 200px;
  overflow-y: auto;
  margin: 0;
}
.log-error {
  background: #fef0f0;
  border-color: #fbc4c4;
  color: #f56c6c;
}
.log-meta {
  display: inline-block;
  margin-right: 16px;
  font-size: 12px;
  color: var(--text-sub, #909399);
}
</style>

<script setup lang="ts">
/**
 * Hangfire 定时任务面板：展示周期任务列表，支持手动触发与执行日志查看。
 */
import { onMounted, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { VideoPlay, Document } from '@element-plus/icons-vue'
import { httpGet, httpPost } from '@/api/request'
import CommonDialog from '@/common/components/CommonDialog.vue'
import CommonDataTable, { type DataTableColumn } from '@/common/components/CommonDataTable.vue'

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

const columns: DataTableColumn<JobItem>[] = [
  { prop: 'id', label: '任务标识', minWidth: 160, sortable: true },
  { prop: 'description', label: '任务类型', minWidth: 180, sortable: true },
  { prop: 'cron', label: 'Cron 表达式', width: 140, sortable: true },
  { prop: 'queue', label: '队列', width: 100, sortable: true },
  { prop: 'lastState', label: '上次状态', width: 100, type: 'tag', tagType: (row) => stateTagType(row.lastState), sortable: true },
  { prop: 'lastExecution', label: '上次执行', width: 170, sortable: true },
  { prop: 'nextExecution', label: '下次执行', width: 170, sortable: true },
]

const logColumns: DataTableColumn<ExecutionLog>[] = [
  { prop: 'state', label: '状态', width: 110, type: 'tag', tagType: (row) => stateTagType(row.state), sortable: true },
  { prop: 'startedAt', label: '执行时间', width: 170, sortable: true },
  { prop: 'methodName', label: '调用方法', minWidth: 160, sortable: true },
  { prop: 'durationMs', label: '耗时', width: 90, sortable: true },
]

// ===== 执行日志弹窗 =====
const logVisible = ref(false)
const logLoading = ref(false)
const logJobTitle = ref('')
const logRows = ref<ExecutionLog[]>([])

const detailVisible = ref(false)
const currentDetail = ref<ExecutionLog | null>(null)

function showDetail(log: ExecutionLog) {
  currentDetail.value = log
  detailVisible.value = true
}

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
  <div class="hangfire-page">
    <div class="page-header">
      <h2>定时任务管理</h2>
    </div>

    <div class="table-card">
      <CommonDataTable
        :columns="columns"
        :data="jobs"
        :loading="loading"
        :show-pagination="false"
        :border="true"
        :stripe="true"
        size="small"
        show-refresh
        show-column-toggle
        table-key="hangfire-jobs"
        empty-text="暂无定时任务"
        @load="load"
      >
        <template #cell-lastState="{ row }">
          <el-tag v-if="row.lastState" :type="stateTagType(row.lastState)" size="small">
            {{ row.lastState }}
          </el-tag>
          <span v-else class="text-muted">-</span>
        </template>
        <template #actions="{ row }">
          <el-button link size="small" type="primary" :icon="Document" @click="openLog(row as JobItem)">日志</el-button>
          <el-button v-if="$has('hangfire-jobs:trigger')" link size="small" type="primary" :icon="VideoPlay" @click="triggerJob(row as JobItem)">触发</el-button>
        </template>
      </CommonDataTable>
    </div>

    <!-- 执行日志弹窗 -->
    <CommonDialog v-model="logVisible" :title="`执行日志 - ${logJobTitle}`" width="900px" :close-on-click-modal="false" destroy-on-close>
      <CommonDataTable
        :columns="logColumns"
        :data="logRows"
        :loading="logLoading"
        :show-pagination="false"
        :border="true"
        :stripe="true"
        size="small"
        show-refresh
        show-column-toggle
        table-key="hangfire-log"
        empty-text="暂无执行记录"
        max-height="60vh"
        @load="() => { if (logJobTitle) openLog({ id: logJobTitle } as JobItem) }"
      >
        <template #cell-methodName="{ row }">
          {{ row.methodName || '-' }}
          <span v-if="row.arguments" class="text-muted">({{ row.arguments.length > 30 ? row.arguments.slice(0, 30) + '…' : row.arguments }})</span>
        </template>
        <template #cell-durationMs="{ row }">
          {{ row.durationMs != null ? row.durationMs + ' ms' : '-' }}
        </template>
        <template #actions="{ row }">
          <el-button link size="small" type="primary" @click="showDetail(row as ExecutionLog)">详情</el-button>
        </template>
      </CommonDataTable>
    </CommonDialog>

    <!-- 执行详情弹窗 -->
    <CommonDialog v-model="detailVisible" title="执行详情" width="700px" destroy-on-close>
      <template v-if="currentDetail">
        <div class="log-section" v-if="currentDetail.arguments">
          <div class="log-label">请求入参</div>
          <pre class="log-code">{{ formatArgs(currentDetail.arguments) }}</pre>
        </div>
        <div class="log-section" v-if="currentDetail.error">
          <div class="log-label log-label-danger">异常信息</div>
          <pre class="log-code log-error">{{ currentDetail.error }}</pre>
        </div>
        <div class="log-section">
          <div class="log-label">调用信息</div>
          <span class="log-meta">{{ currentDetail.methodName || '-' }}()</span>
          <span class="log-meta">日志 ID: {{ currentDetail.jobId }}</span>
        </div>
      </template>
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

/* 执行日志详情 */
.log-section {
  margin-bottom: 12px;
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

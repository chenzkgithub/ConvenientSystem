<script setup lang="ts">
/**
 * Hangfire 定时任务面板：展示周期任务列表，支持手动触发。
 */
import { onMounted, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Refresh, VideoPlay } from '@element-plus/icons-vue'
import { httpGet, httpPost } from '@/api/request'

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

const loading = ref(false)
const jobs = ref<JobItem[]>([])

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

function stateTagType(state: string | null): 'success' | 'danger' | 'warning' | 'info' {
  if (!state) return 'info'
  if (state === 'Succeeded') return 'success'
  if (state === 'Failed') return 'danger'
  if (state === 'Processing') return 'warning'
  return 'info'
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
        <el-table-column prop="id" label="任务 ID" min-width="160" />
        <el-table-column prop="description" label="描述" min-width="180" show-overflow-tooltip />
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
        <el-table-column label="操作" width="100" fixed="right">
          <template #default="{ row }">
            <el-button link size="small" type="primary" :icon="VideoPlay" @click="triggerJob(row as JobItem)">触发</el-button>
          </template>
        </el-table-column>
      </el-table>
    </div>
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
}
</style>

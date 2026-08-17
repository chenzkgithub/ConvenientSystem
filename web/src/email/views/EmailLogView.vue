<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import { listEmailLogs, listEmailTasks } from '@/email/api/email'
import { formatCreator } from '@/common/formatCreator'
import type { EmailLogDto, EmailTaskDto } from '@/email/types'
import CommonDataTable, { type DataTableColumn } from '@/common/components/CommonDataTable.vue'

const loading = ref(false)
const logs = ref<EmailLogDto[]>([])
const total = ref(0)
const currentPage = ref(1)
const pageSize = ref(20)

const columns: DataTableColumn<EmailLogDto>[] = [
  { prop: 'taskName', label: '任务名称', minWidth: 120 },
  { prop: 'subject', label: '邮件主题', minWidth: 150 },
  { prop: 'recipients', label: '收件人', minWidth: 140 },
  { prop: 'status', label: '状态', width: 80, align: 'center', custom: true },
  { prop: 'content', label: '邮件内容', minWidth: 180 },
  { prop: 'errorMessage', label: '错误信息', minWidth: 150, custom: true },
  { prop: 'costMs', label: '耗时', width: 80, align: 'center', custom: true },
  { prop: 'createTime', label: '发送时间', width: 160, type: 'date' },
  { prop: 'createdByName', label: '创建人', width: 150, formatter: (row) => formatCreator(row) },
]

// ========== 筛选 ==========
const filterTaskId = ref<number | undefined>(undefined)
const filterStatus = ref<number | undefined>(undefined)
const filterDateRange = ref<[string, string] | null>(null)
const taskOptions = ref<{ id: number; name: string }[]>([])

async function loadTaskOptions() {
  try {
    const tasks = await listEmailTasks()
    taskOptions.value = tasks.map((t: EmailTaskDto) => ({ id: t.id, name: t.name }))
  } catch {}
}

async function loadData() {
  loading.value = true
  try {
    const params: Record<string, unknown> = {
      page: currentPage.value,
      size: pageSize.value,
    }
    if (filterTaskId.value != null) params.taskId = filterTaskId.value
    const res = await listEmailLogs(params)
    logs.value = res.list
    total.value = res.total
  } catch {
    /* 错误已由 request.ts 弹出提示 */
  } finally {
    loading.value = false
  }
}

function handleFilter() {
  currentPage.value = 1
  loadData()
}

function handleReset() {
  filterTaskId.value = undefined
  filterStatus.value = undefined
  filterDateRange.value = null
  handleFilter()
}

// 前端二次过滤（状态和日期后端暂未支持，在前端过滤当前页）
const filteredLogs = ref<EmailLogDto[]>([])

function applyClientFilter() {
  let result = [...logs.value]
  if (filterStatus.value != null) {
    result = result.filter(l => l.status === filterStatus.value)
  }
  if (filterDateRange.value) {
    const [start, end] = filterDateRange.value
    result = result.filter(l => {
      const d = l.createTime?.substring(0, 10) || ''
      return d >= start.substring(0, 10) && d <= end.substring(0, 10)
    })
  }
  filteredLogs.value = result
}

watch([filterStatus, filterDateRange, logs], applyClientFilter, { immediate: true })

onMounted(() => {
  loadTaskOptions()
  loadData()
})
</script>

<template>
  <div class="email-log-page">
    <CommonDataTable
      :columns="columns"
      :data="filteredLogs"
      :loading="loading"
      :total="total"
      v-model:page="currentPage"
      :page-size="pageSize"
      empty-text="暂无发送日志"
      searchable
      @load="loadData"
      @search="handleFilter"
      @reset="handleReset"
    >
      <template #filters>
        <el-select v-model="filterTaskId" placeholder="全部任务" clearable style="width: 160px" @change="handleFilter">
          <el-option v-for="t in taskOptions" :key="t.id" :label="t.name" :value="t.id" />
        </el-select>
        <el-select v-model="filterStatus" placeholder="全部状态" clearable style="width: 120px">
          <el-option label="成功" :value="1" />
          <el-option label="失败" :value="0" />
        </el-select>
        <el-date-picker
          v-model="filterDateRange"
          type="daterange"
          range-separator="至"
          start-placeholder="开始日期"
          end-placeholder="结束日期"
          value-format="YYYY-MM-DD"
          style="width: 260px"
        />
      </template>
      <template #toolbar>
        <el-button @click="loadData">刷新</el-button>
      </template>

      <template #cell-status="{ row }">
        <el-tag :type="row.status === 1 ? 'success' : 'danger'" size="small">
          {{ row.status === 1 ? '成功' : '失败' }}
        </el-tag>
      </template>
      <template #cell-errorMessage="{ row }">
        <span v-if="row.errorMessage" class="error-text">{{ row.errorMessage }}</span>
        <span v-else class="text-muted">-</span>
      </template>
      <template #cell-costMs="{ row }">
        <span :class="{ 'fast-ms': row.costMs < 2000 }">{{ row.costMs }}ms</span>
      </template>
    </CommonDataTable>
  </div>
</template>

<style scoped>
.email-log-page {
  display: flex;
  flex-direction: column;
  height: 100%;
  overflow: hidden;
}

.error-text {
  color: var(--el-color-danger);
}

.text-muted {
  color: var(--el-text-color-placeholder);
}

.fast-ms {
  color: var(--el-color-success);
}
</style>

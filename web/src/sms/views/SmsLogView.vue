<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { listLogs, getStatistics, getQuota } from '@/sms/api/sms'
import type { SmsLogDto, SmsStatisticsDto, SmsQuotaDto } from '@/sms/types'
import { useDataTable } from '@/common/composables/useDataTable'
import CommonDataTable, { type DataTableColumn } from '@/common/components/CommonDataTable.vue'

// ========== 统计卡片 ==========
const stats = ref<SmsStatisticsDto>({ todayCount: 0, monthCount: 0, successRate: 0, dailyRemaining: 0 })
const quota = ref<SmsQuotaDto>({ dailyMax: 100, monthlyMax: 3000, dailyUsed: 0, monthlyUsed: 0 })

async function loadStats() {
  try {
    const [s, q] = await Promise.all([getStatistics(), getQuota()])
    stats.value = s
    quota.value = q
  } catch {}
}

const dailyPercent = ref(0)
const monthlyPercent = ref(0)

function calcPercent() {
  dailyPercent.value = quota.value.dailyMax > 0 ? Math.round((quota.value.dailyUsed / quota.value.dailyMax) * 100) : 0
  monthlyPercent.value = quota.value.monthlyMax > 0 ? Math.round((quota.value.monthlyUsed / quota.value.monthlyMax) * 100) : 0
}

// ========== 日志列表 ==========
// 筛选条件：字段名直接对齐接口参数名，非空字段由 useDataTable 自动并入请求
const filters = reactive({
  phone: '',
  status: undefined as number | undefined,
  dateRange: null as [string, string] | null,
})

// immediate: false —— 首屏需要先出统计卡片再拉日志，加载顺序由下方 onMounted 统一编排
const { loading, list, total, page, size, load, search, reset, onSortChange } = useDataTable<SmsLogDto, typeof filters>(listLogs, {
  filters,
  immediate: false,
  // dateRange 仅供日期控件绑定，接口要的是 startTime / endTime
  extraParams: (f) => ({
    dateRange: undefined,
    startTime: f.dateRange?.[0],
    endTime: f.dateRange?.[1],
  }),
})

const columns: DataTableColumn<SmsLogDto>[] = [
  { prop: 'createTime', label: '时间', width: 170, type: 'date', sortable: 'custom' },
  { prop: 'phone', label: '手机号', width: 130, sortable: 'custom' },
  {
    prop: 'status',
    label: '状态',
    width: 80,
    type: 'tag',
    tagType: (row) => (row.status === 1 ? 'success' : row.status === 2 ? 'danger' : 'info'),
    formatter: (row) => (row.status === 0 ? '待发送' : row.status === 1 ? '成功' : '失败'),
    sortable: 'custom',
  },
  { prop: 'content', label: '发送内容', minWidth: 260, showOverflowTooltip: true, sortable: 'custom' },
  { prop: 'errorMessage', label: '错误信息', minWidth: 160, showOverflowTooltip: true, sortable: 'custom' },
  { prop: 'costMs', label: '耗时', width: 80, formatter: (row) => `${row.costMs}ms`, sortable: 'custom' },
]

onMounted(async () => {
  await loadStats()
  calcPercent()
  await load()
})
</script>

<template>
  <div class="sms-log-page">
    <!-- 统计卡片 -->
    <div class="stat-cards">
      <div class="stat-card">
        <div class="stat-num">{{ stats.todayCount }}</div>
        <div class="stat-label">今日发送</div>
      </div>
      <div class="stat-card">
        <div class="stat-num">{{ stats.monthCount }}</div>
        <div class="stat-label">本月发送</div>
      </div>
      <div class="stat-card">
        <div class="stat-num">{{ stats.successRate.toFixed(1) }}%</div>
        <div class="stat-label">成功率</div>
      </div>
      <div class="stat-card">
        <div class="stat-num">{{ stats.dailyRemaining }}</div>
        <div class="stat-label">今日剩余配额</div>
      </div>
    </div>

    <!-- 配额进度 -->
    <div class="quota-bar">
      <div class="quota-item">
        <span class="quota-label">日配额</span>
        <el-progress :percentage="dailyPercent" :color="dailyPercent > 80 ? '#f56c6c' : '#3b82f6'" :stroke-width="14" :text-inside="true" style="flex: 1" />
        <span class="quota-text">{{ quota.dailyUsed }} / {{ quota.dailyMax }}</span>
      </div>
      <div class="quota-item">
        <span class="quota-label">月配额</span>
        <el-progress :percentage="monthlyPercent" :color="monthlyPercent > 80 ? '#f56c6c' : '#3b82f6'" :stroke-width="14" :text-inside="true" style="flex: 1" />
        <span class="quota-text">{{ quota.monthlyUsed }} / {{ quota.monthlyMax }}</span>
      </div>
    </div>

    <!-- 日志列表 -->
    <CommonDataTable
      show-refresh
      show-column-toggle
      table-key="sms-log"
      @load="load"
      @sort-change="onSortChange"
      class="log-table"
      v-model:page="page"
      v-model:pageSize="size"
      :columns="columns"
      :data="list"
      :loading="loading"
      :total="total"
      :show-actions="false"
      searchable
      pagination-layout="prev, pager, next"
      @search="search"
      @reset="reset"
    >
      <template #filters>
        <el-input v-model="filters.phone" placeholder="手机号" clearable style="width: 150px" @clear="search" @keyup.enter="search" />
        <el-select v-model="filters.status" placeholder="全部状态" clearable style="width: 120px" @change="search">
          <el-option label="成功" :value="1" />
          <el-option label="失败" :value="2" />
        </el-select>
        <el-date-picker
          v-model="filters.dateRange"
          type="daterange"
          range-separator="至"
          start-placeholder="开始日期"
          end-placeholder="结束日期"
          value-format="YYYY-MM-DD"
          style="width: 260px"
          @change="search"
        />
      </template>

      <template #empty>暂无日志数据</template>
    </CommonDataTable>
  </div>
</template>

<style scoped>
.sms-log-page {
  display: flex;
  flex-direction: column;
  height: 100%;
  gap: 0;
  overflow: hidden;
}
.stat-cards {
  display: flex;
  gap: 12px;
  padding: 12px;
  flex-shrink: 0;
}
.stat-card {
  flex: 1;
  background: #fff;
  border-radius: 10px;
  padding: 16px;
  text-align: center;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.05);
}
.stat-num {
  font-size: 26px;
  font-weight: 700;
  color: var(--el-color-primary, #3b82f6);
}
.stat-label {
  font-size: 13px;
  color: #6b7280;
  margin-top: 4px;
}
.quota-bar {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 0 12px 8px;
  flex-shrink: 0;
}
.quota-item {
  display: flex;
  align-items: center;
  gap: 10px;
  background: #fff;
  border-radius: 8px;
  padding: 8px 14px;
}
.quota-label {
  font-size: 13px;
  color: #6b7280;
  width: 50px;
  flex-shrink: 0;
}
.quota-text {
  font-size: 12px;
  color: #6b7280;
  width: 90px;
  text-align: right;
  flex-shrink: 0;
}
.log-table {
  flex: 1;
  min-height: 0;
}
</style>

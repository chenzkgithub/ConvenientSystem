<script setup lang="ts">
import { reactive, ref } from 'vue'
import { listAuditLogs, type AuditLogDto } from '@/common/api/audit'
import { formatDate } from '@/common/formatDate'
import { useDataTable } from '@/common/composables/useDataTable'
import CommonDataTable, { type DataTableColumn } from '@/common/components/CommonDataTable.vue'
import CommonDialog from '@/common/components/CommonDialog.vue'

// 筛选条件：字段名直接对齐接口参数名，非空字段由 useDataTable 自动并入请求
const filters = reactive({
  account: '',
  module: '',
  success: undefined as boolean | undefined,
  dateRange: null as [string, string] | null,
})

const { loading, list, total, page, size, load, search, reset, onSortChange } = useDataTable<AuditLogDto, typeof filters>(
  listAuditLogs,
  {
    filters,
    // dateRange 仅供日期控件绑定，接口要的是 startTime / endTime；
    // 结束日补齐到当天末尾，否则选到今天会漏掉今天的记录。
    extraParams: (f) => ({
      dateRange: undefined,
      startTime: f.dateRange?.[0],
      endTime: f.dateRange ? f.dateRange[1] + ' 23:59:59' : undefined,
    }),
  }
)

// 详情弹窗
const detailVisible = ref(false)
const detailRow = ref<AuditLogDto | null>(null)

const columns: DataTableColumn<AuditLogDto>[] = [
  {
    prop: 'createTime',
    label: '时间',
    width: 170,
    type: 'date',
    sortable: 'custom',
  },
  {
    prop: 'account',
    label: '账号',
    width: 120,
    showOverflowTooltip: true,
    sortable: 'custom',
  },
  {
    prop: 'module',
    label: '模块',
    width: 100,
    sortable: 'custom',
  },
  {
    prop: 'method',
    label: '方法',
    width: 90,
    type: 'tag',
    tagType: (row) => {
      if (row.method === 'DELETE') return 'danger'
      if (row.method === 'POST') return 'success'
      if (row.method === 'PUT') return 'warning'
      return 'info'
    },
    sortable: 'custom',
  },
  {
    prop: 'action',
    label: '操作',
    minWidth: 160,
    showOverflowTooltip: true,
    sortable: 'custom',
  },
  {
    prop: 'path',
    label: '路径',
    minWidth: 200,
    showOverflowTooltip: true,
    sortable: 'custom',
  },
  {
    prop: 'success',
    label: '结果',
    width: 90,
    type: 'tag',
    tagType: (row) => (row.success ? 'success' : 'danger'),
    formatter: (row) => `${row.success ? '成功' : '失败'} ${row.statusCode}`,
    sortable: 'custom',
  },
  {
    prop: 'ip',
    label: 'IP',
    width: 130,
    showOverflowTooltip: true,
    sortable: 'custom',
  },
  {
    prop: 'costMs',
    label: '耗时',
    width: 80,
    formatter: (row) => `${row.costMs}ms`,
    sortable: 'custom',
  },
]

function showDetail(row: AuditLogDto) {
  detailRow.value = row
  detailVisible.value = true
}
</script>

<template>
  <div class="audit-log-page">
    <CommonDataTable
      show-refresh
      show-column-toggle
      table-key="audit-log"
      @load="load"
      @sort-change="onSortChange"
      v-model:page="page"
      v-model:pageSize="size"
      :columns="columns"
      :data="list"
      :loading="loading"
      :total="total"
      :actions-width="80"
      searchable
      pagination-layout="prev, pager, next"
      @search="search"
      @reset="reset"
      @row-dblclick="(row: AuditLogDto) => showDetail(row)"
    >
      <template #filters>
        <el-input v-model="filters.account" placeholder="操作账号" clearable style="width: 140px" @clear="search" @keyup.enter="search" />
        <el-input v-model="filters.module" placeholder="模块（如 Sms）" clearable style="width: 150px" @clear="search" @keyup.enter="search" />
        <el-select v-model="filters.success" placeholder="全部结果" clearable style="width: 120px" @change="search">
          <el-option label="成功" :value="true" />
          <el-option label="失败" :value="false" />
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

      <template #actions="{ row }">
        <el-button link type="primary" size="small" @click="showDetail(row as AuditLogDto)">详情</el-button>
      </template>

      <template #empty>暂无审计日志</template>
    </CommonDataTable>

    <!-- 详情弹窗 -->
    <CommonDialog v-model="detailVisible" title="审计详情" width="600px">
      <el-descriptions :column="1" border v-if="detailRow">
        <el-descriptions-item label="时间">{{ formatDate(detailRow.createTime) }}</el-descriptions-item>
        <el-descriptions-item label="账号">{{ detailRow.account || '(匿名)' }}</el-descriptions-item>
        <el-descriptions-item label="模块">{{ detailRow.module }}</el-descriptions-item>
        <el-descriptions-item label="方法">{{ detailRow.method }}</el-descriptions-item>
        <el-descriptions-item label="操作">{{ detailRow.action }}</el-descriptions-item>
        <el-descriptions-item label="路径">{{ detailRow.path }}</el-descriptions-item>
        <el-descriptions-item label="IP">{{ detailRow.ip }}</el-descriptions-item>
        <el-descriptions-item label="结果">{{ detailRow.success ? '成功' : '失败' }}（{{ detailRow.statusCode }}）</el-descriptions-item>
        <el-descriptions-item label="耗时">{{ detailRow.costMs }}ms</el-descriptions-item>
        <el-descriptions-item label="请求参数">
          <pre class="param-pre">{{ detailRow.paramSummary || '(无)' }}</pre>
        </el-descriptions-item>
      </el-descriptions>
    </CommonDialog>
  </div>
</template>

<style scoped>
.audit-log-page {
  height: 100%;
  overflow: hidden;
}
.param-pre {
  margin: 0;
  white-space: pre-wrap;
  word-break: break-all;
  max-height: 240px;
  overflow: auto;
  font-size: 12px;
}
</style>

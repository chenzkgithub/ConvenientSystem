<script setup lang="ts">
import { reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { listErrorLogs, clearErrorLogs, type ErrorLogDto } from '@/common/api/errorLog'
import { formatDate } from '@/common/formatDate'
import { useDataTable } from '@/common/composables/useDataTable'
import { confirmAndRun } from '@/common/utils/confirm'
import CommonDataTable, { type DataTableColumn } from '@/common/components/CommonDataTable.vue'
import CommonDialog from '@/common/components/CommonDialog.vue'

// 筛选条件：字段名直接对齐接口参数名，非空字段由 useDataTable 自动并入请求
const filters = reactive({
  keyword: '',
  dateRange: null as [string, string] | null,
})

const { loading, list, total, page, size, load, search, reset } = useDataTable<ErrorLogDto, typeof filters>(
  listErrorLogs,
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
const detailRow = ref<ErrorLogDto | null>(null)

const columns: DataTableColumn<ErrorLogDto>[] = [
  {
    prop: 'createTime',
    label: '时间',
    width: 170,
    type: 'date',
  },
  {
    prop: 'account',
    label: '账号',
    width: 120,
    showOverflowTooltip: true,
  },
  {
    prop: 'method',
    label: '方法',
    width: 80,
    type: 'tag',
    tagType: () => 'info',
  },
  {
    prop: 'statusCode',
    label: '状态码',
    width: 90,
    type: 'tag',
    tagType: () => 'danger',
  },
  {
    prop: 'exceptionType',
    label: '异常类型',
    minWidth: 200,
    showOverflowTooltip: true,
  },
  {
    prop: 'errorMessage',
    label: '错误消息',
    minWidth: 300,
    showOverflowTooltip: true,
  },
  {
    prop: 'path',
    label: '路径',
    minWidth: 200,
    showOverflowTooltip: true,
  },
  {
    prop: 'ip',
    label: 'IP',
    width: 130,
    showOverflowTooltip: true,
  },
]

function showDetail(row: ErrorLogDto) {
  detailRow.value = row
  detailVisible.value = true
}

async function handleClear() {
  let cleared = 0
  // successText 置空：成功提示需带上清空条数，由这里自行弹出
  const ok = await confirmAndRun(
    '确定清空全部错误日志？此操作不可撤销。',
    async () => {
      cleared = await clearErrorLogs()
    },
    { title: '警告', confirmButtonText: '确定清空', successText: '' }
  )
  if (!ok) return
  ElMessage.success(`已清空 ${cleared} 条错误日志`)
  await search()
}
</script>

<template>
  <div class="error-log-page">
    <CommonDataTable
      v-model:page="page"
      v-model:pageSize="size"
      :columns="columns"
      :data="list"
      :loading="loading"
      :total="total"
      :actions-width="80"
      searchable
      pagination-layout="prev, pager, next"
      @load="load"
      @search="search"
      @reset="reset"
      @row-dblclick="(row: ErrorLogDto) => showDetail(row)"
    >
      <template #filters>
        <el-input
          v-model="filters.keyword"
          placeholder="搜索错误消息/路径/异常类型"
          clearable
          style="width: 280px"
          @clear="search"
          @keyup.enter="search"
        />
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

      <template #toolbar>
        <el-button type="danger" plain @click="handleClear">清空</el-button>
      </template>

      <template #actions="{ row }">
        <el-button link type="primary" size="small" @click="showDetail(row as ErrorLogDto)">详情</el-button>
      </template>

      <template #empty>暂无错误日志</template>
    </CommonDataTable>

    <!-- 详情弹窗 -->
    <CommonDialog v-model="detailVisible" title="错误详情" width="700px">
      <el-descriptions :column="1" border v-if="detailRow">
        <el-descriptions-item label="时间">{{ formatDate(detailRow.createTime) }}</el-descriptions-item>
        <el-descriptions-item label="账号">{{ detailRow.account || '(匿名)' }}</el-descriptions-item>
        <el-descriptions-item label="方法">{{ detailRow.method }}</el-descriptions-item>
        <el-descriptions-item label="状态码">{{ detailRow.statusCode }}</el-descriptions-item>
        <el-descriptions-item label="路径">{{ detailRow.path }}</el-descriptions-item>
        <el-descriptions-item label="IP">{{ detailRow.ip }}</el-descriptions-item>
        <el-descriptions-item label="异常类型">{{ detailRow.exceptionType }}</el-descriptions-item>
        <el-descriptions-item label="错误消息">{{ detailRow.errorMessage }}</el-descriptions-item>
        <el-descriptions-item label="堆栈跟踪">
          <pre class="stack-pre">{{ detailRow.stackTrace || '(无)' }}</pre>
        </el-descriptions-item>
      </el-descriptions>
    </CommonDialog>
  </div>
</template>

<style scoped>
.error-log-page {
  height: 100%;
  overflow: hidden;
}
.stack-pre {
  margin: 0;
  white-space: pre-wrap;
  word-break: break-all;
  max-height: 320px;
  overflow: auto;
  font-size: 12px;
}
</style>

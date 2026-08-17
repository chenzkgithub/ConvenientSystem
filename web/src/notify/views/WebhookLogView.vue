<script setup lang="ts">
import { reactive } from 'vue'
import { listWebhookLogs } from '@/notify/api/notify'
import { PROVIDER_LABELS, type WebhookLogDto } from '@/notify/types'
import { useDataTable } from '@/common/composables/useDataTable'
import CommonDataTable, { type DataTableColumn } from '@/common/components/CommonDataTable.vue'

// 筛选条件：字段名直接对齐接口参数名，非空字段由 useDataTable 自动并入请求
const filters = reactive({
  configName: '',
  success: undefined as boolean | undefined,
})

const { loading, list, total, page, size, load, search, reset } = useDataTable<WebhookLogDto, typeof filters>(
  listWebhookLogs,
  { filters }
)

const columns: DataTableColumn<WebhookLogDto>[] = [
  { prop: 'configName', label: '配置名称', minWidth: 140 },
  { prop: 'providerType', label: '类型', width: 110, custom: true },
  { prop: 'title', label: '标题', minWidth: 160 },
  { prop: 'success', label: '状态', width: 90, align: 'center', custom: true },
  { prop: 'errorMessage', label: '错误信息', minWidth: 180, custom: true },
  { prop: 'costMs', label: '耗时', width: 90, align: 'center', custom: true },
  { prop: 'createTime', label: '发送时间', width: 170, type: 'date' },
]

function providerLabel(t: string) {
  return PROVIDER_LABELS[t] || t
}
</script>

<template>
  <div class="webhook-log-page">
    <CommonDataTable
      :columns="columns"
      :data="list"
      :loading="loading"
      :total="total"
      v-model:page="page"
      v-model:pageSize="size"
      empty-text="暂无发送日志"
      searchable
      @load="load"
      @search="search"
      @reset="reset"
    >
      <template #filters>
        <el-input
          v-model="filters.configName"
          placeholder="配置名称"
          clearable
          style="width: 160px"
          @keyup.enter="search"
        />
        <el-select
          v-model="filters.success"
          placeholder="全部状态"
          clearable
          style="width: 120px"
          @change="search"
        >
          <el-option label="成功" :value="true" />
          <el-option label="失败" :value="false" />
        </el-select>
      </template>
      <template #toolbar>
        <el-button @click="load">刷新</el-button>
      </template>

      <template #cell-providerType="{ row }">
        <el-tag size="small">{{ providerLabel(row.providerType) }}</el-tag>
      </template>
      <template #cell-success="{ row }">
        <el-tag :type="row.success ? 'success' : 'danger'" size="small">
          {{ row.success ? '成功' : '失败' }}
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
.webhook-log-page {
  display: flex;
  flex-direction: column;
  height: 100%;
  overflow: hidden;
}

.error-text {
  color: var(--el-color-danger);
  word-break: break-all;
}

.text-muted {
  color: var(--el-text-color-placeholder);
}

.fast-ms {
  color: var(--el-color-success);
}
</style>

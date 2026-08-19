<script setup lang="ts">
import { onMounted, onUnmounted, ref } from 'vue'
import { listOnlineUsers, type OnlineUserDto } from '@/common/api/userOnline'
import { formatDate } from '@/common/formatDate'
import CommonDataTable, { type DataTableColumn } from '@/common/components/CommonDataTable.vue'

const loading = ref(false)
const list = ref<OnlineUserDto[]>([])
const autoRefresh = ref(true)
let timer: ReturnType<typeof setInterval> | null = null

const columns: DataTableColumn<OnlineUserDto>[] = [
  { prop: 'account', label: '账号', width: 140, sortable: true },
  { prop: 'displayName', label: '显示名称', minWidth: 120, formatter: (row) => row.displayName || '—', sortable: true },
  { prop: 'ip', label: 'IP 地址', width: 150, sortable: true },
  { prop: 'loginTime', label: '登录时间', width: 170, dateFormatter: formatDate, sortable: true },
  { prop: 'lastSeen', label: '最后活跃', width: 260, custom: true, sortable: true },
]

async function loadData() {
  loading.value = true
  try {
    list.value = await listOnlineUsers()
  } catch {
    /* 错误已由 request.ts 弹出提示 */
  } finally {
    loading.value = false
  }
}

function startAutoRefresh() {
  stopAutoRefresh()
  if (autoRefresh.value) timer = setInterval(loadData, 30_000)
}

function stopAutoRefresh() {
  if (timer) { clearInterval(timer); timer = null }
}

function toggleAutoRefresh() {
  autoRefresh.value = !autoRefresh.value
  if (autoRefresh.value) startAutoRefresh()
  else stopAutoRefresh()
}

/** 距离最后活跃的时间描述 */
function lastSeenAgo(lastSeen: string): string {
  const diff = Math.floor((Date.now() - new Date(lastSeen).getTime()) / 1000)
  if (diff < 60) return `${diff} 秒前`
  const m = Math.floor(diff / 60)
  if (m < 60) return `${m} 分钟前`
  return `${Math.floor(m / 60)} 小时前`
}

onMounted(() => { loadData(); startAutoRefresh() })
onUnmounted(stopAutoRefresh)
</script>

<template>
  <div class="online-page">
    <!-- 在线用户列表（标题提示与刷新按钮封装进列表组件插槽，与表格统一对齐） -->
    <CommonDataTable
      show-refresh
      show-column-toggle
      table-key="user-online"
      @load="loadData"
      :columns="columns"
      :data="list"
      :loading="loading"
      :total="list.length"
      :show-pagination="false"
      :refresh-on-activated="false"
      empty-text="暂无在线用户"
    >
      <template #filters>
        <span class="title">在线用户</span>
        <el-tag type="success" size="small" class="count-tag">{{ list.length }} 人在线</el-tag>
        <span class="hint">心跳超过 6 分钟无响应自动移出</span>
      </template>
      <template #toolbar>
        <el-button
          :type="autoRefresh ? 'success' : 'default'"
          size="small"
          @click="toggleAutoRefresh"
        >
          {{ autoRefresh ? '自动刷新中' : '自动刷新已停' }}
        </el-button>
      </template>
      <template #cell-lastSeen="{ row }">
        <span>{{ formatDate(row.lastSeen) }}</span>
        <el-tag size="small" type="success" style="margin-left: 6px">{{ lastSeenAgo(row.lastSeen) }}</el-tag>
      </template>
    </CommonDataTable>
  </div>
</template>

<style scoped>
.online-page {
  display: flex;
  flex-direction: column;
  height: 100%;
  overflow: hidden;
}
.title {
  font-size: 18px;
  font-weight: 600;
  color: var(--text-primary, #303133);
}
.count-tag {
  font-size: 13px;
}
.hint {
  font-size: 12px;
  color: #9ca3af;
}
</style>

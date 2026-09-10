<script setup lang="ts">
import { onMounted, onUnmounted, ref } from 'vue'
import { ChatDotRound } from '@element-plus/icons-vue'
import { listOnlineUsers, type OnlineUserDto } from '@/common/api/userOnline'
import { formatDate } from '@/common/formatDate'
import { useChatStore } from '@/common/stores/chat'
import CommonDataTable, { type DataTableColumn } from '@/common/components/CommonDataTable.vue'

const loading = ref(false)
const list = ref<OnlineUserDto[]>([])
const autoRefresh = ref(true)
let timer: ReturnType<typeof setInterval> | null = null

const previewAvatar = ref<string | null>(null)
const previewVisible = ref(false)

const chatStore = useChatStore()

/** 给该用户发消息：打开聊天弹窗并自动定向到与该用户的会话 */
function gotoChat(row: OnlineUserDto) {
  chatStore.openChat(row.userId)
}

const columns: DataTableColumn<OnlineUserDto>[] = [
  { prop: 'avatar', label: '头像', width: 80, align: 'center', custom: true },
  { prop: 'account', label: '账号', width: 140, sortable: true },
  { prop: 'displayName', label: '显示名称', minWidth: 120, formatter: (row) => row.displayName || '—', sortable: true },
  { prop: 'ip', label: 'IP 地址', width: 150, sortable: true },
  { prop: 'loginTime', label: '登录时间', width: 170, dateFormatter: formatDate, sortable: true },
  { prop: 'lastActive', label: '最后活跃', width: 260, custom: true, sortable: true },
  { prop: 'lastHeartbeat', label: '最后心跳', width: 170, dateFormatter: formatDate, sortable: true },
  { prop: 'actions', label: '操作', width: 110, align: 'center', custom: true },
]

function userInitials(row: OnlineUserDto): string {
  return (row.displayName || row.account || '?').slice(0, 1).toUpperCase()
}

function openAvatarPreview(avatar: string) {
  previewAvatar.value = avatar
  previewVisible.value = true
}

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
function lastActiveAgo(lastActive: string): string {
  const diff = Math.floor((Date.now() - new Date(lastActive).getTime()) / 1000)
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
        <span class="hint">登录即在线，退出登录即离线</span>
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
      <template #cell-avatar="{ row }">
        <div class="avatar-cell">
          <el-avatar
            v-if="row.avatar"
            :size="36"
            :src="row.avatar"
            class="user-avatar"
            @click="openAvatarPreview(row.avatar!)"
          />
          <el-avatar v-else :size="36" class="user-avatar fallback">{{ userInitials(row) }}</el-avatar>
        </div>
      </template>
      <template #cell-lastActive="{ row }">
        <span>{{ formatDate(row.lastActive) }}</span>
        <el-tag size="small" type="success" style="margin-left: 6px">{{ lastActiveAgo(row.lastActive) }}</el-tag>
      </template>
      <template #cell-actions="{ row }">
        <el-button text type="primary" size="small" :icon="ChatDotRound" @click="gotoChat(row)">发消息</el-button>
      </template>
    </CommonDataTable>

    <el-dialog v-model="previewVisible" title="头像预览" width="420px" :append-to-body="true" align-center>
      <div class="avatar-preview">
        <img v-if="previewAvatar" :src="previewAvatar" alt="头像预览" />
      </div>
    </el-dialog>
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

.avatar-cell {
  display: flex;
  align-items: center;
  justify-content: center;
}

.user-avatar {
  cursor: pointer;
  transition: transform 0.15s;
}

.user-avatar:hover {
  transform: scale(1.05);
}

.user-avatar.fallback {
  background: var(--el-color-primary-light-8);
  color: var(--el-color-primary);
  font-weight: 600;
}

.avatar-preview {
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 12px;
}

.avatar-preview img {
  max-width: 100%;
  max-height: 360px;
  border-radius: 8px;
}
</style>

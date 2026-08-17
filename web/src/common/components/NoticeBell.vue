<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { Bell } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import {
  getMyNotices,
  getNoticeUnreadCount,
  markNoticeRead,
  markAllNoticeRead,
  NOTICE_LEVELS,
  type NoticeUserDto,
} from '@/common/api/notice'

// 顶栏系统通知铃铛：展示未读数角标，弹层内查看通知内容并标记已读。
// 未读数每 60 秒轮询一次；弹层打开时拉取完整通知列表。

const unread = ref(0)
const notices = ref<NoticeUserDto[]>([])
const loading = ref(false)
/** 当前展开正文的通知 Id（手风琴式，一次只展开一条） */
const expandedId = ref<number | null>(null)

const unreadCount = computed(() => unread.value)

async function loadUnread() {
  try {
    const res = await getNoticeUnreadCount()
    unread.value = res.count
  } catch { /* 静默：轮询失败不打扰用户 */ }
}

async function loadList() {
  loading.value = true
  try {
    notices.value = await getMyNotices()
    unread.value = notices.value.filter((n) => !n.isRead).length
  } catch { /* 错误已由 request.ts 弹出提示 */ } finally {
    loading.value = false
  }
}

// 弹层打开/关闭钩子：不能用 v-model:visible 受控模式（会禁用内部 click 切换导致点击无反应），
// 改用 @show/@hide 事件：打开时拉列表，关闭时收起已展开条目。
async function onShow() {
  await loadList()
}
function onHide() {
  expandedId.value = null
}

/** 点击通知条目：展开正文并标记已读 */
async function onItemClick(item: NoticeUserDto) {
  expandedId.value = expandedId.value === item.id ? null : item.id
  if (!item.isRead) {
    try {
      await markNoticeRead(item.id)
      item.isRead = true
      unread.value = Math.max(0, unread.value - 1)
    } catch { /* 错误已由 request.ts 弹出提示 */ }
  }
}

async function onMarkAllRead() {
  try {
    await markAllNoticeRead()
    notices.value.forEach((n) => (n.isRead = true))
    unread.value = 0
    ElMessage.success('已全部标记为已读')
  } catch { /* 错误已由 request.ts 弹出提示 */ }
}

function formatTime(time: string): string {
  return time ? time.replace('T', ' ').slice(0, 16) : ''
}

// ===== 轮询：挂载时拉取未读数，之后每 60 秒刷新一次 =====
let timer: ReturnType<typeof setInterval> | null = null

// 登录后提醒窗（NoticeAlert）确认阅读后会派发 notice:read 事件，立即同步未读数与列表
function onNoticeRead() {
  void loadUnread()
  if (notices.value.length > 0) void loadList()
}

onMounted(() => {
  void loadUnread()
  timer = setInterval(loadUnread, 60_000)
  window.addEventListener('notice:read', onNoticeRead)
})
onBeforeUnmount(() => {
  if (timer) clearInterval(timer)
  window.removeEventListener('notice:read', onNoticeRead)
})
</script>

<template>
  <el-popover
    placement="bottom-end"
    :width="380"
    trigger="click"
    popper-class="notice-bell-popper"
    @show="onShow"
    @hide="onHide"
  >
    <template #reference>
      <el-badge :value="unreadCount" :hidden="unreadCount === 0" :max="99" class="notice-badge">
        <el-button :icon="Bell" circle size="small" title="系统通知" />
      </el-badge>
    </template>

    <div class="notice-popover">
      <div class="notice-popover-header">
        <span class="notice-popover-title">系统通知</span>
        <el-button v-if="unreadCount > 0" link type="primary" size="small" @click="onMarkAllRead">全部已读</el-button>
      </div>

      <div class="notice-popover-body" v-loading="loading">
        <div v-if="!loading && notices.length === 0" class="notice-empty">暂无通知</div>
        <div
          v-for="item in notices"
          :key="item.id"
          class="notice-item"
          :class="{ unread: !item.isRead }"
          @click="onItemClick(item)"
        >
          <div class="notice-item-head">
            <el-tag
              :type="NOTICE_LEVELS[item.level]?.type || 'info'"
              size="small"
              class="notice-level-tag"
            >
              {{ NOTICE_LEVELS[item.level]?.label || '普通' }}
            </el-tag>
            <span class="notice-item-title" :title="item.title">{{ item.title }}</span>
            <span class="notice-item-time">{{ formatTime(item.createTime) }}</span>
          </div>
          <div v-if="expandedId === item.id" class="notice-item-content">{{ item.content }}</div>
        </div>
      </div>
    </div>
  </el-popover>
</template>

<style scoped>
.notice-badge {
  line-height: 1;
}
.notice-popover {
  display: flex;
  flex-direction: column;
  max-height: 460px;
}
.notice-popover-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding-bottom: 8px;
  border-bottom: 1px solid var(--el-border-color-lighter);
}
.notice-popover-title {
  font-weight: 600;
  font-size: 14px;
}
.notice-popover-body {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  padding-top: 6px;
}
.notice-empty {
  padding: 32px 0;
  text-align: center;
  color: var(--el-text-color-secondary);
  font-size: 13px;
}
.notice-item {
  padding: 8px;
  border-radius: 6px;
  cursor: pointer;
}
.notice-item:hover {
  background: var(--el-fill-color-light);
}
.notice-item-head {
  display: flex;
  align-items: center;
  gap: 6px;
}
.notice-level-tag {
  flex-shrink: 0;
}
.notice-item-title {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-size: 13px;
}
.notice-item.unread .notice-item-title {
  font-weight: 600;
}
.notice-item.unread .notice-item-title::before {
  content: '';
  display: inline-block;
  width: 6px;
  height: 6px;
  margin-right: 5px;
  border-radius: 50%;
  background: var(--el-color-danger);
  vertical-align: middle;
}
.notice-item-time {
  flex-shrink: 0;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}
.notice-item-content {
  margin-top: 6px;
  padding: 8px;
  background: var(--el-fill-color-lighter);
  border-radius: 4px;
  font-size: 13px;
  line-height: 1.7;
  white-space: pre-wrap;
  word-break: break-all;
}
</style>

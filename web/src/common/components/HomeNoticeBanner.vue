<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { BellFilled } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import CommonDialog from '@/common/components/CommonDialog.vue'
import {
  getMyNotices,
  markNoticeRead,
  markAllNoticeRead,
  NOTICE_LEVELS,
  type NoticeUserDto,
} from '@/common/api/notice'

// 首页公告横幅：展示当前用户可见的未读公告（全级别），轮播滚动，
// 点击条目打开详情弹窗并自动标记已读（派发 notice:read 事件同步顶栏铃铛）。
// 顶栏铃铛/重要通知弹窗标记已读后也会通过 notice:read 事件刷新本横幅。
const notices = ref<NoticeUserDto[]>([])
const unread = computed(() => notices.value.filter((n) => !n.isRead))

async function load() {
  try {
    notices.value = await getMyNotices()
  } catch { /* 静默：公告加载失败不阻断首页 */ }
}

// ===== 详情弹窗（打开即标记已读） =====
const detailVisible = ref(false)
const detail = ref<NoticeUserDto | null>(null)

async function openDetail(item: NoticeUserDto) {
  detail.value = item
  detailVisible.value = true
  if (!item.isRead) {
    try {
      await markNoticeRead(item.id)
      item.isRead = true
      window.dispatchEvent(new CustomEvent('notice:read'))
    } catch { /* 静默 */ }
  }
}

/** 全部标记已读 */
async function markAll() {
  try {
    await markAllNoticeRead()
    notices.value.forEach((n) => (n.isRead = true))
    window.dispatchEvent(new CustomEvent('notice:read'))
    ElMessage.success('已全部标记为已读')
  } catch { /* 错误已由 request.ts 弹出提示 */ }
}

function formatTime(time: string): string {
  return time ? time.replace('T', ' ').slice(0, 16) : ''
}

function onNoticeRead() {
  void load()
}

onMounted(() => {
  void load()
  window.addEventListener('notice:read', onNoticeRead)
})
onBeforeUnmount(() => {
  window.removeEventListener('notice:read', onNoticeRead)
})
</script>

<template>
  <div v-if="unread.length > 0" class="notice-banner">
    <div class="notice-banner-head">
      <el-icon class="notice-banner-icon"><BellFilled /></el-icon>
      <span class="notice-banner-label">系统公告</span>
      <span class="notice-banner-count">{{ unread.length }} 条未读</span>
    </div>
    <el-carousel
      class="notice-banner-carousel"
      height="44px"
      direction="vertical"
      :autoplay="unread.length > 1"
      :interval="4000"
      indicator-position="none"
      arrow="never"
    >
      <el-carousel-item v-for="item in unread" :key="item.id">
        <div class="notice-banner-item" @click="openDetail(item)">
          <el-tag :type="NOTICE_LEVELS[item.level]?.type || 'info'" size="small" effect="dark" class="notice-banner-tag">
            {{ NOTICE_LEVELS[item.level]?.label || '普通' }}
          </el-tag>
          <span class="notice-banner-title">{{ item.title }}</span>
          <span class="notice-banner-content">{{ item.content }}</span>
          <span class="notice-banner-time">{{ formatTime(item.createTime) }}</span>
        </div>
      </el-carousel-item>
    </el-carousel>
    <el-button link type="primary" size="small" class="notice-banner-all" @click="markAll">全部已读</el-button>

    <!-- 公告详情弹窗 -->
    <CommonDialog
      v-model="detailVisible"
      :title="detail?.title ?? ''"
      width="520px"
      destroy-on-close
    >
      <div class="notice-detail-meta">
        <el-tag :type="NOTICE_LEVELS[detail?.level ?? 1]?.type || 'info'" size="small">
          {{ NOTICE_LEVELS[detail?.level ?? 1]?.label || '普通' }}
        </el-tag>
        <span>发布时间：{{ formatTime(detail?.createTime ?? '') }}</span>
      </div>
      <div class="notice-detail-content">{{ detail?.content }}</div>
      <template #footer>
        <el-button type="primary" @click="detailVisible = false">我知道了</el-button>
      </template>
    </CommonDialog>
  </div>
</template>

<style scoped>
.notice-banner {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 6px 16px;
  margin-bottom: 20px;
  border-radius: 10px;
  background: #fff7ed;
  border: 1px solid #fed7aa;
}
.notice-banner-head {
  display: flex;
  align-items: center;
  gap: 6px;
  flex-shrink: 0;
}
.notice-banner-icon {
  color: #ea580c;
  font-size: 16px;
}
.notice-banner-label {
  font-weight: 600;
  font-size: 13px;
  color: #9a3412;
}
.notice-banner-count {
  font-size: 12px;
  color: #c2410c;
}
.notice-banner-carousel {
  flex: 1;
  min-width: 0;
}
.notice-banner-item {
  display: flex;
  align-items: center;
  gap: 8px;
  height: 44px;
  cursor: pointer;
  min-width: 0;
}
.notice-banner-tag {
  flex-shrink: 0;
}
.notice-banner-title {
  flex-shrink: 0;
  max-width: 30%;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-weight: 600;
  font-size: 13px;
  color: var(--el-text-color-primary);
}
.notice-banner-content {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-size: 13px;
  color: var(--el-text-color-secondary);
}
.notice-banner-time {
  flex-shrink: 0;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}
.notice-banner-all {
  flex-shrink: 0;
}
.notice-detail-meta {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-bottom: 12px;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}
.notice-detail-content {
  padding: 12px 14px;
  border-radius: 8px;
  background: var(--el-fill-color-lighter);
  font-size: 13px;
  line-height: 1.8;
  white-space: pre-wrap;
  word-break: break-all;
  color: var(--el-text-color-regular);
  max-height: 50vh;
  overflow-y: auto;
}
</style>

<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'
import { Close } from '@element-plus/icons-vue'
import { getMyNotices, markNoticeRead, NOTICE_LEVELS, type NoticeUserDto } from '@/common/api/notice'
import { parseNoticeContent } from '@/common/utils/noticeLink'

// 重要/紧急通知提醒：登录后立即检查一次；新通知实时到达（SignalR NoticeCreated → chat store
// 派发 notice:created 事件）秒级弹卡片，轮询仅作 SignalR 断连降级时的兜底。
// 提醒为右侧依次弹出的小窗口（非模态、不打断操作）：
// 每条可单独“已读”或“稍后”关闭；关闭过的通知记录在 sessionStorage 中，
// 本次会话内不再重复弹出；标记已读后通过 notice:read 事件通知顶栏铃铛同步未读数。

/** 轮询间隔：SignalR 断连降级时接收新通知的最大延迟（实时模式下仅为兜底，极少触发弹卡） */
const POLL_INTERVAL = 10_000
/** 屏幕上同时最多展示的小窗口数量，超出排队等待 */
const MAX_VISIBLE = 4
/** 相邻小窗口弹出的间隔（依次弹出效果） */
const STAGGER_MS = 350
/** 自动消失倒计时：弹出后 10 秒自动关闭（用户可手动提前关闭，鼠标悬停时暂停） */
const AUTO_DISMISS_MS = 10_000

/** 已弹出展示中的通知（最多 MAX_VISIBLE 条） */
const active = ref<NoticeUserDto[]>([])
/** 排队等待弹出的通知 */
const pending = ref<NoticeUserDto[]>([])

let timer: ReturnType<typeof setInterval> | null = null
let staggerTimer: ReturnType<typeof setTimeout> | null = null
let reading = false // 标记已读请求进行中，避免并发重复提交
let checking = false // 拉取进行中，避免实时事件与轮询并发触发重复弹卡片（错过的由下次轮询补上）

/** 每条通知的自动消失定时器 */
const dismissTimers = new Map<number, ReturnType<typeof setTimeout>>()
/** 每条通知剩余倒计时（悬停时记录，用于恢复） */
const remainingMs = new Map<number, number>()
/** 每条通知上次启动倒计时的时间戳（用于计算剩余时间） */
const startedAt = new Map<number, number>()

/** 已关闭（未读）的通知 Id：本次会话内不再重复弹出 */
const DISMISS_KEY = 'notice_alert_dismissed_v1'
const dismissed = loadDismissed()

function loadDismissed(): Set<number> {
  try {
    return new Set<number>(JSON.parse(sessionStorage.getItem(DISMISS_KEY) || '[]'))
  } catch {
    return new Set<number>()
  }
}
function saveDismissed() {
  try {
    sessionStorage.setItem(DISMISS_KEY, JSON.stringify([...dismissed]))
  } catch { /* 忽略持久化失败 */ }
}

/** 把排队中的通知按固定间隔依次弹入展示区 */
function pump() {
  if (staggerTimer) return // 已有排队弹出在进行中
  const step = () => {
    if (active.value.length < MAX_VISIBLE && pending.value.length > 0) {
      const next = pending.value.shift()!
      active.value.push(next)
      startAutoDismiss(next)
      staggerTimer = setTimeout(step, STAGGER_MS)
    } else {
      staggerTimer = null
    }
  }
  step()
}

/** 启动单条通知的自动消失倒计时 */
function startAutoDismiss(item: NoticeUserDto) {
  clearTimeout(dismissTimers.get(item.id))
  const ms = remainingMs.get(item.id) ?? AUTO_DISMISS_MS
  remainingMs.delete(item.id)
  startedAt.set(item.id, Date.now())
  dismissTimers.set(
    item.id,
    setTimeout(() => {
      dismissTimers.delete(item.id)
      startedAt.delete(item.id)
      // 自动消失也记入 dismissed，本次会话内不再重复弹出
      dismissed.add(item.id)
      saveDismissed()
      removeActive(item)
    }, ms),
  )
}

/** 暂停单条通知的自动消失倒计时（鼠标悬停时调用） */
function pauseAutoDismiss(item: NoticeUserDto) {
  clearTimeout(dismissTimers.get(item.id))
  dismissTimers.delete(item.id)
  const start = startedAt.get(item.id) ?? Date.now()
  const remain = AUTO_DISMISS_MS - (Date.now() - start)
  remainingMs.set(item.id, Math.max(remain, 500))
  startedAt.delete(item.id)
}

/** 恢复单条通知的自动消失倒计时（鼠标离开时调用） */
function resumeAutoDismiss(item: NoticeUserDto) {
  startAutoDismiss(item)
}

/** 清除单条通知的自动消失定时器（手动关闭时调用） */
function clearAutoDismiss(id: number) {
  clearTimeout(dismissTimers.get(id))
  dismissTimers.delete(id)
  startedAt.delete(id)
  remainingMs.delete(id)
}

/** 检查是否存在未读的重要/紧急通知，有则加入弹出队列（幂等：已展示/已关闭/已读的均过滤） */
async function checkUrgent() {
  if (checking) return
  checking = true
  try {
    const list = await getMyNotices()
    const known = new Set<number>([...active.value, ...pending.value].map((n) => n.id))
    const urgent = list.filter((n) => !n.isRead && n.level >= 2 && !dismissed.has(n.id) && !known.has(n.id))
    if (urgent.length > 0) {
      pending.value.push(...urgent)
      pump()
    }
  } catch { /* 静默：提醒失败不阻断正常使用 */ } finally {
    checking = false
  }
}

// 新系统通知实时到达：立即检查弹卡片（秒级，不等轮询）
function onNoticeCreated() {
  void checkUrgent()
}

onMounted(() => {
  void checkUrgent()
  timer = setInterval(checkUrgent, POLL_INTERVAL)
  window.addEventListener('notice:created', onNoticeCreated)
})
onBeforeUnmount(() => {
  if (timer) clearInterval(timer)
  if (staggerTimer) clearTimeout(staggerTimer)
  dismissTimers.forEach((t) => clearTimeout(t))
  dismissTimers.clear()
  window.removeEventListener('notice:created', onNoticeCreated)
})

/** 确认已读：标记单条已读、移出展示区并通知铃铛刷新 */
async function acknowledge(item: NoticeUserDto) {
  if (reading) return
  reading = true
  try {
    await markNoticeRead(item.id)
    window.dispatchEvent(new CustomEvent('notice:read'))
  } catch { /* 标记失败也移除窗口，避免卡住队列 */ }
  finally {
    reading = false
    clearAutoDismiss(item.id)
    removeActive(item)
  }
}

/** 稍后处理：不标记已读直接关闭（本次会话内不再弹出同一条） */
function dismiss(item: NoticeUserDto) {
  dismissed.add(item.id)
  saveDismissed()
  clearAutoDismiss(item.id)
  removeActive(item)
}

/** 从展示区移除一条并补位弹出排队中的下一条 */
function removeActive(item: NoticeUserDto) {
  clearAutoDismiss(item.id)
  const idx = active.value.findIndex((n) => n.id === item.id)
  if (idx !== -1) active.value.splice(idx, 1)
  pump()
}

function formatTime(time: string): string {
  return time ? time.replace('T', ' ').slice(0, 16) : ''
}
</script>

<template>
  <!-- 右侧通知小窗口堆叠区：依次滑入，互不遮挡操作 -->
  <div class="notice-toast-stack">
    <TransitionGroup name="notice-toast">
      <div
        v-for="item in active"
        :key="item.id"
        class="notice-toast-card"
        @mouseenter="pauseAutoDismiss(item)"
        @mouseleave="resumeAutoDismiss(item)"
      >
        <div class="notice-toast-head">
          <el-tag :type="NOTICE_LEVELS[item.level]?.type || 'warning'" size="small" effect="dark" round>
            {{ NOTICE_LEVELS[item.level]?.label || '重要' }}
          </el-tag>
          <span class="notice-toast-title" :title="item.title">{{ item.title }}</span>
          <el-icon class="notice-toast-close" @click="dismiss(item)"><Close /></el-icon>
        </div>
        <!-- 正文分段渲染：含 /api/... 或 http(s) 链接时渲染为可点击下载（同窗导航到附件响应，页面不跳转） -->
        <div class="notice-toast-content">
          <template v-for="(seg, i) in parseNoticeContent(item.content)" :key="i">
            <a v-if="seg.url" :href="seg.url" class="notice-link">点击下载</a>
            <template v-else>{{ seg.text }}</template>
          </template>
        </div>
        <div class="notice-toast-foot">
          <span class="notice-toast-time">{{ formatTime(item.createTime) }}</span>
          <el-button size="small" text @click="dismiss(item)">稍后</el-button>
          <el-button size="small" type="primary" @click="acknowledge(item)">已读</el-button>
        </div>
        <div class="notice-toast-progress" />
      </div>
    </TransitionGroup>
  </div>
</template>

<style scoped>
.notice-toast-stack {
  position: fixed;
  top: 70px;
  right: 16px;
  z-index: 2100;
  display: flex;
  flex-direction: column;
  gap: 10px;
  width: 330px;
  pointer-events: none;
}
.notice-toast-card {
  position: relative;
  overflow: hidden;
  pointer-events: auto;
  background: #fff;
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 10px;
  padding: 12px 14px 16px;
  box-shadow: 0 6px 20px rgba(0, 0, 0, 0.12);
}
.notice-toast-head {
  display: flex;
  align-items: center;
  gap: 8px;
}
.notice-toast-title {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-weight: 600;
  font-size: 14px;
  color: var(--el-text-color-primary);
}
.notice-toast-close {
  flex-shrink: 0;
  font-size: 14px;
  color: var(--el-text-color-placeholder);
  cursor: pointer;
}
.notice-toast-close:hover {
  color: var(--el-text-color-secondary);
}
.notice-toast-content {
  margin-top: 8px;
  font-size: 13px;
  line-height: 1.7;
  white-space: pre-wrap;
  word-break: break-all;
  color: var(--el-text-color-regular);
  max-height: 96px;
  overflow-y: auto;
}
/* 正文内下载链接：蓝色可点击，无需下划线与文字装饰 */
.notice-link {
  color: var(--el-color-primary);
  font-weight: 600;
  text-decoration: none;
}
.notice-link:hover {
  opacity: 0.85;
}
.notice-toast-foot {
  display: flex;
  align-items: center;
  gap: 6px;
  margin-top: 8px;
}
.notice-toast-time {
  flex: 1;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

/* 依次从右侧滑入 / 淡出收起 */
.notice-toast-enter-from {
  opacity: 0;
  transform: translateX(110%);
}
.notice-toast-enter-active {
  transition: all 0.35s ease-out;
}
.notice-toast-leave-active {
  transition: all 0.25s ease-in;
}
.notice-toast-leave-to {
  opacity: 0;
  transform: translateX(110%);
}
.notice-toast-move {
  transition: transform 0.25s ease;
}

/* 自动消失倒计时进度条：10 秒内从 100% 收缩到 0%（与 JS AUTO_DISMISS_MS 一致） */
.notice-toast-progress {
  position: absolute;
  bottom: 0;
  left: 0;
  width: 100%;
  height: 3px;
  background: var(--el-color-primary-light-5);
  transform-origin: left;
  animation: notice-progress-shrink 10s linear forwards;
}
@keyframes notice-progress-shrink {
  from { transform: scaleX(1); }
  to { transform: scaleX(0); }
}

/* 鼠标悬停时暂停进度条动画（与 JS 暂停逻辑同步） */
.notice-toast-card:hover .notice-toast-progress {
  animation-play-state: paused;
}
</style>

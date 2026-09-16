<script setup lang="ts">
import { onBeforeUnmount, onMounted } from 'vue'
import { ChatDotRound } from '@element-plus/icons-vue'
import { useChatStore } from '@/common/stores/chat'
import { getChatUnreadTotal, parseUserIdFromToken } from '@/common/api/chat'
import { useAuthStore } from '@/common/stores/auth'

// 顶栏聊天入口：未读数角标 + 点击打开聊天弹窗。
// 未读数优先走 store 本地计数（SignalR 推送即时维护），每 60 秒轮询校准一次，
// 可用性不依赖实时连接（桌面壳远程模式降级轮询时角标依然准确）。
// 兜底账号（数据库不可用时登录）userId 为空 Guid，跳过轮询避免 400 错误。

const chatStore = useChatStore()
const authStore = useAuthStore()

/** 是否为兜底账号（userId 为空 Guid），兜底账号不启动轮询也不显示聊天入口 */
const isFallback = (() => {
  const userId = parseUserIdFromToken(authStore.token)
  return !userId || userId === '00000000-0000-0000-0000-000000000000'
})()

async function loadUnread() {
  try {
    const res = await getChatUnreadTotal()
    chatStore.unreadTotal = res.count
  } catch { /* 静默：轮询失败不打扰用户 */ }
}

let timer: ReturnType<typeof setInterval> | null = null

onMounted(() => {
  if (isFallback) return
  void loadUnread()
  timer = setInterval(loadUnread, 60_000)
})
onBeforeUnmount(() => {
  if (timer) clearInterval(timer)
})
</script>

<template>
  <el-badge v-if="!isFallback" :value="chatStore.unreadTotal" :hidden="chatStore.unreadTotal === 0" :max="99" class="chat-badge">
    <el-button :icon="ChatDotRound" circle size="small" title="即时聊天" :class="{ blink: chatStore.unreadTotal > 0 }" @click="chatStore.openChat()" />
  </el-badge>
</template>

<style scoped>
.chat-badge {
  line-height: 1;
}
.blink {
  animation: bell-blink 1.6s ease-in-out infinite;
}
@keyframes bell-blink {
  0%, 100% { opacity: 1; }
  50% { opacity: 0.25; }
}
</style>

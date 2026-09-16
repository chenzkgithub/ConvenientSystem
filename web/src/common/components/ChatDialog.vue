<script setup lang="ts">
import { computed } from 'vue'
import { useChatStore } from '@/common/stores/chat'
import ChatView from '@/common/views/ChatView.vue'

// 聊天弹窗薄壳：el-dialog 包裹 ChatView。
// 显隐由 chatStore.dialogVisible 控制；关闭时走 store.closeChat() 清空状态。
// destroy-on-close：关闭时销毁 ChatView 组件，下次打开重建，避免残留消息与会话状态。
// 高度钉死/弹窗体禁滚样式在 styles/main.css 的 .el-dialog.chat-dialog 全局块：
// el-dialog 的 class 与 data-cs-fixed 会落到 .el-dialog 根元素，但 scoped 的 data-v
// 过不了 EP 内部 Teleport→Overlay 组件链（组件内 scoped 对 .el-dialog 是死代码）。

const chatStore = useChatStore()

const visible = computed({
  get: () => chatStore.dialogVisible,
  set: (v) => { if (!v) chatStore.closeChat() },
})
</script>

<template>
  <el-dialog
    v-model="visible"
    width="94%"
    :destroy-on-close="true"
    :close-on-click-modal="true"
    :close-on-press-escape="true"
    title="即时聊天"
    class="chat-dialog"
    data-cs-fixed
  >
    <ChatView />
  </el-dialog>
</template>

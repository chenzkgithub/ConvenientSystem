<script setup lang="ts">
import { computed } from 'vue'
import { useChatStore } from '@/common/stores/chat'
import ChatView from '@/common/views/ChatView.vue'

// 聊天弹窗薄壳：el-dialog 包裹 ChatView。
// 显隐由 chatStore.dialogVisible 控制；关闭时走 store.closeChat() 清空状态。
// destroy-on-close：关闭时销毁 ChatView 组件，下次打开重建，避免残留消息与会话状态。

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
    top="2vh"
    :destroy-on-close="true"
    :close-on-click-modal="true"
    :close-on-press-escape="true"
    title="即时聊天"
    class="chat-dialog"
  >
    <ChatView />
  </el-dialog>
</template>

<style scoped>
/* class="chat-dialog" 由 el-dialog 直接落在对话框根元素自身（$attrs 继承），
   scoped 下直接命中，不能写 .chat-dialog :deep(.el-dialog)——那是后代选择器永远匹配不到自身。 */
.chat-dialog {
  display: flex;
  flex-direction: column;
  /* 固定总高（不用 max-height：auto 高度的 flex 容器里 basis:0 的 body 不贡献高度会塌缩）。
   2vh 顶部 + 96vh 身 = 98vh，不超视口；覆盖默认 margin-bottom:50px 否则必出滚动条。 */
  height: 96vh;
  max-height: 96vh;
  margin-bottom: 0;
}
/* body 是对话框的后代，:deep 写法正确：弹性分得剩余高度（96vh - header），
   ChatView 根节点 height:100% 自适应，内部（会话列表/消息区）各自滚动，整体不出滚动条 */
.chat-dialog :deep(.el-dialog__body) {
  padding: 0;
  flex: 1;
  min-height: 0;
  overflow: hidden;
}
</style>

<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, ref, watch } from 'vue'
import { ElMessage } from 'element-plus'
import { Bell, ChatDotRound, Delete, Mute, Promotion, Search } from '@element-plus/icons-vue'
import { useAuthStore } from '@/common/stores/auth'
import { useChatStore } from '@/common/stores/chat'
import { blockChatUser, hideChatConversation, setChatMuted, unblockChatUser } from '@/common/api/chat'
import type { ChatContactDto, ChatMessageDto, ChatOpenDto } from '@/common/api/chat'
import { confirmAndRun } from '@/common/utils/confirm'

const authStore = useAuthStore()
const chatStore = useChatStore()

// ==================== 本地状态 ====================

const leftTab = ref<'conversations' | 'contacts'>('conversations')
const keyword = ref('')
const contacts = ref<ChatContactDto[]>([])
/** 当前打开会话的对方信息（Open 接口返回的本地副本，屏蔽操作后就地更新） */
const activePeer = ref<ChatOpenDto | null>(null)
const inputText = ref('')
const sending = ref(false)
const loadingEarlier = ref(false)
const hasMore = ref(true)
const scrollBox = ref<HTMLElement | null>(null)
/** 向上翻页期间抑制"新消息滚到底部"监听，保持视口停在原位置 */
let suppressScrollWatch = false

// ==================== 计算属性 ====================

/** 当前会话在列表中的对应项（免打扰标记等就地读取） */
const activeConv = computed(() =>
  chatStore.conversations.find(c => c.conversationId === chatStore.activeConversationId))

const activeMuted = computed(() => activeConv.value?.muted ?? false)

/** 对方是否在线（在线集合增量维护，连接未建立时为空 → 显示离线，可接受） */
const peerOnline = computed(() =>
  !!activePeer.value && chatStore.onlineIds.has(activePeer.value.peerId))

const filteredConversations = computed(() => {
  const kw = keyword.value.trim().toLowerCase()
  if (!kw) return chatStore.conversations
  return chatStore.conversations.filter(c =>
    (c.peerDisplayName || c.peerAccount).toLowerCase().includes(kw))
})

/** 通讯录：在线在前，其次按名称排序；在线状态 = 拉取快照 ∪ 增量在线集合 */
const filteredContacts = computed(() => {
  const kw = keyword.value.trim().toLowerCase()
  const list = kw
    ? contacts.value.filter(c =>
        (c.displayName || c.account).toLowerCase().includes(kw) || c.account.toLowerCase().includes(kw))
    : contacts.value
  return [...list].sort((a, b) =>
    Number(isContactOnline(b)) - Number(isContactOnline(a)) ||
    (a.displayName || a.account).localeCompare(b.displayName || b.account))
})

const inputDisabled = computed(() =>
  !!activePeer.value && (activePeer.value.blockedByMe || activePeer.value.blockedMe))

const inputPlaceholder = computed(() => {
  if (!activePeer.value) return ''
  if (activePeer.value.blockedByMe) return '你已屏蔽对方，无法发送消息'
  if (activePeer.value.blockedMe) return '对方暂无法接收你的消息'
  return '输入消息，Enter 发送，Shift+Enter 换行'
})

// ==================== 工具函数 ====================

function peerName(p: { peerDisplayName?: string | null; peerAccount: string }): string {
  return p.peerDisplayName || p.peerAccount
}

function initials(name: string): string {
  return (name || '?').slice(0, 1).toUpperCase()
}

function isMine(m: ChatMessageDto): boolean {
  return !!chatStore.myUserId && m.senderId === chatStore.myUserId
}

function isContactOnline(c: ChatContactDto): boolean {
  return c.online || chatStore.onlineIds.has(c.userId)
}

/** 短时间显示：今天只显示时分，昨天显示"昨天 HH:mm"，更早显示"M-D HH:mm" */
function shortTime(value?: string | null): string {
  if (!value) return ''
  const d = new Date(value)
  if (isNaN(d.getTime())) return ''
  const hm = `${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`
  const now = new Date()
  if (d.toDateString() === now.toDateString()) return hm
  const yesterday = new Date(now)
  yesterday.setDate(now.getDate() - 1)
  if (d.toDateString() === yesterday.toDateString()) return `昨天 ${hm}`
  return `${d.getMonth() + 1}-${d.getDate()} ${hm}`
}

/** 我方最后一条消息且对方已读到它 → 显示"已读" */
function showReadAck(m: ChatMessageDto): boolean {
  if (!isMine(m) || !chatStore.peerReadMessageId) return false
  if (m.id > chatStore.peerReadMessageId) return false
  const lastMine = [...chatStore.messages].reverse().find(x => isMine(x))
  return lastMine?.id === m.id
}

// ==================== 动作 ====================

async function loadContacts() {
  try {
    contacts.value = await chatStore.refreshContacts()
  } catch {
    /* 错误已由 request.ts 弹出提示 */
  }
}

async function switchToContacts() {
  leftTab.value = 'contacts'
  if (!contacts.value.length) await loadContacts()
}

async function openByPeerId(peerId: string) {
  try {
    const dto = await chatStore.openConversation(peerId)
    activePeer.value = dto
    hasMore.value = true
    leftTab.value = 'conversations'
    await nextTick()
    const box = scrollBox.value
    if (box) box.scrollTop = box.scrollHeight
  } catch {
    /* 错误已由 request.ts 弹出提示 */
  }
}

async function loadEarlier() {
  if (loadingEarlier.value || !chatStore.activeConversationId) return
  const box = scrollBox.value
  const prevHeight = box?.scrollHeight ?? 0
  loadingEarlier.value = true
  suppressScrollWatch = true
  try {
    hasMore.value = await chatStore.loadEarlierMessages()
    await nextTick()
    if (box) box.scrollTop = box.scrollHeight - prevHeight
  } finally {
    loadingEarlier.value = false
    suppressScrollWatch = false
  }
}

async function doSend() {
  const content = inputText.value.trim()
  const peer = activePeer.value
  if (!content || !peer || sending.value || peer.blockedByMe || peer.blockedMe) return
  sending.value = true
  try {
    await chatStore.sendMessage(peer.peerId, content)
    inputText.value = ''
  } catch {
    /* 屏蔽等原因已弹出提示；保留输入内容便于处理后再发 */
  } finally {
    sending.value = false
  }
}

async function toggleMuted() {
  const conv = activeConv.value
  if (!conv) return
  const target = !conv.muted
  try {
    await setChatMuted(conv.conversationId, target)
    conv.muted = target
    ElMessage.success(target ? '已开启免打扰，新消息不再提醒' : '已取消免打扰')
  } catch {
    /* 错误已弹出提示 */
  }
}

async function toggleBlock() {
  const peer = activePeer.value
  if (!peer) return
  const target = !peer.blockedByMe
  const ok = await confirmAndRun(
    target
      ? `屏蔽后对方将无法给你发消息，你发消息也会被拒收。确定屏蔽「${peerName(peer)}」？`
      : `确定取消屏蔽「${peerName(peer)}」？双方即可恢复正常收发消息。`,
    () => (target ? blockChatUser(peer.peerId) : unblockChatUser(peer.peerId)),
    { successText: target ? '已屏蔽' : '已取消屏蔽' },
  )
  if (ok) peer.blockedByMe = target
}

async function deleteConversation() {
  const conv = activeConv.value
  if (!conv) return
  const ok = await confirmAndRun(
    `删除后「${peerName(conv)}」将从你的会话列表移除，对方发来新消息时会自动恢复。`,
    () => hideChatConversation(conv.conversationId),
    { successText: '已删除' },
  )
  if (!ok) return
  chatStore.conversations = chatStore.conversations.filter(c => c.conversationId !== conv.conversationId)
  // 删除的是当前打开的会话：关闭右侧
  if (chatStore.activeConversationId === conv.conversationId) {
    chatStore.closeConversation()
    activePeer.value = null
  }
}

// ==================== 生命周期与监听 ====================

/** 新消息到达（列表长度变化）→ 滚到底部 */
watch(() => chatStore.messages.length, async () => {
  if (suppressScrollWatch) return
  await nextTick()
  const box = scrollBox.value
  if (box) box.scrollTop = box.scrollHeight
})

/** 弹窗打开时指定的初始 peerId 变化 → 定向打开会话 */
watch(() => chatStore.dialogPeerId, (v) => {
  if (typeof v === 'string' && v) void openByPeerId(v)
})

onMounted(() => {
  void chatStore.start()
  void chatStore.refreshConversations()
  // 弹窗打开时若指定了初始 peerId，自动定向
  if (chatStore.dialogPeerId) void openByPeerId(chatStore.dialogPeerId)
})

onUnmounted(() => {
  chatStore.closeConversation()
  activePeer.value = null
})
</script>

<template>
  <div class="chat-page">
    <!-- ==================== 左栏：会话 / 通讯录 ==================== -->
    <aside class="chat-sidebar">
      <div class="sidebar-tabs">
        <div class="tab" :class="{ active: leftTab === 'conversations' }" @click="leftTab = 'conversations'">
          会话
          <span v-if="chatStore.unreadTotal" class="tab-badge">{{ chatStore.unreadTotal > 99 ? '99+' : chatStore.unreadTotal }}</span>
        </div>
        <div class="tab" :class="{ active: leftTab === 'contacts' }" @click="switchToContacts">通讯录</div>
      </div>

      <div class="sidebar-search">
        <el-input v-model="keyword" :prefix-icon="Search" placeholder="搜索名称 / 账号" clearable />
      </div>

      <div class="sidebar-list">
        <!-- 会话列表 -->
        <template v-if="leftTab === 'conversations'">
          <div v-if="!filteredConversations.length" class="list-empty">
            {{ keyword ? '没有匹配的会话' : '暂无会话，去通讯录发起聊天' }}
          </div>
          <div
            v-for="c in filteredConversations"
            :key="c.conversationId"
            class="conv-item"
            :class="{ active: c.conversationId === chatStore.activeConversationId }"
            @click="openByPeerId(c.peerId)"
          >
            <div class="avatar-wrap">
              <el-avatar v-if="c.peerAvatar" :size="40" :src="c.peerAvatar" />
              <el-avatar v-else :size="40" class="avatar fallback">{{ initials(peerName(c)) }}</el-avatar>
              <span class="online-dot" :class="{ on: chatStore.onlineIds.has(c.peerId) }"></span>
            </div>
            <div class="item-main">
              <div class="item-row">
                <span class="name">{{ peerName(c) }}</span>
                <span class="time">{{ shortTime(c.lastMessageTime) }}</span>
              </div>
              <div class="item-row">
                <span class="preview">
                  <el-icon v-if="c.muted" class="mute-icon" :size="12"><Mute /></el-icon>
                  {{ (c.lastFromMe ? '我：' : '') + (c.lastMessageText || '暂无消息') }}
                </span>
                <span v-if="c.unreadCount" class="unread-badge" :class="{ muted: c.muted }">
                  {{ c.unreadCount > 99 ? '99+' : c.unreadCount }}
                </span>
              </div>
            </div>
          </div>
        </template>

        <!-- 通讯录 -->
        <template v-else>
          <div v-if="!filteredContacts.length" class="list-empty">
            {{ keyword ? '没有匹配的联系人' : '通讯录为空' }}
          </div>
          <div
            v-for="c in filteredContacts"
            :key="c.userId"
            class="contact-item"
            @click="openByPeerId(c.userId)"
          >
            <div class="avatar-wrap">
              <el-avatar v-if="c.avatar" :size="36" :src="c.avatar" />
              <el-avatar v-else :size="36" class="avatar fallback">{{ initials(c.displayName || c.account) }}</el-avatar>
              <span class="online-dot" :class="{ on: isContactOnline(c) }"></span>
            </div>
            <div class="item-main">
              <div class="item-row">
                <span class="name">{{ c.displayName || c.account }}</span>
                <el-tag v-if="c.blockedByMe" size="small" type="info">已屏蔽</el-tag>
              </div>
              <div class="item-row">
                <span class="account">{{ c.account }}</span>
                <span class="online-text" :class="{ on: isContactOnline(c) }">{{ isContactOnline(c) ? '在线' : '离线' }}</span>
              </div>
            </div>
          </div>
        </template>
      </div>
    </aside>

    <!-- ==================== 右栏：消息区 ==================== -->
    <section class="chat-main">
      <template v-if="activePeer">
        <!-- 会话头 -->
        <header class="chat-header">
          <div class="peer-info">
            <el-avatar v-if="activePeer.peerAvatar" :size="40" :src="activePeer.peerAvatar" />
            <el-avatar v-else :size="40" class="avatar fallback">{{ initials(peerName(activePeer)) }}</el-avatar>
            <div class="peer-text">
              <div class="name">{{ peerName(activePeer) }}</div>
              <div class="online-text" :class="{ on: peerOnline }">{{ peerOnline ? '在线' : '离线' }}</div>
            </div>
          </div>
          <div class="header-actions">
            <el-button text :type="activeMuted ? 'warning' : 'default'" @click="toggleMuted">
              <el-icon><component :is="activeMuted ? Mute : Bell" /></el-icon>
              <span class="action-text">{{ activeMuted ? '取消免打扰' : '免打扰' }}</span>
            </el-button>
            <el-button text :type="activePeer.blockedByMe ? 'danger' : 'default'" @click="toggleBlock">
              {{ activePeer.blockedByMe ? '取消屏蔽' : '屏蔽' }}
            </el-button>
            <el-button text type="danger" @click="deleteConversation">
              <el-icon><Delete /></el-icon>
              <span class="action-text">删除</span>
            </el-button>
          </div>
        </header>

        <!-- 轮询降级提示：实时连接暂不可用（断网/服务重启/代理故障），恢复后自动切回秒级推送 -->
        <div v-if="chatStore.polling" class="polling-tip">
          实时连接暂不可用，消息约每 5 秒自动刷新，恢复后将自动切回实时推送
        </div>

        <!-- 屏蔽提示 -->
        <div v-if="activePeer.blockedByMe" class="block-tip warn">
          你已屏蔽对方：对方发消息会被拒收，你也无法发送消息。
          <el-button text size="small" type="primary" @click="toggleBlock">取消屏蔽</el-button>
        </div>
        <div v-else-if="activePeer.blockedMe" class="block-tip danger">对方暂无法接收你的消息</div>

        <!-- 消息气泡区 -->
        <div ref="scrollBox" class="msg-scroll">
          <div class="load-earlier">
            <el-button v-if="hasMore" text size="small" :loading="loadingEarlier" @click="loadEarlier">
              {{ loadingEarlier ? '加载中…' : '加载更早消息' }}
            </el-button>
            <span v-else-if="chatStore.messages.length" class="no-more">没有更多消息了</span>
          </div>

          <div
            v-for="m in chatStore.messages"
            :key="m.id"
            class="msg-row"
            :class="{ mine: isMine(m) }"
          >
            <el-avatar v-if="isMine(m) && authStore.avatar" :size="34" :src="authStore.avatar" />
            <el-avatar v-else-if="!isMine(m) && activePeer.peerAvatar" :size="34" :src="activePeer.peerAvatar" />
            <el-avatar v-else :size="34" class="avatar fallback small">
              {{ initials(isMine(m) ? (authStore.displayName || authStore.currentAccount) : peerName(activePeer)) }}
            </el-avatar>
            <div class="bubble-wrap">
              <div class="bubble">{{ m.content }}</div>
              <div class="meta">
                <span>{{ shortTime(m.createTime) }}</span>
                <span v-if="showReadAck(m)" class="read-ack">已读</span>
              </div>
            </div>
          </div>

          <div v-if="!chatStore.messages.length" class="no-messages">开始你们的对话吧</div>
        </div>

        <!-- 输入区 -->
        <footer class="chat-input">
          <textarea
            v-model="inputText"
            class="input-area"
            rows="3"
            :disabled="inputDisabled"
            :placeholder="inputPlaceholder"
            @keydown.enter.exact.prevent="doSend"
          ></textarea>
          <div class="input-actions">
            <span class="input-hint">Enter 发送 / Shift+Enter 换行</span>
            <el-button type="primary" :icon="Promotion" :loading="sending" :disabled="!inputText.trim() || inputDisabled" @click="doSend">
              发送
            </el-button>
          </div>
        </footer>
      </template>

      <!-- 空态 -->
      <div v-else class="chat-empty">
        <el-icon :size="56" class="empty-icon"><ChatDotRound /></el-icon>
        <p>选择左侧会话，或从通讯录发起聊天</p>
      </div>
    </section>
  </div>
</template>

<style scoped>
.chat-page {
  display: flex;
  height: 100%;
  overflow: hidden;
  background: var(--el-bg-color);
}

/* ==================== 左栏 ==================== */
.chat-sidebar {
  display: flex;
  flex-direction: column;
  width: 280px;
  min-width: 280px;
  border-right: 1px solid var(--el-border-color-light);
  overflow: hidden;
}

.sidebar-tabs {
  display: flex;
  border-bottom: 1px solid var(--el-border-color-lighter);
}

.sidebar-tabs .tab {
  flex: 1;
  padding: 12px 0;
  text-align: center;
  font-size: 14px;
  color: var(--el-text-color-regular);
  cursor: pointer;
  user-select: none;
  transition: color 0.15s, background-color 0.15s;
}

.sidebar-tabs .tab:hover {
  background: var(--el-fill-color-light);
}

.sidebar-tabs .tab.active {
  color: var(--el-color-primary);
  font-weight: 600;
  box-shadow: inset 0 -2px 0 var(--el-color-primary);
}

.tab-badge {
  display: inline-block;
  min-width: 18px;
  margin-left: 4px;
  padding: 0 5px;
  border-radius: 9px;
  background: var(--el-color-danger);
  color: #fff;
  font-size: 12px;
  font-weight: 600;
  line-height: 18px;
}

.sidebar-search {
  padding: 10px 12px;
}

.sidebar-list {
  flex: 1;
  overflow-y: auto;
}

.list-empty {
  padding: 40px 16px;
  text-align: center;
  font-size: 13px;
  color: var(--el-text-color-secondary);
}

/* 会话/联系人列表项共用 */
.conv-item,
.contact-item {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 10px 12px;
  cursor: pointer;
  transition: background-color 0.15s;
}

.conv-item:hover,
.contact-item:hover {
  background: var(--el-fill-color-light);
}

.conv-item.active {
  background: var(--el-color-primary-light-9);
}

.avatar-wrap {
  position: relative;
  flex-shrink: 0;
}

.avatar.fallback {
  background: var(--el-color-primary-light-8);
  color: var(--el-color-primary);
  font-weight: 600;
}

.avatar.fallback.small {
  font-size: 13px;
}

.online-dot {
  position: absolute;
  right: -1px;
  bottom: -1px;
  width: 10px;
  height: 10px;
  border-radius: 50%;
  background: var(--el-text-color-disabled);
  border: 2px solid var(--el-bg-color);
}

.online-dot.on {
  background: var(--el-color-success);
}

.item-main {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.item-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  min-width: 0;
}

.item-row .name {
  font-size: 14px;
  font-weight: 500;
  color: var(--el-text-color-primary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.item-row .time {
  flex-shrink: 0;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.preview {
  display: flex;
  align-items: center;
  gap: 4px;
  min-width: 0;
  font-size: 12px;
  color: var(--el-text-color-secondary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.mute-icon {
  flex-shrink: 0;
  color: var(--el-text-color-placeholder);
}

.unread-badge {
  flex-shrink: 0;
  min-width: 18px;
  padding: 0 5px;
  border-radius: 9px;
  background: var(--el-color-danger);
  color: #fff;
  font-size: 12px;
  font-weight: 600;
  line-height: 18px;
  text-align: center;
}

.unread-badge.muted {
  background: var(--el-text-color-disabled);
}

.item-row .account {
  font-size: 12px;
  color: var(--el-text-color-secondary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.online-text {
  flex-shrink: 0;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.online-text.on {
  color: var(--el-color-success);
}

/* ==================== 右栏 ==================== */
.chat-main {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.chat-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 10px 16px;
  border-bottom: 1px solid var(--el-border-color-light);
}

.peer-info {
  display: flex;
  align-items: center;
  gap: 10px;
  min-width: 0;
}

.peer-text .name {
  font-size: 15px;
  font-weight: 600;
  color: var(--el-text-color-primary);
}

.header-actions {
  display: flex;
  align-items: center;
  gap: 4px;
  flex-shrink: 0;
}

.action-text {
  margin-left: 4px;
}

.polling-tip {
  padding: 6px 16px;
  font-size: 12px;
  color: var(--el-color-warning);
  background: var(--el-color-warning-light-9);
  border-bottom: 1px solid var(--el-color-warning-light-7);
}

.block-tip {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 6px 16px;
  font-size: 12px;
}

.block-tip.warn {
  color: var(--el-color-warning);
  background: var(--el-color-warning-light-9);
  border-bottom: 1px solid var(--el-color-warning-light-7);
}

.block-tip.danger {
  color: var(--el-color-danger);
  background: var(--el-color-danger-light-9);
  border-bottom: 1px solid var(--el-color-danger-light-7);
}

/* ==================== 消息区 ==================== */
.msg-scroll {
  flex: 1;
  overflow-y: auto;
  padding: 16px;
}

.load-earlier {
  text-align: center;
  margin-bottom: 12px;
}

.no-more {
  font-size: 12px;
  color: var(--el-text-color-placeholder);
}

.no-messages {
  text-align: center;
  padding: 40px 0;
  font-size: 13px;
  color: var(--el-text-color-secondary);
}

.msg-row {
  display: flex;
  align-items: flex-start;
  gap: 10px;
  margin-bottom: 16px;
}

.msg-row.mine {
  flex-direction: row-reverse;
}

.bubble-wrap {
  display: flex;
  flex-direction: column;
  gap: 4px;
  max-width: 60%;
}

.msg-row.mine .bubble-wrap {
  align-items: flex-end;
}

.bubble {
  padding: 9px 12px;
  border-radius: 10px;
  background: var(--el-fill-color-light);
  color: var(--el-text-color-primary);
  font-size: 14px;
  line-height: 1.6;
  white-space: pre-wrap;
  word-break: break-word;
}

.msg-row.mine .bubble {
  background: var(--el-color-primary);
  color: #fff;
}

.meta {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 11px;
  color: var(--el-text-color-placeholder);
}

.read-ack {
  color: var(--el-color-success);
}

/* ==================== 输入区 ==================== */
.chat-input {
  border-top: 1px solid var(--el-border-color-light);
  padding: 10px 16px 12px;
}

.input-area {
  display: block;
  width: 100%;
  box-sizing: border-box;
  padding: 10px 12px;
  border: 1px solid var(--el-border-color);
  border-radius: 6px;
  background: var(--el-bg-color);
  color: var(--el-text-color-primary);
  font-size: 14px;
  line-height: 1.6;
  font-family: inherit;
  resize: none;
  outline: none;
  transition: border-color 0.15s;
}

.input-area:focus {
  border-color: var(--el-color-primary);
}

.input-area:disabled {
  background: var(--el-fill-color-lighter);
  color: var(--el-text-color-placeholder);
  cursor: not-allowed;
}

.input-actions {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-top: 8px;
}

.input-hint {
  font-size: 12px;
  color: var(--el-text-color-placeholder);
}

/* ==================== 空态 ==================== */
.chat-empty {
  flex: 1;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 12px;
  color: var(--el-text-color-secondary);
}

.chat-empty p {
  font-size: 14px;
}

.empty-icon {
  color: var(--el-text-color-placeholder);
}
</style>

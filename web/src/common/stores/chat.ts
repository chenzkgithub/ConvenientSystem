import { defineStore } from 'pinia'
import { ref, watch } from 'vue'
import type { HubConnection } from '@microsoft/signalr'
import { useAuthStore } from '@/common/stores/auth'
import {
  CHAT_EVENTS,
  createChatHubConnection,
  getChatConversations,
  getChatContacts,
  getChatMessages,
  getChatUnreadTotal,
  markChatRead,
  openChatConversation,
  parseUserIdFromToken,
  sendChatMessage,
} from '@/common/api/chat'
import type { ChatConversationDto, ChatMessageDto, ChatOpenDto } from '@/common/api/chat'

/** SignalR 不可用时的轮询间隔（ms） */
const POLL_INTERVAL = 5_000
/** 降级轮询期间重试实时连接的间隔（ms）：恢复即停轮询回到秒级推送 */
const RETRY_DELAY = 30_000

/** 每页消息条数（与后端 limit 默认值一致） */
const PAGE_SIZE = 50

/**
 * 聊天状态与实时连接中心（单例 store）。
 * 数据流：REST 拉取基础数据（会话/消息/未读/通讯录） + SignalR 推送增量维护。
 * SignalR 不可用（桌面壳反向代理不支持 WebSocket 等）时自动降级为 REST 轮询；
 * 顶栏红点另有独立轮询（ChatBell），可用性不依赖本连接。
 */
export const useChatStore = defineStore('chat', () => {
  const authStore = useAuthStore()

  // ==================== 状态 ====================

  const conversations = ref<ChatConversationDto[]>([])
  const unreadTotal = ref(0)
  /** 在线用户 Id 集合（UserOnline/UserOffline 增量维护；通讯录加载时全量重建） */
  const onlineIds = ref<Set<string>>(new Set())
  /** 当前打开的会话 Id（0 表示未打开） */
  const activeConversationId = ref(0)
  /** 当前会话消息（正序）；切换会话时整体替换 */
  const messages = ref<ChatMessageDto[]>([])
  /** 对方已读到的消息 Id（我方气泡"已读"标记用；0 表示未知） */
  const peerReadMessageId = ref(0)
  /** 我的用户 Id（JWT 解析；空串时消息归属判断降级为全部按对方渲染） */
  const myUserId = ref('')
  /** SignalR 实时连接是否已建立 */
  const connected = ref(false)
  /** 实时通道不可用、已降级 REST 轮询（ChatView 顶部提示"消息非实时"用） */
  const polling = ref(false)

  /** 聊天弹窗显隐状态（ChatDialog 双向绑定） */
  const dialogVisible = ref(false)
  /** 弹窗打开时指定的初始 peerId（空串表示不自动打开会话） */
  const dialogPeerId = ref('')

  // ==================== 连接与轮询管理（非响应式内部状态） ====================

  let connection: HubConnection | null = null
  let starting = false
  let pollTimer: number | null = null
  /** 实时连接重试定时器（降级轮询期间定期重试 SignalR） */
  let retryTimer: number | null = null
  /** 当前会话对方的用户 Id（空串表示未打开）；轮询校准已读水位用 */
  let activePeerId = ''

  /** 建立实时连接（幂等）：MainLayout 挂载后与 ChatView 进入时调用 */
  async function start() {
    if (connection || starting) return
    if (!authStore.loggedIn || !authStore.token) return
    starting = true
    try {
      myUserId.value = parseUserIdFromToken(authStore.token)
      // 兜底账号（数据库不可用时登录）userId 为空 Guid，聊天功能不可用，直接跳过
      if (!myUserId.value || myUserId.value === '00000000-0000-0000-0000-000000000000') {
        starting = false
        return
      }
      const conn = createChatHubConnection(() => authStore.token)
      registerEvents(conn)
      await conn.start()
      connection = conn
      connected.value = true
      polling.value = false
      stopPolling()
      // 自己刚连上收不到"之前已在线用户"的任何事件（UserOnline 仅在对方连接那刻广播），全量拉一次在线快照
      void refreshOnline()
    } catch {
      // 实时通道暂不可用（断网、服务重启、代理故障等）：降级 REST 轮询，轮询期间定期重试
      connection = null
      connected.value = false
      polling.value = true
      startPolling()
    } finally {
      starting = false
    }
  }

  /** 停止连接与轮询并清空业务状态（登出时调用） */
  async function stop() {
    stopPolling()
    connected.value = false
    polling.value = false
    const conn = connection
    connection = null
    if (conn) {
      try { await conn.stop() } catch { /* 连接已断开时忽略 */ }
    }
    conversations.value = []
    messages.value = []
    onlineIds.value = new Set()
    unreadTotal.value = 0
    activeConversationId.value = 0
    peerReadMessageId.value = 0
    myUserId.value = ''
    activePeerId = ''
  }

  /** 登出时自动停止（单向依赖 auth，避免 auth 反向引用 chat 造成循环） */
  watch(() => authStore.loggedIn, (v) => {
    if (!v) void stop()
  })

  function registerEvents(conn: HubConnection) {
    conn.on(CHAT_EVENTS.receiveMessage, (msg: ChatMessageDto) => handleReceiveMessage(msg))
    conn.on(CHAT_EVENTS.readAck, (conversationId: number, _readerId: string, messageId: number) => {
      if (conversationId === activeConversationId.value) {
        peerReadMessageId.value = Math.max(peerReadMessageId.value, messageId)
      }
    })
    conn.on(CHAT_EVENTS.userOnline, (userId: string) => onlineIds.value.add(userId))
    conn.on(CHAT_EVENTS.userOffline, (userId: string) => onlineIds.value.delete(userId))
    // 新系统通知广播：转发 window 事件，NoticeBell 刷新角标、NoticeAlert 立即拉取弹卡片。
    // 通知与聊天共用本条 SignalR 连接（单连接多事件）；可见性/去重由各组件拉取时自行处理。
    conn.on(CHAT_EVENTS.noticeCreated, () => {
      window.dispatchEvent(new CustomEvent('notice:created'))
    })

    conn.onreconnecting(() => { connected.value = false })
    conn.onreconnected(() => {
      connected.value = true
      polling.value = false
      stopPolling()
      // 断线期间的丢失增量全量校准
      void refreshConversations()
      void refreshUnread()
    })
    // 自动重连放弃后彻底断开：降级轮询保底
    conn.onclose(() => {
      if (!connection) return
      connected.value = false
      polling.value = true
      startPolling()
    })
  }

  // ==================== 消息与会话维护 ====================

  /**
   * 收到新消息（SignalR 推送）。发送方也会收到自己消息的推送副本（多端同步），
   * 与 REST Send 返回后的本地处理按消息 Id 去重，两份先后到达均安全。
   */
  function handleReceiveMessage(msg: ChatMessageDto) {
    const fromMe = !!myUserId.value && msg.senderId === myUserId.value
    const isActive = msg.conversationId === activeConversationId.value
    if (isActive) {
      if (!messages.value.some(m => m.id === msg.id)) messages.value.push(msg)
      void markReadActive()
    }
    // 更新会话列表项（不存在则为新会话/被隐藏会话恢复，全量刷新兜底）
    const conv = conversations.value.find(c => c.conversationId === msg.conversationId)
    if (conv) {
      conv.lastMessageText = msg.content
      conv.lastMessageTime = msg.createTime
      conv.lastFromMe = fromMe
      if (!fromMe && !isActive) {
        conv.unreadCount++
        unreadTotal.value++
      }
      // 该会话移到列表顶部
      conversations.value = [conv, ...conversations.value.filter(c => c !== conv)]
    } else {
      void refreshConversations()
      if (!fromMe && !isActive) void refreshUnread()
    }
  }

  /** 把当前会话已读水位推进到最新消息（只进不退，服务端幂等） */
  async function markReadActive() {
    const convId = activeConversationId.value
    const last = messages.value[messages.value.length - 1]
    if (!convId || !last) return
    const conv = conversations.value.find(c => c.conversationId === convId)
    if (conv && conv.unreadCount > 0) {
      unreadTotal.value = Math.max(0, unreadTotal.value - conv.unreadCount)
      conv.unreadCount = 0
    }
    try {
      await markChatRead(convId, last.id)
    } catch {
      /* 静默：轮询/下次事件到达时会校准 */
    }
  }

  /** 打开（或创建）会话并加载最新一页消息（ChatView 与路由入口共用） */
  async function openConversation(peerId: string): Promise<ChatOpenDto> {
    const dto = await openChatConversation(peerId)
    activeConversationId.value = dto.conversationId
    activePeerId = peerId
    peerReadMessageId.value = dto.peerReadMessageId
    messages.value = await getChatMessages(dto.conversationId)
    void markReadActive()
    // 会话列表同步：已有会话清未读置顶；新会话/隐藏恢复全量刷新落位
    const conv = conversations.value.find(c => c.conversationId === dto.conversationId)
    if (conv) {
      conv.unreadCount = 0
      conversations.value = [conv, ...conversations.value.filter(c => c !== conv)]
    } else {
      void refreshConversations()
    }
    return dto
  }

  /** 关闭当前会话（ChatView 卸载时调用；列表与未读状态保留） */
  function closeConversation() {
    activeConversationId.value = 0
    activePeerId = ''
    messages.value = []
    peerReadMessageId.value = 0
  }

  /** 发送消息：REST 落库返回后本地即时上屏（推送副本按 Id 去重） */
  async function sendMessage(peerId: string, content: string): Promise<ChatMessageDto> {
    const msg = await sendChatMessage(peerId, content)
    handleReceiveMessage(msg)
    return msg
  }

  /** 向上翻页加载更早消息；返回是否可能还有更早的一页 */
  async function loadEarlierMessages(): Promise<boolean> {
    const convId = activeConversationId.value
    const first = messages.value[0]
    if (!convId || !first) return false
    const list = await getChatMessages(convId, first.id, PAGE_SIZE)
    if (!list.length) return false
    messages.value = [...list, ...messages.value]
    return list.length >= PAGE_SIZE
  }

  /** 轮询模式下当前会话消息增量合并（SignalR 不可用时的兜底） */
  async function pollActiveMessages() {
    const convId = activeConversationId.value
    if (!convId) return
    try {
      const list = await getChatMessages(convId, 0, PAGE_SIZE, { silent: true })
      const known = new Set(messages.value.map(m => m.id))
      let added = false
      for (const m of list) {
        if (!known.has(m.id)) { messages.value.push(m); added = true }
      }
      if (added) {
        messages.value.sort((a, b) => a.id - b.id)
        void markReadActive()
      }
    } catch {
      /* 静默：下一轮再试 */
    }
  }

  /**
   * 轮询模式下校准对方已读水位（SignalR ReadAck 推不到时的兜底）：
   * 静默调 Open 接口拿最新 peerReadMessageId。Open 对已存在会话幂等
   * （仅清我方隐藏标记，当前打开中的会话无影响），可安全高频调用。
   */
  async function pollActiveReadWatermark() {
    const peerId = activePeerId
    if (!peerId) return
    try {
      const dto = await openChatConversation(peerId, { silent: true })
      if (dto.conversationId === activeConversationId.value) {
        peerReadMessageId.value = Math.max(peerReadMessageId.value, dto.peerReadMessageId)
      }
    } catch {
      /* 静默：下一轮再试 */
    }
  }

  // ==================== 基础数据刷新 ====================

  /** 静默刷新会话列表（保留旧列表于失败时） */
  async function refreshConversations() {
    try {
      conversations.value = await getChatConversations({ silent: true })
    } catch {
      /* 静默 */
    }
  }

  /** 静默校准总未读数 */
  async function refreshUnread() {
    try {
      const dto = await getChatUnreadTotal()
      unreadTotal.value = dto.count
    } catch {
      /* 静默 */
    }
  }

  /** 刷新通讯录并全量重建在线集合（返回联系人列表供调用方渲染） */
  async function refreshContacts() {
    const list = await getChatContacts()
    onlineIds.value = new Set(list.filter(c => c.online).map(c => c.userId))
    return list
  }

  /** 静默校准在线状态（拉通讯录重建在线集合，不动通讯录数据）：实时初连与轮询 tick 共用 */
  async function refreshOnline() {
    try {
      const list = await getChatContacts({ silent: true })
      onlineIds.value = new Set(list.filter(c => c.online).map(c => c.userId))
    } catch {
      /* 静默：下一轮再试 */
    }
  }

  // ==================== 轮询降级 ====================

  function startPolling() {
    if (pollTimer === null) {
      const tick = () => {
        void refreshConversations()
        void refreshUnread()
        void pollActiveMessages()
        void pollActiveReadWatermark()
        // 轮询模式下收不到 UserOnline/UserOffline 事件，在线状态也静默校准
        void refreshOnline()
      }
      tick()
      pollTimer = window.setInterval(tick, POLL_INTERVAL)
    }
    // 降级期间定期重试实时连接：恢复即停轮询回到秒级推送（断网恢复、代理修好等场景）
    scheduleRetry()
  }

  function scheduleRetry() {
    if (retryTimer !== null) return
    retryTimer = window.setTimeout(async () => {
      retryTimer = null
      if (connection || !authStore.loggedIn) return
      await start()
    }, RETRY_DELAY)
  }

  function stopPolling() {
    if (pollTimer !== null) {
      clearInterval(pollTimer)
      pollTimer = null
    }
    if (retryTimer !== null) {
      clearTimeout(retryTimer)
      retryTimer = null
    }
  }

  /** 打开聊天弹窗：可选传入 peerId，打开时自动定向到与指定用户的会话 */
  function openChat(peerId?: string) {
    dialogPeerId.value = peerId ?? ''
    dialogVisible.value = true
  }

  /** 关闭聊天弹窗并清空初始 peerId */
  function closeChat() {
    dialogVisible.value = false
    dialogPeerId.value = ''
  }

  return {
    // 状态
    conversations, unreadTotal, onlineIds, activeConversationId, messages,
    peerReadMessageId, myUserId, connected, polling,
    dialogVisible, dialogPeerId,
    // 连接
    start, stop,
    // 弹窗
    openChat, closeChat,
    // 数据
    refreshConversations, refreshUnread, refreshContacts,
    openConversation, closeConversation, loadEarlierMessages, sendMessage, markReadActive,
  }
})

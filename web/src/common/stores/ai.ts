import { ref, computed } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import {
  type AiMyStatus, type AiConversation, type AiMessage, type AiChunk, type AiToolEvent,
  getAiMyStatus, getAiConversations, getAiMessages, deleteAiConversation,
  sendAiMessage, stopAiGeneration, regenerateAiMessage,
} from '@/common/api/ai'

/** 工具调用状态（后端生成过程按 CallId 推送 executing → done/error） */
export interface AiToolCallState {
  id: string
  name: string
  status: 'executing' | 'done' | 'error'
  result?: string
  error?: string
}

/**
 * AI 助手全局状态：抽屉开关 / 会话与消息 / 流式生成拼接。
 * chunk 经 chat store 的 window 事件 'ai:chunk' 桥接进来（单连接多事件，不单独建 SignalR 连接）；
 * 推送通道不可用时自动降级为轮询 Messages（发送后 4 秒未收到任何 chunk 即启动，2 秒一次，
 * 直到 assistant 消息 Status≠0——内容后端已落库，轮询一定能拿到终态）。
 */
const drawerOpen = ref(false)
const status = ref<AiMyStatus | null>(null)
const conversations = ref<AiConversation[]>([])
/** 当前会话 Id；0 = 新对话（首条消息发送后才真正建会话） */
const currentConversationId = ref(0)
const messages = ref<AiMessage[]>([])
const sending = ref(false)
/** 生成中的 assistant 消息 Id（停止按钮 & chunk 过滤用） */
const generatingMessageId = ref(0)
const loadingMessages = ref(false)
/** 发送中 4 秒内是否收到过 chunk（决定是否启动轮询兜底） */
let chunkArrived = false
let pollTimer: ReturnType<typeof setInterval> | null = null

/** 当前生成的工具调用横条（生成结束清空；SignalR 断链时无中间态，轮询直接拿终态） */
const toolCalls = ref<AiToolCallState[]>([])

/** 今日配额文本 */
const quotaText = computed(() => {
  const s = status.value
  if (!s) return ''
  return s.requestsQuota > 0 ? `${s.requestsUsed}/${s.requestsQuota} 次` : '不限'
})

const canSend = computed(() =>
  !!status.value?.enabled && !sending.value && !!status.value.modelReady)

/** window 事件桥接的 chunk 处理：过滤非当前生成的消息，工具事件更新横条，文本增量拼接、终态覆盖 */
function handleChunk(chunk: AiChunk) {
  if (chunk.messageId !== generatingMessageId.value) return
  chunkArrived = true
  // 工具调用中间态：更新工具横条，不触碰消息内容
  if (chunk.toolEvent) {
    upsertToolCall(chunk.toolEvent)
    return
  }
  const target = messages.value.find(m => m.id === chunk.messageId)
  if (!target) return
  if (!chunk.done) {
    target.content += chunk.delta
    return
  }
  // 终态：done chunk 携带完整最终内容（后端已落库），直接覆盖防止节流丢字
  if (chunk.content != null) target.content = chunk.content
  target.status = chunk.error ? 3 : 1
  target.error = chunk.error
  finishGeneration()
}

/** 工具事件按 CallId 原位更新（同一次调用 executing → done/error） */
function upsertToolCall(ev: AiToolEvent) {
  const existing = toolCalls.value.find(t => t.id === ev.callId)
  if (existing) {
    existing.status = ev.status
    existing.result = ev.result ?? undefined
    existing.error = ev.error ?? undefined
  } else {
    toolCalls.value.push({
      id: ev.callId,
      name: ev.name,
      status: ev.status,
      result: ev.result ?? undefined,
      error: ev.error ?? undefined,
    })
  }
}

function finishGeneration() {
  sending.value = false
  generatingMessageId.value = 0
  toolCalls.value = []
  stopPolling()
  void refreshStatus()
  void loadConversations()
}

function stopPolling() {
  if (pollTimer) {
    clearInterval(pollTimer)
    pollTimer = null
  }
}

/** 轮询兜底：SignalR 不可用/丢推送时按 Messages 接口拉取终态（2s 一次，最长 3 分钟） */
function startPolling() {
  stopPolling()
  const conversationId = currentConversationId.value
  const messageId = generatingMessageId.value
  let elapsed = 0
  pollTimer = setInterval(async () => {
    elapsed += 2
    if (!messageId || !conversationId) return stopPolling()
    try {
      const list = await getAiMessages(conversationId)
      const final = list.find(m => m.id === messageId)
      if (final && final.status !== 0) {
        const target = messages.value.find(m => m.id === messageId)
        if (target) {
          target.content = final.content
          target.status = final.status
          target.error = final.error
        }
        finishGeneration()
        return
      }
      // 3 分钟仍未终态（服务重启残留 Status=0 等）：放弃等待并提示
      if (elapsed >= 180) {
        ElMessage.warning('生成超时，可稍后重新打开会话查看结果')
        finishGeneration()
      }
    } catch {
      /* 轮询失败静默，下次重试 */
    }
  }, 2000)
}

// 模块级单例监听（chat store 转发的 SignalR chunk）
window.addEventListener('ai:chunk', (e: Event) => {
  handleChunk((e as CustomEvent).detail as AiChunk)
})

// ---------------------------------------------------------------- 状态加载

async function refreshStatus() {
  try {
    status.value = await getAiMyStatus()
  } catch {
    /* silent：无 ai-chat 权限时接口 403，抽屉不展示功能即可 */
  }
}

async function loadConversations() {
  try {
    conversations.value = await getAiConversations()
  } catch {
    /* silent */
  }
}

async function openConversation(conversationId: number) {
  if (sending.value) {
    ElMessage.info('正在生成中，请先停止或等待完成')
    return
  }
  currentConversationId.value = conversationId
  toolCalls.value = []
  if (conversationId === 0) {
    messages.value = []
    return
  }
  loadingMessages.value = true
  try {
    messages.value = await getAiMessages(conversationId)
  } catch {
    /* httpGet silent，失败保持空列表 */
  } finally {
    loadingMessages.value = false
  }
}

function open() {
  drawerOpen.value = true
  void refreshStatus()
  void loadConversations()
  if (currentConversationId.value !== 0) void openConversation(currentConversationId.value)
}

function close() {
  drawerOpen.value = false
}

function toggle() {
  drawerOpen.value ? close() : open()
}

/** 新对话（回到未建会话状态；正在生成中则拦截） */
function newConversation() {
  if (sending.value) {
    ElMessage.info('正在生成中，请先停止或等待完成')
    return
  }
  currentConversationId.value = 0
  messages.value = []
  toolCalls.value = []
}

async function removeConversation(conversationId: number) {
  try {
    await deleteAiConversation(conversationId)
    if (currentConversationId.value === conversationId) {
      currentConversationId.value = 0
      messages.value = []
    }
    await loadConversations()
  } catch {
    /* httpDelete 已弹错误提示 */
  }
}

// ---------------------------------------------------------------- 发送 / 停止

/** 发送消息：本地立刻补 user 消息与 assistant 占位，流式 chunk/轮询回填占位内容 */
async function sendMessage(content: string) {
  const text = content.trim()
  if (!text) return
  if (!canSend.value) {
    if (!status.value?.enabled) ElMessage.warning('AI 功能未启用')
    else if (!status.value.modelReady) ElMessage.warning('系统未配置可用模型，请联系管理员')
    return
  }
  sending.value = true
  chunkArrived = false
  toolCalls.value = []
  try {
    const result = await sendAiMessage({
      conversationId: currentConversationId.value,
      content: text,
    })
    currentConversationId.value = result.conversationId
    messages.value.push(
      { id: result.userMessageId, conversationId: result.conversationId, role: 'user', content: text, status: 1, tokensIn: 0, tokensOut: 0, error: null, createTime: new Date().toISOString() },
      { id: result.assistantMessageId, conversationId: result.conversationId, role: 'assistant', content: '', status: 0, tokensIn: 0, tokensOut: 0, error: null, createTime: new Date().toISOString() },
    )
    generatingMessageId.value = result.assistantMessageId
    // 4 秒未收到任何 chunk（SignalR 断链）→ 轮询兜底
    setTimeout(() => {
      if (generatingMessageId.value === result.assistantMessageId && !chunkArrived) startPolling()
    }, 4000)
    void loadConversations()
  } catch {
    /* httpPost 已弹错误提示（配额/开关/未配置模型） */
    sending.value = false
  }
}

async function stopGenerating() {
  const messageId = generatingMessageId.value
  if (!messageId) return
  try {
    await stopAiGeneration(messageId)
    // 终态 chunk（已手动停止）随即到达；SignalR 不可用时由轮询兜底收尾
  } catch {
    /* httpGet 已弹错误提示 */
  }
}

/** 重新生成某条 AI 回复：重置本地内容后走同一套流式 chunk/轮询兜底链路 */
async function regenerate(messageId: number) {
  if (sending.value) return
  const target = messages.value.find(m => m.id === messageId)
  if (!target) return
  sending.value = true
  chunkArrived = false
  toolCalls.value = []
  try {
    const result = await regenerateAiMessage(messageId)
    target.content = ''
    target.status = 0
    target.error = null
    generatingMessageId.value = result.assistantMessageId
    // 4 秒未收到任何 chunk（SignalR 断链）→ 轮询兜底
    setTimeout(() => {
      if (generatingMessageId.value === result.assistantMessageId && !chunkArrived) startPolling()
    }, 4000)
    void loadConversations()
  } catch {
    /* httpPost 已弹错误提示（配额/开关/未配置模型） */
    sending.value = false
  }
}

export function useAiStore() {
  return {
    drawerOpen, status, conversations, currentConversationId, messages,
    sending, generatingMessageId, loadingMessages, toolCalls,
    quotaText, canSend,
    open, close, toggle, newConversation, openConversation, removeConversation,
    sendMessage, stopGenerating, regenerate,
  }
}

// 模块加载时自动初始化状态（顶栏图标显隐依赖 status.enabled）
void refreshStatus()
void loadConversations()

import { httpGet, httpPost, httpDelete } from '@/api/request'

/** 模型管理列表项（后端永不回传明文 Key，仅 HasKey） */
export interface AiModelItem {
  id: number
  name: string
  baseUrl: string
  modelId: string
  scenes: string
  isDefault: boolean
  enabled: boolean
  hasKey: boolean
  maxContextChars: number
  sortOrder: number
}

/** 新增/编辑模型请求（id=0 新增；apiKey 留空 = 编辑时保持原 Key 不变） */
export interface AiModelSaveRequest {
  id: number
  name: string
  baseUrl: string
  apiKey?: string
  modelId: string
  maxContextChars: number
  enabled: boolean
}

export interface AiModelTestResult {
  ok: boolean
  message: string
  elapsedMs: number
}

/** 助手抽屉头部状态（一次拉齐：开关/配额/模型就绪；模型由管理员统一配置，用户不可选） */
export interface AiMyStatus {
  enabled: boolean
  requestsUsed: number
  requestsQuota: number
  modelReady: boolean
}

export interface AiConversation {
  id: number
  title: string
  updateTime: string
}

/** 对话消息（status：0 生成中 / 1 完成 / 2 失败 / 3 已手动停止） */
export interface AiMessage {
  id: number
  conversationId: number
  role: 'user' | 'assistant'
  content: string
  status: number
  tokensIn: number
  tokensOut: number
  error: string | null
  createTime: string
}

/** 发送结果：拿到 assistantMessageId 后监听流式 chunk（SignalR）或轮询 Messages 兜底 */
export interface AiSendResult {
  conversationId: number
  userMessageId: number
  assistantMessageId: number
}

/** 流式 chunk（chat store 经 window 事件桥接到 ai store；done=true 时 content 为完整最终内容） */
export interface AiChunk {
  conversationId: number
  messageId: number
  delta: string
  done: boolean
  error: string | null
  content: string | null
  /** 工具调用中间态事件（null = 普通文本增量/终态） */
  toolEvent: AiToolEvent | null
}

/** 工具调用中间态事件（后端逐个推送；status: executing / done / error） */
export interface AiToolEvent {
  callId: string
  name: string
  status: 'executing' | 'done' | 'error'
  result: string | null
  error: string | null
}

// ---------------- 模型管理（系统配置页 AI 页签，sys-config:save 权限） ----------------

export function listAiModels() {
  return httpGet<AiModelItem[]>('/api/Common/AiModel/List', undefined, undefined, { silent: true })
}

export function saveAiModel(request: AiModelSaveRequest) {
  return httpPost<AiModelItem>('/api/Common/AiModel/Save', request)
}

export function testAiModel(request: { id: number; baseUrl: string; apiKey?: string; modelId: string }) {
  return httpPost<AiModelTestResult>('/api/Common/AiModel/Test', request, undefined, 45_000, { noLoading: true })
}

export function setAiModelDefault(id: number) {
  return httpPost<void>(`/api/Common/AiModel/SetDefault?id=${id}`, {})
}

export function deleteAiModel(id: number) {
  return httpDelete<void>(`/api/Common/AiModel/Delete?id=${id}`)
}

// ---------------- AI 对话（全局助手抽屉，ai-chat 权限） ----------------

export function getAiMyStatus() {
  return httpGet<AiMyStatus>('/api/Common/Ai/MyStatus', undefined, undefined, { silent: true })
}

export function getAiConversations() {
  return httpGet<AiConversation[]>('/api/Common/Ai/Conversations', undefined, undefined, { silent: true })
}

export function getAiMessages(conversationId: number) {
  return httpGet<AiMessage[]>('/api/Common/Ai/Messages', { conversationId }, undefined, { silent: true })
}

export function deleteAiConversation(conversationId: number) {
  return httpDelete<void>(`/api/Common/Ai/DeleteConversation?conversationId=${conversationId}`)
}

export function sendAiMessage(request: { conversationId: number; content: string }) {
  return httpPost<AiSendResult>('/api/Common/Ai/Send', request)
}

/** 重新生成：重置该 AI 回复并重新生成（配额同发送） */
export function regenerateAiMessage(messageId: number) {
  return httpPost<AiSendResult>(`/api/Common/Ai/Regenerate?messageId=${messageId}`, {})
}

export function stopAiGeneration(messageId: number) {
  return httpPost<void>(`/api/Common/Ai/Stop?messageId=${messageId}`, {}, undefined, undefined, { silent: true })
}

// ---------------- AI 任务（create_task 工具落库，助手抽屉任务面板） ----------------

/** AI 创建的任务（status：0 待办 / 1 完成） */
export interface AiTask {
  id: number
  title: string
  description: string | null
  dueDate: string | null
  status: number
  createTime: string
}

/** 我的 AI 任务列表 */
export function getAiTasks() {
  return httpGet<AiTask[]>('/api/Common/Ai/Tasks', undefined, undefined, { silent: true })
}

/** 更新任务状态（0 待办 / 1 完成，任务面板勾选） */
export function updateAiTaskStatus(taskId: number, status: number) {
  return httpPost<void>(`/api/Common/Ai/TaskStatus?taskId=${taskId}&status=${status}`, {})
}

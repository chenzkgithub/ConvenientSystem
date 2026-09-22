import { HubConnection, HubConnectionBuilder } from '@microsoft/signalr'
import { API_BASE, httpGet, httpPost } from '@/api/request'

// ==================== DTO 类型（与后端 ChatDto.cs 字段一一对应） ====================

/** 会话列表项：我方视角（对方信息/群信息 + 最后一条消息 + 未读数 + 屏蔽标记） */
export interface ChatConversationDto {
  conversationId: number
  /** 会话类型：0=单聊 1=群聊 */
  conversationType: number
  /** 对方用户 Id（Guid 字符串）；群聊为空字符串 */
  peerId: string
  peerAccount: string
  peerDisplayName?: string | null
  /** 对方头像/群头像 data URL（空表示未设置） */
  peerAvatar?: string | null
  /** 群聊名称（单聊为空） */
  groupName?: string | null
  /** 成员数：单聊固定 2，群聊为实际人数 */
  memberCount: number
  lastMessageText?: string | null
  lastMessageTime?: string | null
  /** 最后一条是否我发送（列表预览前缀"我："用） */
  lastFromMe: boolean
  unreadCount: number
  muted: boolean
  /** 我屏蔽了对方（双向拒收；群聊固定 false） */
  blockedByMe: boolean
  /** 对方屏蔽了我（双向拒收；群聊固定 false） */
  blockedMe: boolean
}

/** 通讯录联系人（在线状态由 Api 层填充） */
export interface ChatContactDto {
  userId: string
  account: string
  displayName?: string | null
  avatar?: string | null
  online: boolean
  blockedByMe: boolean
  blockedMe: boolean
}

/** 聊天消息（是否我发送由前端按 senderId === 我的 userId 判断） */
export interface ChatMessageDto {
  id: number
  conversationId: number
  senderId: string
  senderAccount: string
  senderDisplayName?: string | null
  /** 正文：文本为纯文本；图片为相对路径；合并转发卡片为标题 */
  content: string
  /** 消息类型：0=文本 1=图片 2=合并转发记录卡片 */
  msgType: number
  /** 引用的原消息 Id（0=无引用） */
  quoteId: number
  /** 引用内容快照（原消息被单方面删除后仍可显示） */
  quoteText?: string | null
  quoteSenderId?: string | null
  /** 被引用消息发送者显示名（引用块“xxx：”用） */
  quoteSenderName?: string | null
  /** 合并转发卡片指向的记录 Id（msgType=2 时有效） */
  refRecordId?: number | null
  /** 被@的用户 Id 列表（群聊用） */
  mentions?: string[] | null
  createTime: string
}

/** 打开（或创建）会话结果 */
export interface ChatOpenDto {
  conversationId: number
  peerId: string
  peerAccount: string
  peerDisplayName?: string | null
  peerAvatar?: string | null
  blockedMe: boolean
  blockedByMe: boolean
  /** 对方已读水位（0=对方尚未读过任何消息） */
  peerReadMessageId: number
}

/** 屏蔽名单项 */
export interface ChatBlockDto {
  userId: string
  account: string
  displayName?: string | null
  avatar?: string | null
  createTime: string
}

/** 合并转发快照内的单条消息 */
export interface ChatForwardItemDto {
  /** 原发送者显示名 */
  senderName: string
  /** 消息类型：0=文本 1=图片 */
  msgType: number
  content: string
  time: string
}

/** 合并转发记录查看结果（只读快照） */
export interface ChatForwardRecordDto {
  id: number
  title: string
  items: ChatForwardItemDto[]
}

/** 转发消息请求：merged=true 合并为一条卡片 / false 逐条 */
export interface ChatForwardRequest {
  messageIds: number[]
  targetPeerId: string
  merged: boolean
}

/** 图片上传结果：相对路径（消息 content 直接使用） */
export interface ChatImageDto {
  path: string
}

/** 创建群聊请求 */
export interface ChatGroupCreateRequest {
  name: string
  memberIds: string[]
}

/** 群成员信息 */
export interface ChatGroupMemberDto {
  userId: string
  account: string
  displayName?: string | null
  avatar?: string | null
  online: boolean
  /** 0=成员 1=群主 */
  role: number
}

/** 本服务上传图片路径格式。兼容首版误按文本落库的历史消息，不会把普通文本误识别为图片。 */
const CHAT_IMAGE_PATH_PATTERN = /^\d{6}\/[0-9a-f]{32}\.(?:jpg|jpeg|png|gif|webp|bmp)$/
export function isChatImagePath(content: string | null | undefined): boolean {
  return !!content && CHAT_IMAGE_PATH_PATTERN.test(content)
}

// ==================== REST API（ChatController，任何已登录用户可用） ====================

/** 我的会话列表（未隐藏，按最后消息时间倒序）；silent 供轮询静默刷新 */
export function getChatConversations(opts?: { silent?: boolean }) {
  return httpGet<ChatConversationDto[]>('/api/Common/Chat/Conversations', undefined, undefined, opts)
}

/** 通讯录：全部启用用户 + 在线状态 + 双向屏蔽标记；silent 供轮询静默校准在线状态 */
export function getChatContacts(opts?: { silent?: boolean }) {
  return httpGet<ChatContactDto[]>('/api/Common/Chat/Contacts', undefined, undefined, opts)
}

/** 打开（或创建）与指定用户的会话：清我方隐藏标记，返回会话 Id 与对方信息；silent 供轮询校准已读水位 */
export function openChatConversation(peerId: string, opts?: { silent?: boolean }) {
  return httpPost<ChatOpenDto>(`/api/Common/Chat/Open?peerId=${encodeURIComponent(peerId)}`, {}, undefined, undefined, opts)
}

/** 会话历史消息（正序返回）：beforeId>0 时取该 Id 之前一页（向上翻页） */
export function getChatMessages(conversationId: number, beforeId = 0, limit = 50, opts?: { silent?: boolean }) {
  return httpGet<ChatMessageDto[]>('/api/Common/Chat/Messages', { conversationId, beforeId, limit }, undefined, opts)
}

/** 按 Id 查询单条消息：用于引用点击定位；404 表示消息不存在或已被我方清空删除 */
export function getChatMessageById(conversationId: number, messageId: number, opts?: { silent?: boolean }) {
  return httpGet<ChatMessageDto>('/api/Common/Chat/MessageById', { conversationId, messageId }, undefined, opts)
}

/** 发送消息：单聊传 peerId（conversationId 可省略）；群聊 peerId 传空字符串并指定 conversationId。msgType=0 文本 / 1 图片；quoteId>0 时引用本会话已有消息。
 *  noLoading 供聊天页发送链路使用：高频操作不弹全局遮罩，但失败仍弹错误提示。 */
export function sendChatMessage(
  peerId: string,
  content: string,
  quoteId = 0,
  msgType = 0,
  conversationId = 0,
  mentions?: string[],
  opts?: { noLoading?: boolean },
) {
  return httpPost<ChatMessageDto>('/api/Common/Chat/Send', {
    // 群聊 peerId 传空串：后端 PeerId 是非可空 Guid（群聊约定 Guid.Empty），空串无法反序列化会被 400 拒收，兜底转全零 Guid
    peerId: peerId || '00000000-0000-0000-0000-000000000000',
    conversationId,
    content,
    quoteId,
    msgType,
    mentions: mentions ?? [],
  }, undefined, undefined, opts)
}

/** 创建群聊：群名必填，成员至少 1 人（创建者自动加入） */
export function createChatGroup(request: ChatGroupCreateRequest) {
  return httpPost<ChatConversationDto>('/api/Common/Chat/CreateGroup', request)
}

/** 获取群聊成员列表（含在线状态） */
export function getChatGroupMembers(conversationId: number) {
  return httpGet<ChatGroupMemberDto[]>('/api/Common/Chat/GroupMembers', { conversationId })
}

/** 上传聊天图片（≤10MB，jpg/png/gif/webp/bmp）；返回相对路径作为消息 content。noLoading 发送链路用：不弹全局遮罩但保留错误提示 */
export function uploadChatImage(file: File, opts?: { noLoading?: boolean }) {
  const form = new FormData()
  form.append('file', file)
  // 图片上传链路慢于普通接口：60s 超时；onUploadProgress 自带进度可后续按需接
  return httpPost<ChatImageDto>('/api/Common/Chat/UploadImage', form, undefined, 60_000, opts)
}

/** 聊天图片访问地址：Guid 文件名即访问凭证（img 标签无法携带 JWT），拼 API_BASE 桌面壳反代同域直通 */
export function chatImageUrl(path: string): string {
  return `${API_BASE}/api/Common/Chat/Image?f=${encodeURIComponent(path)}`
}

/** 转发消息到目标联系人：merged=false 逐条 / true 合并为一条“聊天记录”卡片 */
export function forwardChatMessages(request: ChatForwardRequest) {
  return httpPost<{ count: number }>('/api/Common/Chat/Forward', request)
}

/** 查看合并转发记录（快照只读） */
export function getChatForwardRecord(recordId: number) {
  return httpGet<ChatForwardRecordDto>('/api/Common/Chat/ForwardRecord', { recordId })
}

/** 单方面清空我方聊天记录：仅我方视图隐藏历史，对方不受影响 */
export function clearChatMessages(conversationId: number) {
  return httpPost<void>(`/api/Common/Chat/ClearMessages?conversationId=${conversationId}`, {})
}

/** 标记已读水位（静默：高频调用不弹遮罩与错误提示） */
export function markChatRead(conversationId: number, messageId: number) {
  return httpPost<void>('/api/Common/Chat/MarkRead', { conversationId, messageId }, undefined, undefined, { silent: true })
}

/** 总未读数（顶栏红点轮询，静默） */
export function getChatUnreadTotal() {
  return httpGet<{ count: number }>('/api/Common/Chat/UnreadTotal', undefined, undefined, { silent: true })
}

/** 屏蔽用户（幂等） */
export function blockChatUser(peerId: string) {
  return httpPost<void>(`/api/Common/Chat/Block?peerId=${encodeURIComponent(peerId)}`, {})
}

/** 取消屏蔽（幂等） */
export function unblockChatUser(peerId: string) {
  return httpPost<void>(`/api/Common/Chat/Unblock?peerId=${encodeURIComponent(peerId)}`, {})
}

/** 我屏蔽的名单 */
export function getChatBlocks() {
  return httpGet<ChatBlockDto[]>('/api/Common/Chat/Blocks')
}

/** 隐藏（删除）会话：仅我方视角，来新消息自动恢复 */
export function hideChatConversation(conversationId: number) {
  return httpPost<void>(`/api/Common/Chat/Hide?conversationId=${conversationId}`, {})
}

/** 设置会话免打扰（仍计未读，前端不提醒） */
export function setChatMuted(conversationId: number, muted: boolean) {
  return httpPost<void>(`/api/Common/Chat/SetMuted?conversationId=${conversationId}&muted=${muted}`, {})
}

// ==================== SignalR 连接 ====================

/** SignalR 事件名常量（与后端 ChatHub / ChatController 推送约定一致） */
export const CHAT_EVENTS = {
  /** 新消息（发给会话双方；ChatMessageDto 单参数） */
  receiveMessage: 'ReceiveMessage',
  /** 已读水位推进回执（发给对方；conversationId, readerId, messageId 三参数） */
  readAck: 'ReadAck',
  /** 上线广播（全部连接；userId 单参数） */
  userOnline: 'UserOnline',
  /** 下线广播（全部连接；userId 单参数） */
  userOffline: 'UserOffline',
  /** 新系统通知广播（全部连接；无参数，前端收到后各自拉取列表/未读数） */
  noticeCreated: 'NoticeCreated',
  /** 我方另一端清空了聊天记录（仅推给我自己，单方面删除多端同步；conversationId 单参数） */
  messagesCleared: 'MessagesCleared',
  /** 异步任务进度（定向推给任务发起人；AsyncTaskDto 单参数） */
  asyncTaskProgress: 'AsyncTaskProgress',
  /** AI 流式生成 chunk（定向推给发起人；AiChunk 单参数：delta 增量 / done 终态含完整 content） */
  aiResponseChunk: 'AiResponseChunk',
} as const

/**
 * 创建聊天 Hub 连接。accessTokenFactory 由调用方（chat store）传入，避免 api 层反向依赖 store；
 * SignalR 会把 token 拼到 query（access_token），与后端 JwtBearerEvents 对 /hubs/chat 的查询令牌读取匹配。
 * 不限制 transport：协商按 WebSockets→SSE→LongPolling 自动降级，
 * 全部失败由 store 降级为 REST 轮询（桌面壳反向代理不支持 WebSocket 的场景）。
 */
export function createChatHubConnection(accessTokenFactory: () => string): HubConnection {
  return new HubConnectionBuilder()
    .withUrl(`${API_BASE}/hubs/chat`, { accessTokenFactory })
    .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
    .build()
}

/**
 * 从 JWT payload 解析当前用户 Id（claim "userId"，Guid 字符串）。
 * 消息归属判断（senderId === 我的 id → 右侧气泡）依赖它；auth store 未暴露 userId 故就地解析。
 * 解析失败返回空串（此时消息全部按对方渲染，属降级显示）。
 */
export function parseUserIdFromToken(token: string): string {
  try {
    const part = token.split('.')[1]
    if (!part) return ''
    // base64url → base64（补齐 padding）
    let b64 = part.replace(/-/g, '+').replace(/_/g, '/')
    while (b64.length % 4 !== 0) b64 += '='
    const bytes = Uint8Array.from(atob(b64), c => c.charCodeAt(0))
    const payload = JSON.parse(new TextDecoder().decode(bytes)) as { userId?: unknown }
    return typeof payload.userId === 'string' ? payload.userId : ''
  } catch {
    return ''
  }
}

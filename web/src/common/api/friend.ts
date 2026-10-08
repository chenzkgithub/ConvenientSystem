import { httpGet, httpPost } from '@/api/request'

// ==================== DTO 类型（与后端 FriendDto.cs 字段一一对应） ====================

/** 好友搜索结果（服务端已按发起方平台过滤：对方在本平台可被添加，且排除自己/已有好友/双向屏蔽） */
export interface FriendSearchResultDto {
  userId: string
  account: string
  displayName?: string | null
  avatar?: string | null
}

/** 好友申请条目（接收方视角，含申请人信息） */
export interface FriendRequestDto {
  id: number
  fromUserId: string
  fromAccount: string
  fromDisplayName?: string | null
  fromAvatar?: string | null
  /** 验证留言（可选） */
  message?: string | null
  /** 0=待处理 1=已同意 2=已拒绝 */
  status: number
  createTime: string
}

/** 好友申请处理结果（SignalR FriendRequestHandled 事件体，推给申请人） */
export interface FriendRequestHandledDto {
  requestId: number
  /** true=已同意（双方已互为好友） */
  accept: boolean
  /** 处理人显示名（提示「张三同意了你的好友申请」用） */
  handlerName?: string | null
}

// ==================== REST API（FriendController：PC 端任何已登录用户可用；手机端要求 app-friends） ====================

/** 搜索可添加的用户（服务端按发起方平台过滤） */
export function searchFriendUsers(keyword: string, opts?: { silent?: boolean }) {
  return httpGet<FriendSearchResultDto[]>('/api/Common/Friend/Search', { keyword }, undefined, opts)
}

/** 我收到的好友申请（全部状态，最新在前） */
export function getFriendRequests(opts?: { silent?: boolean }) {
  return httpGet<FriendRequestDto[]>('/api/Common/Friend/Requests', undefined, undefined, opts)
}

/** 待处理好友申请数（角标轮询，静默） */
export function getFriendPendingCount() {
  return httpGet<{ count: number }>('/api/Common/Friend/PendingCount', undefined, undefined, { silent: true })
}

/** 发起好友申请（验证留言可选，最长 100 字；成功后对方收到实时推送） */
export function sendFriendRequest(toUserId: string, message?: string) {
  return httpPost<FriendRequestDto>('/api/Common/Friend/Send', { toUserId, message: message ?? '' })
}

/** 处理好友申请：accept=true 同意（双方互加好友）/ false 拒绝；处理结果实时推给申请人 */
export function handleFriendRequest(requestId: number, accept: boolean) {
  return httpPost<FriendRequestHandledDto>('/api/Common/Friend/Handle', { requestId, accept })
}

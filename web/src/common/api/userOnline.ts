import { httpGet } from '@/api/request'

/** 在线用户记录 */
export interface OnlineUserDto {
  userId: string
  account: string
  displayName?: string | null
  ip: string
  loginTime: string
  lastSeen: string
}

/** 在线用户列表（最近 6 分钟内有心跳的用户） */
export function listOnlineUsers() {
  return httpGet<OnlineUserDto[]>('/api/Common/UserOnline/List')
}

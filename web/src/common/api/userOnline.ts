import { httpGet } from '@/api/request'

/** 在线用户记录 */
export interface OnlineUserDto {
  userId: string
  account: string
  displayName?: string | null
  ip: string
  loginTime: string
  /** 最后真实操作时间 */
  lastActive: string
  /** 最后心跳时间（页面开着就更新） */
  lastHeartbeat: string
}

/** 在线用户列表（已登录且未注销的用户） */
export function listOnlineUsers() {
  return httpGet<OnlineUserDto[]>('/api/Common/UserOnline/List')
}

import { httpGet } from '@/api/request'

/** 在线用户记录 */
export interface OnlineUserDto {
  userId: string
  account: string
  displayName?: string | null
  /** 用户头像：data:image/...;base64 内联图片或 URL；为空时前端回退显示首字母。 */
  avatar?: string | null
  ip: string
  loginTime: string
  /** 最后真实操作时间 */
  lastActive: string
  /** 最后心跳时间（页面开着就更新） */
  lastHeartbeat: string
}

/** 在线用户列表（已登录且未注销的用户）。opts.silent：定时轮询不弹遮罩（页面自带表格 loading） */
export function listOnlineUsers(opts?: { silent?: boolean }) {
  return httpGet<OnlineUserDto[]>('/api/Common/UserOnline/List', undefined, undefined, opts)
}

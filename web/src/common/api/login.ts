import { httpGet, httpPost } from '@/api/request'

/** 登录校验返回：含 JWT 令牌与权限信息 */
export interface LoginVerifyResult {
  ok: boolean
  displayName?: string
  /** 头像（data:image/...;base64），用于顶栏展示 */
  avatar?: string | null
  /** JWT 令牌：后续请求以 Authorization: Bearer 携带 */
  token?: string
  /** 可见菜单权限码（菜单 Name），仅供前端参考，鉴权以后端为准 */
  menuCodes?: string[]
  /** 角色编码 */
  roles?: string[]
  /** 失败原因码：account_disabled / wrong_password / account_not_found */
  reason?: 'account_disabled' | 'wrong_password' | 'account_not_found'
  /** 会话超时时间（分钟）：0 表示不自动退出 */
  sessionTimeoutMinutes?: number
}

/** 登录默认账号密码（后端配置回填，对应后端 LoginController） */
export function getLoginDefault() {
  return httpGet<{ account: string; password: string }>('/api/Common/Login/GetLoginDefault')
}

/** 校验登录 */
export function verifyLogin(account: string, password: string) {
  return httpPost<LoginVerifyResult>('/api/Common/Login/VerifyLogin', { account, password })
}

/** 心跳检查：前端轮询当前登录账号是否仍处于启用状态 */
export function checkAuthStatus() {
  return httpGet<{ enabled: boolean }>('/api/Common/Login/CheckStatus')
}

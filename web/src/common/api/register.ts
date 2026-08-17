import { httpGet, httpPost } from '@/api/request'

/** 检查邮箱是否已被注册 */
export function checkEmailExists(email: string) {
  return httpGet<{ exists: boolean; valid: boolean }>('/api/Common/Register/CheckEmail', { email })
}

/** 发送注册验证码到邮箱 */
export function sendRegisterCode(email: string) {
  return httpPost<{ ok: boolean; msg: string }>('/api/Common/Register/SendCode', { email })
}

/** 完成注册（邮箱 + 验证码 + 密码） */
export function registerAccount(data: { email: string; code: string; password: string; displayName?: string }) {
  return httpPost<{ ok: boolean; msg: string }>('/api/Common/Register/Register', data)
}

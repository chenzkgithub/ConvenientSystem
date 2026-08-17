import { httpGet, httpPost } from '@/api/request'

/** 读取客户端配置（锁屏开关与空闲时长，对应后端 LockController） */
export function getAppConfig() {
  return httpGet<{ enableLock: boolean; lockTimeout: number }>('/api/Common/Lock/GetAppConfig')
}

/** 校验锁屏解锁密码 */
export function verifyUnlock(password: string) {
  return httpPost<{ ok: boolean }>('/api/Common/Lock/VerifyUnlock', { password })
}

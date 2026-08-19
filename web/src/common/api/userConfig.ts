import { httpGet, httpPut, httpPost } from '@/api/request'

/** 用户个人配置项（前端展示用） */
export interface UserConfigItem {
  configKey: string
  configValue: string
  displayName: string
  description: string | null
  inputType: string
  category: string
  sortOrder: number
}

/** 用户个人配置分组 */
export interface UserConfigGroup {
  category: string
  items: UserConfigItem[]
}

/** 批量保存项 */
export interface UserConfigSaveItem {
  configKey: string
  configValue: string
}

/** 获取当前用户的个人配置（合并全局默认值 + 用户覆盖值，按分组返回） */
export function getMyConfig() {
  return httpGet<UserConfigGroup[]>('/api/Common/UserConfig/GetMyConfig')
}

/** 批量更新当前用户配置 */
export function updateMyConfig(items: UserConfigSaveItem[]) {
  return httpPut('/api/Common/UserConfig/UpdateBatch', items)
}

/** 获取当前用户 UI 偏好键值字典（含默认值） */
export function getUIPrefs() {
  return httpGet<Record<string, string>>('/api/Common/UserConfig/GetUIPrefs')
}

// ========== 启动器条目 ==========

/** 启动器自定义条目 */
export interface LauncherEntry {
  title: string
  target: string
  /** url | file | command */
  kind: string
}

/** 获取当前用户的启动器条目（API 直接返回 JSON 数组，无需二次 JSON.parse） */
export async function getLauncherItems(): Promise<LauncherEntry[]> {
  const data = await httpGet<LauncherEntry[] | null>('/api/Common/UserConfig/GetLauncherItems')
  return Array.isArray(data) ? data : []
}

/** 保存当前用户的启动器条目 */
export function saveLauncherItems(items: LauncherEntry[]) {
  // 后端 [FromBody] string json 要求 body 是 JSON 字符串字面量，
  // 因此先 JSON.stringify(items) 得到数组字符串，再 JSON.stringify 一次得到字符串字面量，
  // 并显式声明 application/json，避免 axios 对 string body 使用 text/plain 导致 415。
  const payload = JSON.stringify(JSON.stringify(items))
  return httpPost('/api/Common/UserConfig/SaveLauncherItems', payload, undefined, undefined, {
    headers: { 'Content-Type': 'application/json' },
  })
}

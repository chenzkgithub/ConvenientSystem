import { httpGet, httpPut } from '@/api/request'

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

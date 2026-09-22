import { localGet, localPost } from '@/api/request'

/** 接口归属：local=全部走桌面端本地通道（审计脚本依据，勿删） */
export const API_SIDE = 'local' as const

/** 配置文件列表项 */
export interface ConfigFileInfo {
  name: string
  type: string
  size: string
  modified: string
}

/** 配置文件内容 */
export interface ConfigFileContent {
  name: string
  content: string
  modified: string
}

/** 服务状态 */
export interface ConfigEditorStatus {
  startTime: string
  restarting: boolean
  baseDirectory: string
}

/** 获取可编辑的配置文件列表 */
export function getConfigFileList() {
  return localGet<ConfigFileInfo[]>('/api/local/config-editor/List')
}

/** 读取指定配置文件的完整内容 */
export function readConfigFile(name: string) {
  return localGet<ConfigFileContent>('/api/local/config-editor/Read', { name })
}

/** 保存配置文件（自动备份旧版本） */
export function saveConfigFile(name: string, content: string) {
  return localPost<void>('/api/local/config-editor/Save', { name, content })
}

/** 触发服务重启 */
export function restartService() {
  return localPost<void>('/api/local/config-editor/Restart')
}

/** 获取服务状态 */
export function getConfigEditorStatus() {
  return localGet<ConfigEditorStatus>('/api/local/config-editor/Status')
}

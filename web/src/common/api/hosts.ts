import { localGet, localPost } from '@/api/request'

/** 接口归属：local=全部走桌面端本地通道（审计脚本依据，勿删） */
export const API_SIDE = 'local' as const

/** hosts 行类型 */
export type HostsLineKind = 'entry' | 'group' | 'other'

/** hosts 文件中的一行 */
export interface HostsLine {
  kind: HostsLineKind
  /** other 行：原文 */
  raw?: string
  /** entry：IP */
  ip?: string
  /** entry：域名列表 */
  domains?: string[]
  /** entry：是否启用（false = 整行被注释） */
  enabled?: boolean
  /** entry：行尾备注 */
  comment?: string
  /** group：分组名；entry：上方最近分组（展示用） */
  group?: string
}

/** hosts 文件完整视图 */
export interface HostsFile {
  path: string
  writable: boolean
  lines: HostsLine[]
  entryCount: number
  otherLineCount: number
}

/** 备份文件 */
export interface HostsBackup {
  name: string
  modified: string
  size: number
}

/** 读取并解析本机 hosts 文件 */
export function getHosts() {
  return localGet<HostsFile>('/api/local/hosts/List')
}

/** 整体写回 hosts（写入前自动备份，保留最近 20 份） */
export function saveHosts(lines: HostsLine[]) {
  return localPost<{ message: string; backup: string }>('/api/local/hosts/Save', { lines })
}

/** 备份列表（新的在前） */
export function getHostsBackups() {
  return localGet<HostsBackup[]>('/api/local/hosts/Backups')
}

/** 用指定备份还原 hosts（还原前自动备份当前版本） */
export function restoreHostsBackup(name: string) {
  return localPost<{ message: string; backup: string }>('/api/local/hosts/Restore', { name, modified: '', size: 0 })
}

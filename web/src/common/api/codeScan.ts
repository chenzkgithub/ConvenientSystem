import { localGet, localPost, localDelete } from '@/api/request'

/** 接口归属：local=全部走桌面端本地通道（审计脚本依据，勿删） */
export const API_SIDE = 'local' as const

/** 检测规则 */
export interface CodeScanRule {
  id: number
  name: string
  pattern: string
  severity: string
  fileGlob: string
  enabled: boolean
  description: string
}

/** 扫描结果 */
export interface CodeScanResult {
  id: number
  targetPath: string
  scanMode: string
  fileCount: number
  issueCount: number
  errorCount: number
  warningCount: number
  infoCount: number
  durationMs: number
  createTime: string
  issues?: CodeScanIssue[]
}

/** 具体问题 */
export interface CodeScanIssue {
  id: number
  ruleName: string
  severity: string
  filePath: string
  lineNumber: number
  lineContent: string
}

/** 获取所有检测规则 */
export function getScanRules() {
  return localGet<CodeScanRule[]>('/api/local/code-scan/ListRules')
}

/** 保存规则（新建/编辑） */
export function saveScanRule(rule: Partial<CodeScanRule>) {
  return localPost<void>('/api/local/code-scan/SaveRule', rule)
}

/** 删除规则 */
export function deleteScanRule(id: number) {
  return localDelete<void>('/api/local/code-scan/DeleteRule', { id })
}

/** 导入内置规则模板 */
export function importScanTemplates() {
  return localPost<void>('/api/local/code-scan/ImportTemplates')
}

/** 执行扫描 */
export function runCodeScan(targetPath: string, scanMode: 'full' | 'diff' = 'full') {
  return localPost<CodeScanResult>('/api/local/code-scan/Scan', { targetPath, scanMode }, undefined, undefined, { noLoading: true })
}

/** 获取扫描历史 */
export function getScanResults(limit = 20) {
  return localGet<CodeScanResult[]>('/api/local/code-scan/ListResults', { limit })
}

/** 获取扫描详情 */
export function getScanResult(id: number) {
  return localGet<CodeScanResult>('/api/local/code-scan/GetResult', { id })
}

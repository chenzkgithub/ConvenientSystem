import { httpPost } from '@/api/request'

/** 通用构建类型 */
export type UniversalBuildType = 'Web' | 'Node' | 'DotNet' | 'JavaMaven' | 'JavaGradle' | 'Installer'

/** 通用构建状态（Waiting = 并发已满，排队等待构建槽位） */
export type UniversalBuildStatus = 'Pending' | 'Waiting' | 'Running' | 'Success' | 'Failed' | 'Cancelled'

/** 环境检测信息 */
export interface UniversalEnvironmentInfo {
  type: string
  name: string
  installed: boolean
  version: string
  message: string
  downloadUrl: string
}

/** 通用构建任务 */
export interface UniversalBuildJobDto {
  id: string
  type: UniversalBuildType
  name: string
  status: UniversalBuildStatus
  projectDir: string
  outputDir: string
  progress: number
  /** 排队位置（仅 Waiting 时有值，按启动顺序 1 起） */
  queuePosition?: number | null
  log: string
  exitCode?: number
  startTime: string
  completedTime?: string
}

/** 通用构建请求 */
export interface UniversalBuildRequest {
  id?: string
  type: UniversalBuildType
  projectDir: string
  outputDir?: string
  name: string
  /** 构建前先执行 git pull --ff-only 拉取远端最新代码 */
  prePull?: boolean
}

/** 检测指定类型环境请求 */
export interface EnvironmentForTypeRequest {
  type: UniversalBuildType
}

/** 进度查询请求 */
export interface ProgressRequest {
  id: string
}

/** 取消任务请求 */
export interface CancelRequest {
  id: string
}

/** 默认输出目录请求 */
export interface DefaultOutputDirRequest {
  type: UniversalBuildType
  name: string
}

/** 检测全部环境 */
export function checkUniversalEnvironment() {
  return httpPost<UniversalEnvironmentInfo[]>('/api/Common/UniversalBuild/Environment', {})
}

/** 检测指定类型所需环境 */
export function checkUniversalEnvironmentForType(request: EnvironmentForTypeRequest) {
  return httpPost<UniversalEnvironmentInfo[]>('/api/Common/UniversalBuild/EnvironmentForType', request)
}

/** 启动构建任务 */
export function startUniversalBuild(request: UniversalBuildRequest) {
  return httpPost<UniversalBuildJobDto>('/api/Common/UniversalBuild/Build', request)
}

/** 获取任务进度 */
export function getUniversalBuildProgress(request: ProgressRequest) {
  return httpPost<UniversalBuildJobDto | null>('/api/Common/UniversalBuild/Progress', request)
}

/** 获取所有任务 */
export function getUniversalBuildAllJobs() {
  return httpPost<UniversalBuildJobDto[]>('/api/Common/UniversalBuild/AllJobs', {})
}

/** 取消任务 */
export function cancelUniversalBuild(request: CancelRequest) {
  return httpPost('/api/Common/UniversalBuild/Cancel', request)
}

/** 获取默认输出目录 */
export function getUniversalDefaultOutputDir(request: DefaultOutputDirRequest) {
  return httpPost<string>('/api/Common/UniversalBuild/DefaultOutputDir', request)
}

// ============================ 部署 API ============================

/** 部署目标操作系统 */
export type DeployTargetOS = 'Linux' | 'Windows'

/** 部署状态（Cancelled = 用户取消，后端已自动还原部署前环境） */
export type DeployStatus = 'Running' | 'Success' | 'Failed' | 'Cancelled'

/** 部署任务 DTO */
export interface DeployJobDto {
  id: string
  buildName: string
  buildType: UniversalBuildType
  targetOS: DeployTargetOS
  siteName: string
  status: DeployStatus
  startTime: string
  completedTime?: string
  /** 部署整体进度（0-100），上传段为字节级真实进度 */
  progress?: number
  log: string
}

/** 站点存在性检查请求 */
export interface CheckSiteExistsRequest {
  targetOS: DeployTargetOS
  siteName: string
  serviceName: string
  host: string
  userName: string
  password: string
  buildType: UniversalBuildType
}

/** 站点存在性检查结果 */
export interface SiteExistsResult {
  exists: boolean
  message: string
}

/** 部署请求 */
export interface DeployRequest {
  outputDir: string
  buildName: string
  buildType: UniversalBuildType
  /** Docker 服务名（用户输入，留空自动推断） */
  serviceName?: string
  /** 远程目标目录（用户输入，留空自动推断） */
  remoteDir?: string
  /** 压缩包名称（留空则用 {站点名}-{服务名}.tar.gz，每次覆盖） */
  archiveName?: string
  targetOS: DeployTargetOS
  siteName?: string
  host: string
  userName: string
  password: string
  deployPath?: string
  verifyHealth?: boolean
  keepDatabase?: boolean
}

/** 部署进度查询请求 */
export interface DeployProgressRequest {
  id: string
}

/** 部署取消请求 */
export interface DeployCancelRequest {
  id: string
}

/** 部署取消结果 */
export interface DeployCancelResult {
  message: string
}

/** 启动部署任务 */
export function startDeploy(request: DeployRequest) {
  return httpPost<DeployJobDto>('/api/Common/UniversalBuild/Deploy', request)
}

/** 获取部署进度 */
export function getDeployProgress(request: DeployProgressRequest) {
  return httpPost<DeployJobDto | null>('/api/Common/UniversalBuild/DeployProgress', request)
}

/** 取消部署：中断执行并自动还原部署前环境 */
export function cancelDeploy(request: DeployCancelRequest) {
  return httpPost<DeployCancelResult>('/api/Common/UniversalBuild/DeployCancel', request)
}

/** 手动回滚请求：把最近一次部署的 .old 备份换回正式目录 */
export interface RollbackRequest {
  buildName: string
  buildType: UniversalBuildType
  targetOS: DeployTargetOS
  siteName?: string
  host: string
  userName: string
  password: string
  /** 远程部署根路径（与部署时一致，留空用默认） */
  deployPath?: string
  /** 服务名（留空按构建类型推断） */
  serviceName?: string
  /** 远程目标目录（留空按构建类型推断） */
  remoteDir?: string
  /** 回滚后是否执行健康检查（失败仅告警） */
  verifyHealth?: boolean
}

/** 启动手动回滚（复用部署任务机制，进度/日志/取消同部署） */
export function startRollback(request: RollbackRequest) {
  return httpPost<DeployJobDto>('/api/Common/UniversalBuild/Rollback', request)
}

/** 弹出文件夹选择对话框，返回用户选择的目录路径 */
export function selectFolder() {
  return httpPost<string | null>('/api/Common/UniversalBuild/SelectFolder', {})
}

/** 在资源管理器中打开构建输出目录 */
export function openOutputFolder(path: string) {
  return httpPost(`/api/Common/UniversalBuild/OpenFolder?path=${encodeURIComponent(path)}`, {})
}

/** 检查远程站点/服务是否已存在 */
export function checkSiteExists(request: CheckSiteExistsRequest) {
  return httpPost<SiteExistsResult>('/api/Common/UniversalBuild/CheckSiteExists', request)
}

// ============================ 部署历史 / 定时构建 ============================

/** 部署历史记录 */
export interface DeployHistoryItem {
  buildName: string
  buildType: UniversalBuildType
  targetOS: DeployTargetOS
  siteName: string
  host: string
  status: DeployStatus
  startTime: string
  completedTime?: string
  durationSeconds: number
}

/** 获取部署历史（最近 100 条，按时间倒序） */
export function getDeployHistory() {
  return httpPost<DeployHistoryItem[]>('/api/Common/UniversalBuild/DeployHistory', {})
}

/** 定时构建配置 */
export interface ScheduleItem {
  /** 留空表示新增，有值表示更新 */
  id?: string
  /** 关联卡片 id（前端生成，用于界面关联展示） */
  cardId: string
  name: string
  type: UniversalBuildType
  projectDir: string
  outputDir: string
  /** 触发间隔（分钟） */
  intervalMinutes: number
  enabled: boolean
  lastRunAt?: string
  nextRunAt: string
  lastJobId?: string
  lastError?: string
}

/** 查询定时构建列表 */
export function getScheduleList() {
  return httpPost<ScheduleItem[]>('/api/Common/UniversalBuild/ScheduleList', {})
}

/** 新增/更新定时构建 */
export function setSchedule(item: ScheduleItem) {
  return httpPost<ScheduleItem>('/api/Common/UniversalBuild/ScheduleSet', item)
}

/** 删除定时构建 */
export function removeSchedule(id: string) {
  return httpPost(`/api/Common/UniversalBuild/ScheduleRemove?id=${encodeURIComponent(id)}`, {})
}

// ============================ SSH 凭据（DPAPI 加密存储） ============================

/** SSH 凭据保存请求 */
export interface SshCredentialRequest {
  host: string
  userName: string
  password: string
}

/** SSH 凭据查询结果（未保存过或解密失败时为 null） */
export interface SshCredentialResult {
  password: string | null
}

/** 保存 SSH 凭据（DPAPI 加密后落盘本机，供下次部署与自动部署复用） */
export function saveSshCredential(request: SshCredentialRequest) {
  return httpPost('/api/Common/UniversalBuild/SaveSshCredential', request)
}

/** 读取已保存的 SSH 密码（本机接口，供部署弹窗回填） */
export function getSshCredential(host: string, userName: string) {
  return httpPost<SshCredentialResult>(`/api/Common/UniversalBuild/GetSshCredential?host=${encodeURIComponent(host)}&userName=${encodeURIComponent(userName)}`, {})
}

/** 删除已保存的 SSH 凭据 */
export function removeSshCredential(host: string, userName: string) {
  return httpPost(`/api/Common/UniversalBuild/RemoveSshCredential?host=${encodeURIComponent(host)}&userName=${encodeURIComponent(userName)}`, {})
}
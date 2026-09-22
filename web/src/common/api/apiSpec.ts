import { httpGet, httpPost, localGet, localPost } from '@/api/request'
import type { AsyncTaskDto } from './asyncTask'

/** 接口归属：mixed=ApiSpec 扫描/生成/调试走桌面端本地通道，Apifox 导入删除走服务器（审计脚本依据，勿删） */
export const API_SIDE = 'mixed' as const

/** 导出格式卡片 */
export interface ApiSpecFormatDto {
  format: string
  displayName: string
  fileExtension: string
  contentType: string
  description: string
}

/** 扫描到的 Controller 文件 */
export interface ApiSpecFileDto {
  path: string
  controllerName: string
  endpointCount: number
}

/** 解决方案扫描出的接口条目（接口级清单，勾选后生成文档） */
export interface ApiSpecSolutionEndpointDto {
  file: string
  selectionKey: string
  group: string
  /** Controller 所在命名空间（已剥尾部 .Controllers；空串归“未分组”），列表按命名空间二级分组 */
  namespace?: string | null
  method: string
  path: string
  actionName: string
  summary?: string | null
  permission?: string | null
}

/** 接口参数 */
export interface ApiSpecParamDto {
  in: string
  name: string
  typeText: string
  required: boolean
  description?: string | null
}

/** 解析出的接口 */
export interface ApiSpecEndpointDto {
  method: string
  actionName: string
  path: string
  summary?: string | null
  permission?: string | null
  group: string
  selectionKey: string
  params: ApiSpecParamDto[]
  responseType: string
}

/** DTO 字段 */
export interface ApiSpecFieldDto {
  name: string
  typeText: string
  required: boolean
  description?: string | null
}

/** DTO 类型定义（对象或枚举） */
export interface ApiSpecTypeDto {
  name: string
  comment?: string | null
  isEnum: boolean
  enumValues: string[]
  fields: ApiSpecFieldDto[]
}

/** 解析文档（IR） */
export interface ApiSpecDocumentDto {
  title: string
  version: string
  baseUrl: string
  endpoints: ApiSpecEndpointDto[]
  types: Record<string, ApiSpecTypeDto>
  warnings: string[]
}

/** 导出/预览结果 */
export interface ApiSpecExportDto {
  fileName: string
  contentType: string
  content: string
  warnings: string[]
}

/** 当前用户的 Apifox Access Token 保存状态；令牌内容不会返回。 */
export interface ApifoxAccessTokenStatusDto {
  tokenConfigured: boolean
}

/** 保存或清除当前用户的 Apifox Access Token。 */
export interface ApifoxAccessTokenSaveRequest {
  accessToken?: string
  clearAccessToken: boolean
}

/** 本次导入的 Apifox 目标与重复接口处理策略。 */
export interface ApifoxImportRequest {
  content: string
  projectId: string
  targetEndpointFolderId: number | null
  targetBranchId: number | null
  endpointOverwriteBehavior: string
}

/** 批量删除 Apifox 项目接口的请求；目录 ID 可选，填写后只删该目录（含子目录）下的接口。 */
export interface ApifoxDeleteRequest {
  projectId: string
  folderId: number | null
}

/** Apifox 导入结果摘要。 */
export interface ApifoxImportResultDto {
  endpointCreated: number
  endpointUpdated: number
  endpointFailed: number
  endpointIgnored: number
  schemaCreated: number
  schemaUpdated: number
  schemaFailed: number
  schemaIgnored: number
  errors: string[]
}

/** Apifox 批量删除结果摘要。 */
export interface ApifoxDeleteResultDto {
  deleted: number
  failed: number
  errors: string[]
  foldersRemoved: number
}

/** 调试请求（服务端 HttpClient 代理转发到目标地址，规避浏览器 CORS）。 */
export interface ApiDebugRequest {
  method: string
  url: string
  headers?: Record<string, string>
  body?: string
  timeoutMs?: number
}

/** 调试响应：目标服务原始响应；网络层失败时 error 有值、statusCode 为 0。 */
export interface ApiDebugResponse {
  statusCode: number
  statusText: string
  responseHeaders: Record<string, string>
  body: string
  elapsedMs: number
  error?: string | null
}

/** 长耗时同步接口超时（ms）：首次扫描有 Roslyn JIT 冷启动，10s 全局超时不够 */
const SPEC_TIMEOUT = 120_000

/** 支持的导出格式列表 */
export function getApiSpecFormats() {
  return localGet<ApiSpecFormatDto[]>('/api/local/api-spec/Formats')
}

/** 扫描目录下的 Controller 文件 */
export function scanApiSpecControllers(rootDir: string) {
  return localGet<ApiSpecFileDto[]>('/api/local/api-spec/Controllers', { rootDir }, SPEC_TIMEOUT)
}

/** 选择解决方案文件（桌面端原生对话框；取消返回 path 为 null，超时 10 分钟等用户选择） */
export function pickApiSpecSolution() {
  return localGet<{ path: string | null }>('/api/local/api-spec/PickSolution', undefined, 10 * 60 * 1000, { silent: true })
}

/** 扫描任务的完成结果（AsyncTaskDto.result 载体）。 */
export interface ApiSpecScanTaskResult {
  /** 扫描出的接口清单（含命名空间，前端两级分组展示） */
  endpoints: ApiSpecSolutionEndpointDto[]
}

/** 生成任务的完成结果（AsyncTaskDto.result 载体）。 */
export interface ApiSpecGenerateTaskResult {
  /** 未筛选的完整解析文档（IR；类型树/警告展示用） */
  document: ApiSpecDocumentDto
  /** 按选择标识筛选后的导出内容 */
  export: ApiSpecExportDto
}

/** 启动解决方案扫描后台任务：返回任务初始快照（AsyncTaskDto），进度经 asyncTask store 统一跟踪。 */
export function startApiSpecScan(solutionPath: string) {
  return localPost<AsyncTaskDto>('/api/local/api-spec/StartScan', { solutionPath })
}

/** 启动解析并生成后台任务：一次解析同时产出 IR 文档与导出内容（替代旧 Parse+Preview 双请求）。 */
export function startApiSpecGenerate(request: {
  rootDir: string
  files: string
  format: string
  title?: string
  baseUrl?: string
  solutionPath?: string
  selectionKeys?: string[]
}) {
  return localPost<AsyncTaskDto>('/api/local/api-spec/StartGenerate', request)
}

/** 复用已完成生成任务的解析结果重新导出（换格式/标题不重新解析源码）。
 * silent：任务过期等失败由调用方静默回退完整生成，不弹错误提示。 */
export function reExportApiSpec(request: { taskId: string; format: string; title?: string; baseUrl?: string }) {
  return localPost<ApiSpecExportDto>('/api/local/api-spec/ReExport', request, undefined, undefined, { silent: true })
}

/** 读取当前用户的 Apifox Access Token 保存状态。 */
export function getApifoxAccessTokenStatus() {
  return httpGet<ApifoxAccessTokenStatusDto>('/api/Common/ApifoxConfig/GetMyAccessTokenStatus')
}

/** 保存或清除当前用户的 Apifox Access Token。 */
export function saveApifoxAccessToken(request: ApifoxAccessTokenSaveRequest) {
  return httpPut('/api/Common/ApifoxConfig/SaveMyAccessToken', request)
}

/** 启动 OpenAPI 导入任务：服务端按 tag（命名空间/Controller）拆批后台执行，返回任务初始快照。
 * 注意：启动请求体携带整份 OpenAPI 文档（大解决方案可达数 MB），桌面代理转发到云耗时可能超过
 * 默认 10s 请求超时，故放宽到 120s（超时表现为「无法连接到后端服务」误报，实际云端已建任务）。
 * 导入过程本身分批后台执行，不占本请求时长。 */
export function startApifoxImport(request: ApifoxImportRequest) {
  return httpPost<AsyncTaskDto>('/api/Common/ApifoxConfig/StartImportOpenApi', request, undefined, 120_000)
}

/** 启动批量删除任务：删除项目全部接口，或指定目录（含子目录）下的接口。 */
export function startApifoxDelete(request: ApifoxDeleteRequest) {
  return httpPost<AsyncTaskDto>('/api/Common/ApifoxConfig/DeleteEndpoints', request)
}

/** 强制终止当前用户指定类型（apifox-import/apifox-delete）的进行中任务：
 * 上次启动请求超时但实际仍在后台执行时，解锁被卡住的同类型操作。返回 { cancelled }。 */
export function cancelApifoxRunningTask(kind: 'apifox-import' | 'apifox-delete') {
  return httpPost<{ cancelled: boolean }>('/api/Common/ApifoxConfig/CancelRunningTask', { kind })
}

/** 发送一次接口调试请求（POST body，服务端代理转发；超时给目标超时留 15s 代理余量）。 */
export function sendApiDebug(request: ApiDebugRequest) {
  return localPost<ApiDebugResponse>('/api/local/api-spec/Debug', request, undefined, (request.timeoutMs ?? 30_000) + 15_000)
}

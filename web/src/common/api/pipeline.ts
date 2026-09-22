import { localPost } from '@/api/request'
import type { DeployTargetOS, UniversalBuildType } from '@/common/api/universalBuild'

/** 接口归属：local=全部走桌面端本地通道（审计脚本依据，勿删） */
export const API_SIDE = 'local' as const

/** 流水线阶段类型 */
export type PipelineStageType = 'Build' | 'Deploy' | 'Sql'

/** 流水线阶段配置（Build/Deploy 字段扁平共存，按 type 渲染对应表单） */
export interface PipelineStage {
  id?: string
  name: string
  type: PipelineStageType

  // —— 构建阶段 ——
  buildType?: UniversalBuildType
  /** 项目目录 */
  projectDir?: string
  /** 输出目录（空 = 自动推断） */
  outputDir?: string
  /** 输出目录是否由用户手动指定；false = 跟随阶段名/构建类型自动生成 */
  outputDirCustom?: boolean
  /** 构建前 git pull */
  prePull?: boolean

  // —— 部署阶段 ——
  /** 部署产物目录（空 = 取流水线上一个构建阶段的产物目录） */
  explicitOutputDir?: string
  /** 部署关联构建类型（用于默认服务名/远程目录推断） */
  deployBuildType?: UniversalBuildType
  serviceName?: string
  remoteDir?: string
  archiveName?: string
  targetOS?: DeployTargetOS
  siteName?: string
  host?: string
  userName?: string
  deployPath?: string
  verifyHealth?: boolean
  keepDatabase?: boolean

  // —— 数据库脚本阶段 ——
  /** SQL 文件或目录（空 = 上一个构建阶段的产物目录，执行其中全部 .sql） */
  sqlSource?: string
  /** 目标数据库类型（SqlServer/MySql/PostgreSQL/Sqlite/Oracle） */
  dbType?: string
  /** 目标库连接串（明文存本机 pipelines.json） */
  connectionString?: string
  /** 是否用事务包裹每个文件的执行 */
  useTransaction?: boolean
}

/** 流水线定义 */
export interface PipelineDefinition {
  /** 留空表示新增，有值表示更新 */
  id?: string
  name: string
  createTime?: string
  updateTime?: string
  stages: PipelineStage[]
}

/** 流水线整体运行状态 */
export type PipelineRunStatus = 'Running' | 'Success' | 'Failed' | 'Cancelled'

/** 单阶段运行状态（Skipped = 前置失败/取消未执行） */
export type PipelineStageRunStatus = 'Pending' | 'Running' | 'Success' | 'Failed' | 'Skipped'

/** 单阶段运行结果 */
export interface PipelineRunStage {
  stageId: string
  name: string
  status: PipelineStageRunStatus
  /** 关联构建/部署任务 id */
  jobId?: string | null
  startTime?: string
  completedTime?: string
  /** 阶段进度（0-100，运行中实时刷新；成功结束置 100） */
  progress?: number
  /** 当前步骤文本（运行中显示，如 "[3/7] SFTP 上传到服务器"） */
  stepText?: string
  /** 失败原因或结果摘要 */
  message?: string
}

/** 一次流水线运行 */
export interface PipelineRun {
  id: string
  pipelineId: string
  pipelineName: string
  status: PipelineRunStatus
  startTime: string
  completedTime?: string
  stages: PipelineRunStage[]
  trigger?: string
  /** 汇总日志（历史记录重启后为空） */
  log?: string
}

/** 流水线定义列表 */
export function getPipelineList() {
  return localPost<PipelineDefinition[]>('/api/local/pipeline/List', {})
}

/** 新增/更新流水线定义 */
export function savePipeline(def: PipelineDefinition) {
  return localPost<PipelineDefinition>('/api/local/pipeline/Save', def)
}

/** 删除流水线定义 */
export function removePipeline(id: string) {
  return localPost(`/api/local/pipeline/Remove?id=${encodeURIComponent(id)}`, {})
}

/** 启动一次流水线运行 */
export function startPipelineRun(pipelineId: string) {
  return localPost<PipelineRun>('/api/local/pipeline/Start', { pipelineId })
}

/** 查询单次运行详情（含汇总日志） */
export function getPipelineRun(id: string) {
  return localPost<PipelineRun | null>(`/api/local/pipeline/Run?id=${encodeURIComponent(id)}`, {})
}

/** 查询运行历史（按开始时间倒序） */
export function getPipelineRuns(pipelineId?: string, limit = 30) {
  const q = pipelineId ? `?pipelineId=${encodeURIComponent(pipelineId)}&limit=${limit}` : `?limit=${limit}`
  return localPost<PipelineRun[]>(`/api/local/pipeline/Runs${q}`, {})
}

/** 取消运行中的流水线（部署阶段取消会自动还原部署前环境） */
export function cancelPipelineRun(runId: string) {
  return localPost<{ message: string }>('/api/local/pipeline/CancelRun', { runId })
}

// ============================ 数据库阶段：连接测试 ============================

/** 数据库连接测试结果（不执行脚本，仅探活并取服务版本） */
export interface PipelineConnectionTestResult {
  success: boolean
  /** 成功为“连接成功”，失败为数据库返回的错误原文（或超时提示） */
  message: string
  serverVersion: string
  elapsedMs: number
}

/** 测试数据库连接串（数据库阶段“测试连接”按钮，结果内联展示；不弹全局遮罩，超时放宽到 30 秒） */
export function testPipelineConnection(request: { dbType: string; connectionString: string }) {
  return localPost<PipelineConnectionTestResult>('/api/local/pipeline/TestConnection', request, undefined, 30 * 1000, { noLoading: true })
}

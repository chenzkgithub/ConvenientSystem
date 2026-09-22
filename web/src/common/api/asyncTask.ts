import { httpGet, localGet } from '@/api/request'

/** 接口归属：mixed=本地任务走桌面端通道，Apifox 云端任务走服务器（审计脚本依据，勿删） */
export const API_SIDE = 'mixed' as const

/** 统一异步任务进度（后端 AsyncTaskDto）：解决方案扫描/解析生成/Apifox 导入删除等后台任务的公共载体 */
export interface AsyncTaskDto {
  /** 任务 ID：轮询进度 / 复用结果的标识 */
  taskId: string
  /** 任务类型：apispec-scan / apispec-generate / apifox-import / apifox-delete（并发拒绝按类型判定） */
  kind: string
  /** 展示标题（如"解决方案扫描"） */
  title: string
  /** running / succeeded / failed */
  status: 'running' | 'succeeded' | 'failed'
  /** 业务阶段（indexing/scanning/exporting/listing/deleting 等），完成或失败后为空 */
  phase: string
  /** 当前步骤描述 */
  current: string
  /** 总数（文件数/批次数/接口数）；阶段开始前为 0（显示不确定进度） */
  total: number
  /** 已完成数 */
  completed: number
  /** 失败数（部分失败不中断任务，如删除单个接口失败） */
  failed: number
  /** 任务失败原因（status=failed 时） */
  error: string | null
  /** 终态摘要（如“已生成 1234 个接口的文档”）：轮询/推送不带 result（大负载），完成通知与任务卡片展示用 */
  summary: string
  /** 任务开始时间（UTC） */
  startedAt: string
  /** 任务结束时间（UTC；运行中为 null）。完成后服务端保留 30 分钟 */
  finishedAt: string | null
  /** 任务结果（结构随 kind 变化：扫描接口清单 / 文档+导出 / Apifox 聚合结果），运行中为 null */
  result: unknown
}

/** 查询单个任务进度（本地任务端点：ApiSpec 扫描/生成等。桌面模式经白名单走本机进程，浏览器模式直连云端） */
export function getAsyncTask(taskId: string) {
  return localGet<AsyncTaskDto>('/api/local/async-task/Get', { taskId }, undefined, { silent: true })
}

/** 全部未清理任务（运行中 + 完成未超 30 分钟），页面刷新后恢复任务面板用 */
export function getAllAsyncTasks() {
  return localGet<AsyncTaskDto[]>('/api/local/async-task/GetAll', undefined, undefined, { silent: true })
}

/** 读取已完成任务的完整 result（可达数 MB：轮询/推送只发轻量快照，页面在任务终态后按需拉取）；
 *  本地任务端点（ApiSpec 扫描/生成等），超时放宽应对大响应体传输与解析 */
export function getAsyncResult(taskId: string) {
  return localGet<AsyncTaskDto>('/api/local/async-task/GetResult', { taskId }, 120_000, { silent: true })
}

/** 查询单个 Apifox 云端任务进度（导入/删除在云端执行；桌面代理模式下经 ApifoxConfig 路径转发到云） */
export function getApifoxTaskProgress(taskId: string) {
  return httpGet<AsyncTaskDto>('/api/Common/ApifoxConfig/GetTaskProgress', { taskId }, undefined, { silent: true })
}

/** 读取已完成 Apifox 云端任务的完整 result（轮询快照不携带，终态后按需拉取；桌面代理模式下同路径转发到云） */
export function getApifoxTaskResult(taskId: string) {
  return httpGet<AsyncTaskDto>('/api/Common/ApifoxConfig/GetTaskResult', { taskId }, 120_000, { silent: true })
}

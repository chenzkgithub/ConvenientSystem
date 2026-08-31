import { httpPost } from '@/api/request'

/** 本地命令超时：仓库多时逐个查询状态较慢（与后端 LocalTimeoutMs 60s 对齐留余量） */
const LOCAL_TIMEOUT_MS = 30_000
/** 扫描子目录发现仓库：每个子仓库逐个查状态 */
const DISCOVER_TIMEOUT_MS = 60_000
/** 网络命令超时：pull/push 慢仓库（如首次克隆后的大 pull）需留足，后端限 180s */
const NETWORK_TIMEOUT_MS = 200_000
/** 克隆超时：大仓库下载耗时长，后端限 600s，前端略放宽余量 */
const CLONE_TIMEOUT_MS = 620_000

/** 仓库状态总览 */
export interface GitRepoStatus {
  /** 仓库绝对路径（唯一标识） */
  path: string
  name: string
  /** 目录有效且是 Git 仓库 */
  isRepo: boolean
  /** 当前分支（游离 HEAD 显示 "HEAD（游离）"，空仓库为空） */
  branch: string
  /** HEAD 短哈希 */
  shortHash: string
  /** 领先远程提交数 */
  ahead: number
  /** 落后远程提交数 */
  behind: number
  /** 工作区改动文件数（含未跟踪） */
  changes: number
  /** 最近一次提交（短哈希 + 说明） */
  lastCommit: string
  /** 远程名（如 origin，无远程为空） */
  remote: string
  /** 状态不可用原因（目录不存在等，正常为空） */
  message: string
}

/** 分支条目 */
export interface GitBranch {
  name: string
  /** 是否当前分支 */
  isCurrent: boolean
  /** 是否远程分支（origin/xxx） */
  isRemote: boolean
}

/** Git 命令执行结果 */
export interface GitCommandResult {
  success: boolean
  /** 合并后的输出（stdout + stderr） */
  output: string
  exitCode: number
}

/** 子目录扫描发现的仓库 */
export interface GitDiscoveredRepo {
  path: string
  name: string
  branch: string
}

/** 添加仓库结果 */
export interface GitAddRepoResult {
  ok: boolean
  message: string
  /** 添加成功时的仓库状态（可直接展示） */
  status?: GitRepoStatus
}

/** 路径请求（仓库列表增删 / 状态 / 分支 / 拉取 / 推送） */
export interface GitPathRequest {
  path: string
  /** 操作 ID（前端生成，用于取消；拉取/推送等可取消操作传） */
  opId?: string
}

/** 合并请求 */
export interface GitMergeRequest {
  path: string
  /** 合并来源分支（合入当前分支） */
  sourceBranch: string
  /** 操作 ID（用于取消） */
  opId?: string
}

/** 切换/新建分支请求 */
export interface GitCheckoutRequest {
  path: string
  /** 切换目标分支（已有分支） */
  branch: string
  /** 新建分支名（提供时以 branch 为起点创建并切换） */
  newBranch: string
  /** 操作 ID（用于取消） */
  opId?: string
}

/** 克隆请求 */
export interface GitCloneRequest {
  /** 远程仓库地址（https / ssh 均可） */
  url: string
  /** 保存位置（父目录，需已存在） */
  parentDir: string
  /** 克隆后的目录名（空则从 URL 推断） */
  dirName: string
  /** 操作 ID（用于取消） */
  opId?: string
}

/** 白名单执行请求 */
export interface GitExecRequest {
  path: string
  /** 完整命令（必须以 git 开头，如 git log --oneline -10） */
  command: string
  /** 操作 ID（用于取消） */
  opId?: string
}

/** 取消请求：opId 对应运行中的操作进程 */
export interface GitCancelRequest {
  opId: string
}

/** 取消结果 */
export interface GitCancelResult {
  /** 是否找到并杀掉了运行中操作 */
  cancelled: boolean
  message: string
}

/** 合并中间状态（合并进行中横幅 + 一键放弃） */
export interface GitMergeState {
  inProgress: boolean
  /** 来源分支（解析不出为空） */
  sourceBranch: string
  /** 冲突文件数（无冲突为 0） */
  conflicts: number
}

/** 提交历史条目 */
export interface GitLogEntry {
  hash: string
  shortHash: string
  /** 父提交哈希列表（首个为 first parent，画分支线用） */
  parents: string[]
  author: string
  /** 提交时间（yyyy-MM-dd HH:mm） */
  date: string
  subject: string
  /** 指向此提交的引用装饰（HEAD -> main、origin/main、tag: v1.0 等） */
  refs: string[]
}

/** 提交历史请求（分页） */
export interface GitLogRequest {
  path: string
  /** 筛选分支（空 = 当前 HEAD 全部历史） */
  branch?: string
  skip?: number
  take?: number
}

/** 提交变更文件（含状态） */
export interface GitCommitFile {
  /** 状态字母（M/A/D/R/C） */
  status: string
  path: string
  /** 重命名/复制的旧路径（其余状态为空） */
  oldPath?: string
}

/** 按文件切好的 diff 文本 */
export interface GitDiffFile {
  path: string
  diff: string
}

/** 单提交详情 */
export interface GitCommitDetail {
  commit: GitLogEntry
  files: GitCommitFile[]
  diffs: GitDiffFile[]
}

/** 工作区改动文件 */
export interface GitChangeFile {
  /** 状态字母（M/A/D/R/U/“?”未跟踪） */
  status: string
  path: string
  /** 重命名旧路径（其余状态为空） */
  oldPath?: string
  isUntracked: boolean
  isConflict: boolean
}

/** 工作区改动分组 */
export interface GitChanges {
  staged: GitChangeFile[]
  unstaged: GitChangeFile[]
  mergeState: GitMergeState
}

/** 单文件 diff 预览 */
export interface GitFileDiff {
  path: string
  /** unified diff 文本（未跟踪为内容合成的 + 行，二进制为占位提示） */
  diff: string
  deleted: boolean
  binary: boolean
}

/** 仓库列表（附带实时状态） */
export function getGitRepos() {
  return httpPost<GitRepoStatus[]>('/api/Common/Git/Repos', {}, undefined, LOCAL_TIMEOUT_MS)
}

/** 添加仓库（自动解析仓库根目录，子目录自动归属） */
export function addGitRepo(request: GitPathRequest) {
  return httpPost<GitAddRepoResult>('/api/Common/Git/AddRepo', request)
}

/** 移除仓库（仅移除列表记录，不碰磁盘） */
export function removeGitRepo(request: GitPathRequest) {
  return httpPost('/api/Common/Git/RemoveRepo', request)
}

/** 扫描目录一级子目录，发现其中的 Git 仓库 */
export function discoverGitRepos(request: GitPathRequest) {
  return httpPost<GitDiscoveredRepo[]>('/api/Common/Git/Discover', request, undefined, DISCOVER_TIMEOUT_MS)
}

/** 查询仓库状态总览 */
export function getGitStatus(request: GitPathRequest) {
  return httpPost<GitRepoStatus>('/api/Common/Git/Status', request)
}

/** 分支列表（本地在前、远程在后） */
export function getGitBranches(request: GitPathRequest) {
  return httpPost<GitBranch[]>('/api/Common/Git/Branches', request)
}

/** 网络命令统一 silent：进行中需要界面可交互（点取消按钮），
 *  不用全局 loading 遮罩，由调用方按钮 loading + 日志反馈，错误提示也由调用方负责。 */

/** 拉取当前分支（无上游时自动回退 origin {branch}） */
export function gitPull(request: GitPathRequest) {
  return httpPost<GitCommandResult>('/api/Common/Git/Pull', request, undefined, NETWORK_TIMEOUT_MS, { silent: true })
}

/** 推送当前分支（无上游时自动建立跟踪） */
export function gitPush(request: GitPathRequest) {
  return httpPost<GitCommandResult>('/api/Common/Git/Push', request, undefined, NETWORK_TIMEOUT_MS, { silent: true })
}

/** 合并来源分支到当前分支 */
export function gitMerge(request: GitMergeRequest) {
  return httpPost<GitCommandResult>('/api/Common/Git/Merge', request, undefined, NETWORK_TIMEOUT_MS, { silent: true })
}

/** 切换/新建分支 */
export function gitCheckout(request: GitCheckoutRequest) {
  return httpPost<GitCommandResult>('/api/Common/Git/Checkout', request, undefined, NETWORK_TIMEOUT_MS, { silent: true })
}

/** 白名单执行 git 命令（必须以 git 开头，参数直传不经 shell） */
export function gitExec(request: GitExecRequest) {
  return httpPost<GitCommandResult>('/api/Common/Git/Exec', request, undefined, NETWORK_TIMEOUT_MS, { silent: true })
}

/** 克隆远程仓库，成功后自动添加到仓库列表。
 *  silent：长耗时操作（大仓库最长 10 分钟），不用全局 loading 遮罩盖界面，
 *  由调用方按钮 loading + 日志自行反馈，错误提示也由调用方负责。 */
export function gitClone(request: GitCloneRequest) {
  return httpPost<GitCommandResult>('/api/Common/Git/Clone', request, undefined, CLONE_TIMEOUT_MS, { silent: true })
}

/** 取消运行中操作：杀对应 git 进程树，原请求随 WaitForExit 返回。
 *  走普通模式（很快，失败时全局提示合理；此刻可取消操作本身是 silent 的，不冲突）。 */
export function gitCancel(request: GitCancelRequest) {
  return httpPost<GitCancelResult>('/api/Common/Git/Cancel', request)
}

/** 合并中间状态（合并进行中横幅 + 一键放弃） */
export function getGitMergeState(request: GitPathRequest) {
  return httpPost<GitMergeState>('/api/Common/Git/MergeState', request)
}

/** 提交历史（新→旧，含父提交与 refs，画分支线用）；分页滚动加载 */
export function getGitLog(request: GitLogRequest) {
  return httpPost<GitLogEntry[]>('/api/Common/Git/Log', request, undefined, LOCAL_TIMEOUT_MS)
}

/** 单提交详情：元信息 + 变更文件 + 按文件切分的 diff */
export function getGitCommitDetail(request: { path: string; hash: string }) {
  return httpPost<GitCommitDetail>('/api/Common/Git/Commit', request, undefined, LOCAL_TIMEOUT_MS)
}

/** 工作区改动列表（已暂存/未暂存两组，含未跟踪与冲突） */
export function getGitChanges(request: GitPathRequest) {
  return httpPost<GitChanges>('/api/Common/Git/Changes', request, undefined, LOCAL_TIMEOUT_MS)
}

/** 暂存/取消暂存（单文件或全部，本地快命令不可取消） */
export function gitStage(request: { path: string; filePath?: string | null; stage: boolean }) {
  return httpPost<GitCommandResult>('/api/Common/Git/Stage', request)
}

/** 提交已暂存改动（可选顺带推送；路由避开历史功能的 Commit 详情端点） */
export function gitCommitChanges(request: { path: string; message: string; push: boolean; opId?: string }) {
  return httpPost<GitCommandResult>('/api/Common/Git/CommitChanges', request, undefined, NETWORK_TIMEOUT_MS, { silent: true })
}

/** 放弃改动（不可恢复，前端二次确认） */
export function gitDiscard(request: { path: string; filePath?: string | null; includeUntracked: boolean }) {
  return httpPost<GitCommandResult>('/api/Common/Git/Discard', request)
}

/** 单文件 diff 预览（已暂存/工作区，未跟踪合成 + 行） */
export function getGitFileDiff(request: { path: string; filePath: string; staged: boolean }) {
  return httpPost<GitFileDiff>('/api/Common/Git/FileDiff', request, undefined, LOCAL_TIMEOUT_MS)
}

// ==================== 环境检测与配置管理 ====================

export interface GitEnv {
  installed: boolean
  version: string
  userName: string
  userEmail: string
}

export interface GitConfigItem {
  key: string
  value: string
}

/** Git 环境检测（git 是否已安装、版本、全局身份） */
export function getGitEnv() {
  return httpPost<GitEnv>('/api/Common/Git/Env', {}, undefined, LOCAL_TIMEOUT_MS)
}

/** 读取全局 git 配置列表 */
export function getGitConfigList() {
  return httpPost<GitConfigItem[]>('/api/Common/Git/ConfigList', {}, undefined, LOCAL_TIMEOUT_MS)
}

/** 设置或删除一项全局配置（value 为 null 时删除） */
export function gitConfigSet(request: { key: string; value: string | null }) {
  return httpPost<GitCommandResult>('/api/Common/Git/ConfigSet', request)
}

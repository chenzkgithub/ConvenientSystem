// 短信管理模块类型定义

/** 短信模板 */
export interface SmsTemplateDto {
  id: number
  name: string
  content: string
  signature: string
  category: string
  enabled: boolean
  /** 创建人账号（后端关联 SysUser 查询） */
  createdByAccount?: string | null
  /** 创建人姓名（后端关联 SysUser 查询） */
  createdByName?: string | null
  createTime?: string
}

/** 短信任务 */
export interface SmsTaskDto {
  id: number
  name: string
  templateId: number
  templateName?: string
  sendTime: string
  status: number
  totalCount: number
  successCount: number
  failCount: number
  /** 创建人账号（后端关联 SysUser 查询） */
  createdByAccount?: string | null
  /** 创建人姓名（后端关联 SysUser 查询） */
  createdByName?: string | null
  createTime?: string
}

/** 收件人 */
export interface SmsRecipientDto {
  id: number
  taskId: number
  phone: string
  name: string
  status: number
  errorMessage?: string
  sentTime?: string
}

/** 发送日志 */
export interface SmsLogDto {
  id: number
  taskId: number
  taskName?: string
  phone: string
  content: string
  providerMsgId?: string
  status: number
  errorMessage?: string
  costMs: number
  createTime: string
}

/** 配额 */
export interface SmsQuotaDto {
  dailyMax: number
  monthlyMax: number
  dailyUsed: number
  monthlyUsed: number
}

/** 发送统计 */
export interface SmsStatisticsDto {
  todayCount: number
  monthCount: number
  successRate: number
  dailyRemaining: number
}

/** 创建任务请求 */
export interface CreateSmsTaskRequest {
  name: string
  templateId: number
  sendTime: string
  recipients: { phone: string; name: string }[]
}

/** 服务商配置 */
export interface SmsProviderConfigDto {
  id: number
  name: string
  providerType: string
  accessKeyId: string
  accessKeySecret: string
  defaultSignature: string
  templateCode: string
  templateId?: number | null
  templateName?: string | null
  isDefault: boolean
  enabled: boolean
  createTime?: string
  updateTime?: string
}

/** 测试发送请求 */
export interface SmsTestSendRequest {
  phone: string
  content: string
  signature: string
}

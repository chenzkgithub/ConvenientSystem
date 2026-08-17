// 邮件通知模块类型定义

/** 邮件 SMTP 配置 */
export interface EmailConfigDto {
  id: number
  name: string
  smtpServer: string
  smtpPort: number
  account: string
  password: string
  fromName: string
  enableSsl: boolean
  isDefault: boolean
  enabled: boolean
  createTime?: string
  updateTime?: string
}

/** 邮件任务 */
export interface EmailTaskDto {
  id: number
  name: string
  subject: string
  content: string
  recipients: string
  scheduleType: string
  sendTime?: string
  cronExpression?: string
  weekDays?: string
  dailyTime?: string
  enabled: boolean
  status: number
  lastSendTime?: string
  /** 创建人账号（后端关联 SysUser 查询） */
  createdByAccount?: string | null
  /** 创建人姓名（后端关联 SysUser 查询） */
  createdByName?: string | null
  createTime?: string
}

/** 邮件发送日志 */
export interface EmailLogDto {
  id: number
  taskId: number
  taskName: string
  recipients: string
  subject: string
  content: string
  status: number
  errorMessage?: string
  costMs: number
  /** 创建人账号（后端关联 SysUser 查询；系统自动发送为"系统"） */
  createdByAccount?: string | null
  /** 创建人姓名（后端关联 SysUser 查询） */
  createdByName?: string | null
  createTime: string
}

/** 测试发送请求 */
export interface EmailTestSendRequest {
  recipients: string
  subject: string
  content: string
}

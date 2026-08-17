/** 群 + 私聊机器人配置 */
export interface WebhookConfigDto {
  id: number
  name: string
  providerType: string
  // 群机器人字段
  webhookUrl: string
  secret?: string
  // 私聊机器人字段
  appKey?: string
  appSecret?: string
  recipientIds?: string
  // 控制字段
  enableGroup: boolean
  enablePrivate: boolean
  useCard: boolean
  isDefault: boolean
  enabled: boolean
  createTime?: string
  updateTime?: string
}

/** 测试发送结果 */
export interface WebhookSendResultDto {
  success: boolean
  errorMessage?: string
  costMs: number
}

/** 服务商类型显示名 */
export const PROVIDER_LABELS: Record<string, string> = {
  dingtalk: '钉钉',
  wecom: '企业微信',
  feishu: '飞书'
}

/** 机器人发送日志 */
export interface WebhookLogDto {
  id: number
  configId: number
  configName: string
  providerType: string
  title: string
  content: string
  success: boolean
  errorMessage?: string | null
  costMs: number
  createTime: string
}

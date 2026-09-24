import { httpGet, httpPost } from '@/api/request'

/** 通知管理 DTO（管理端列表与保存共用，id=0 表示新建） */
export interface NoticeDto {
  id: number
  title: string
  content: string
  /** 通知级别：1=普通 2=重要 3=紧急 */
  level: number
  /** 发布时联动邮件推送 */
  sendEmail: boolean
  /** 发布时联动短信推送 */
  sendSms: boolean
  /** 发布时联动群机器人广播 */
  sendWebhook: boolean
  enabled: boolean
  /** 有效期截止时间（空=永久有效；过期后用户端不再展示） */
  expireTime?: string | null
  createdByAccount?: string | null
  createdByName?: string | null
  /** 发布时间（只读；新建时不传，避免空字符串导致后端 DateTime 反序列化 400） */
  createTime?: string
  /** 定向接收用户 Id 列表（与角色均为空时默认发送给全部人员） */
  targetUserIds: string[]
  /** 定向接收角色 Id 列表（与用户均为空时默认发送给全部人员） */
  targetRoleIds: number[]
  /** 定向用户数（列表展示用） */
  targetUserCount?: number
  /** 定向角色数（列表展示用） */
  targetRoleCount?: number
}

/** 用户端通知（含当前用户已读状态） */
export interface NoticeUserDto {
  id: number
  title: string
  content: string
  level: number
  createTime: string
  isRead: boolean
}

// ===== 管理端（NoticeManageController，需 notice 权限） =====

/** 全部通知列表 */
export function getNoticeList() {
  return httpGet<NoticeDto[]>('/api/Common/NoticeManage/List')
}

/** 发布新通知（按勾选开关联动推送）或编辑已有通知 */
export function saveNotice(dto: NoticeDto) {
  return httpPost<void>('/api/Common/NoticeManage/Save', dto)
}

/** 删除通知（连同已读记录） */
export function deleteNotice(id: number) {
  return httpPost<void>(`/api/Common/NoticeManage/Delete?id=${id}`)
}

// ===== 用户端（NoticeController，任何已登录用户可用） =====

/** 当前用户可见的通知列表（仅启用的，含已读状态）。opts.silent：后台轮询不弹遮罩 */
export function getMyNotices(opts?: { silent?: boolean }) {
  return httpGet<NoticeUserDto[]>('/api/Common/Notice/MyList', undefined, undefined, opts)
}

/** 当前用户未读通知数（供顶栏铃铛角标轮询）。opts.silent：轮询/事件驱动的角标刷新不弹遮罩 */
export function getNoticeUnreadCount(opts?: { silent?: boolean }) {
  return httpGet<{ count: number }>('/api/Common/Notice/UnreadCount', undefined, undefined, opts)
}

/** 标记单条通知已读 */
export function markNoticeRead(noticeId: number) {
  return httpPost<void>(`/api/Common/Notice/MarkRead?noticeId=${noticeId}`)
}

/** 全部通知标记已读 */
export function markAllNoticeRead() {
  return httpPost<void>('/api/Common/Notice/MarkAllRead', {})
}

/**
 * 构建/部署/流水线完成与版本包上传确认通知：仅当前操作人可见（后端定向用户 + Clients.User 推送，不打扰其他在线用户）。
 * 铃铛可见 + 右上角弹卡片，level=2 重要。
 * 版本包的全员发布广播（含下载链接）由后端 Controller 在上传接口内直接创建，前端不再代发。
 */
export function notifyBuildComplete(title: string, content: string) {
  return httpPost<void>('/api/Common/Notice/BuildNotify', { title, content }, undefined, undefined, { silent: true })
}

/** 通知级别文案与标签色 */
export const NOTICE_LEVELS: Record<number, { label: string; type: 'info' | 'warning' | 'danger' }> = {
  1: { label: '普通', type: 'info' },
  2: { label: '重要', type: 'warning' },
  3: { label: '紧急', type: 'danger' },
}

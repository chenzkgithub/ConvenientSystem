import { httpGet, httpPost } from '@/api/request'

/** 当前登录用户的个人资料（账号只读） */
export interface ProfileDto {
  account: string
  displayName?: string | null
  /** 头像：data:image/...;base64 内联图片 */
  avatar?: string | null
  phone?: string | null
  email?: string | null
  remark?: string | null
}

/** 提交保存的个人资料字段（账号不可改） */
export interface ProfileSaveDto {
  displayName: string
  /** 传空字符串表示清除头像 */
  avatar?: string | null
  phone?: string | null
  email?: string | null
  remark?: string | null
}

/** 读取当前登录用户的个人资料 */
export function getProfile() {
  return httpGet<ProfileDto>('/api/Common/Profile/Get')
}

/** 修改个人资料（显示名称、头像、手机号、邮箱、备注） */
export function saveProfile(dto: ProfileSaveDto) {
  return httpPost<void>('/api/Common/Profile/Save', dto)
}

/** 修改本人密码（需校验原密码）；成功后应重新登录 */
export function changePassword(oldPassword: string, newPassword: string) {
  return httpPost<void>('/api/Common/Profile/ChangePassword', { oldPassword, newPassword })
}

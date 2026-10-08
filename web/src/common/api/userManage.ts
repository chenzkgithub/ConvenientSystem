import { httpGet, httpPost } from '@/api/request'

/** 用户（含所属角色） */
export interface UserManageDto {
  id: string
  account: string
  displayName: string
  /** 头像：data:image/...;base64 内联图片 */
  avatar?: string | null
  phone?: string | null
  email?: string | null
  remark?: string | null
  enabled: boolean
  isDeleted: boolean
  createTime: string
  /** 注册来源：web=Web 端 / app=手机端（存量数据视为 web） */
  registerSource?: string | null
  roleIds: number[]
  roleNames: string[]
}

/** 用户新增/编辑入参 */
export interface UserSaveDto {
  id: string
  account: string
  displayName: string
  /** 新增必填；编辑留空表示不改密码 */
  password?: string
  /** 头像：data:image/...;base64；空串表示无头像 */
  avatar?: string | null
  phone?: string | null
  email?: string | null
  remark?: string | null
  enabled: boolean
  roleIds: number[]
}

/** 用户列表（对应后端 UserManageController；列表不返回头像，响应体瘦身） */
export function listUsers() {
  return httpGet<UserManageDto[]>('/api/Common/UserManage/List')
}

/** 单用户详情（含头像，供编辑弹窗使用） */
export function getUser(id: string) {
  return httpGet<UserManageDto>(`/api/Common/UserManage/Get?id=${id}`)
}

/** 新增或更新用户 */
export function saveUser(dto: UserSaveDto) {
  return httpPost<void>('/api/Common/UserManage/Save', dto)
}

/** 启用/停用用户 */
export function setUserEnabled(id: string, enabled: boolean) {
  return httpPost<void>('/api/Common/UserManage/SetEnabled', { id, enabled })
}

/** 重置密码 */
export function resetUserPassword(id: string, password: string) {
  return httpPost<void>('/api/Common/UserManage/ResetPassword', { id, password })
}

/** 删除用户（软删除） */
export function deleteUser(id: string) {
  return httpPost<void>(`/api/Common/UserManage/Delete?id=${id}`)
}

/** 恢复已删除用户（软删除还原，恢复后默认启用） */
export function restoreUser(id: string) {
  return httpPost<void>(`/api/Common/UserManage/Restore?id=${id}`)
}

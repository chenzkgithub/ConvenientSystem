import { httpGet, httpPost } from '@/api/request'
import type { RoleDto, MenuFlatDto } from '@/common/api/roleManage'
import type { MenuPermFlatDto, ViewPermNodeDto } from '@/common/api/view'

export type { RoleDto, MenuFlatDto, MenuPermFlatDto, ViewPermNodeDto }

/** 权限设置：角色的菜单分配 + 视图权限点分配请求 */
export interface RolePermissionsDto {
  roleId: number
  menuIds: number[]
  viewPermIds: number[]
}

/** 角色列表（含已分配的菜单 Id，供权限设置页使用）*/
export function listPermissionRoles() {
  return httpGet<RoleDto[]>('/api/Common/Permission/List')
}

/** 全部菜单扁平列表（含视图权限点，供权限树使用）*/
export function listPermissionMenusFlat() {
  return httpGet<MenuPermFlatDto[]>('/api/Common/View/GetMenusWithViewPerms')
}

/** 保存角色的菜单权限 + 视图权限点 */
export function saveRolePermissions(dto: RolePermissionsDto) {
  return httpPost<void>('/api/Common/Permission/Save', dto)
}

// ========== 用户级授权 ==========

/** 用户简要信息（角色树叶子）*/
export interface UserBriefDto {
  id: string
  account: string
  displayName?: string | null
  avatar?: string | null
  enabled: boolean
}

/** 角色 + 该角色下的用户列表（供权限设置左侧角色→用户树）*/
export interface RoleWithUsersDto {
  id: number
  name: string
  code: string
  description?: string | null
  enabled: boolean
  isAdmin: boolean
  menuIds: number[]
  viewPermIds: number[]
  users: UserBriefDto[]
}

/** 用户级权限保存请求 */
export interface UserPermissionsDto {
  userId: string
  menuIds: number[]
  viewPermIds: number[]
}

/** 用户权限详情响应（菜单 + 视图权限点） */
export interface UserPermDetailDto {
  menuIds: number[]
  viewPermIds: number[]
}

/** 角色列表（含各角色下的用户），供权限设置左侧树 */
export function listRolesWithUsers() {
  return httpGet<RoleWithUsersDto[]>('/api/Common/Permission/ListWithUsers')
}

/** 用户直接授权的菜单 Id 列表 + 视图权限点 Id 列表 */
export function getUserPermissions(userId: string) {
  return httpGet<UserPermDetailDto>(`/api/Common/Permission/GetUserPermissions?userId=${userId}`)
}

/** 保存用户级菜单授权 + 视图权限点 */
export function saveUserPermissions(dto: UserPermissionsDto) {
  return httpPost<void>('/api/Common/Permission/SaveUserPermissions', dto)
}

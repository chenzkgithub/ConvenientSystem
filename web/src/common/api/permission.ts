import { httpGet, httpPost } from '@/api/request'
import type { RoleDto, MenuFlatDto } from '@/common/api/roleManage'

export type { RoleDto, MenuFlatDto }

/** 权限设置：角色的菜单分配请求 */
export interface RolePermissionsDto {
  roleId: number
  menuIds: number[]
}

/** 角色列表（含已分配的菜单 Id，供权限设置页使用）*/
export function listPermissionRoles() {
  return httpGet<RoleDto[]>('/api/Common/Permission/List')
}

/** 全部菜单扁平列表（供权限树使用）*/
export function listPermissionMenusFlat() {
  return httpGet<MenuFlatDto[]>('/api/Common/Permission/GetMenusFlat')
}

/** 保存角色的菜单权限（仅更新菜单分配，不改角色基本信息）*/
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
  users: UserBriefDto[]
}

/** 用户级权限保存请求 */
export interface UserPermissionsDto {
  userId: string
  menuIds: number[]
}

/** 角色列表（含各角色下的用户），供权限设置左侧树 */
export function listRolesWithUsers() {
  return httpGet<RoleWithUsersDto[]>('/api/Common/Permission/ListWithUsers')
}

/** 用户直接授权的菜单 Id 列表（不含角色继承的）*/
export function getUserPermissions(userId: string) {
  return httpGet<number[]>(`/api/Common/Permission/GetUserPermissions?userId=${userId}`)
}

/** 保存用户级菜单授权 */
export function saveUserPermissions(dto: UserPermissionsDto) {
  return httpPost<void>('/api/Common/Permission/SaveUserPermissions', dto)
}

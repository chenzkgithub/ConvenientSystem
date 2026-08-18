import { httpGet, httpPost } from '@/api/request'

/** 角色（含分配的菜单 Id） */
export interface RoleDto {
  id: number
  name: string
  code: string
  description?: string | null
  enabled: boolean
  isAdmin: boolean
  /** 数据范围：0=本人 1=全部 */
  dataScope: number
  createTime: string
  menuIds: number[]
}

/** 角色新增/编辑入参 */
export interface RoleSaveDto {
  id: number
  name: string
  code: string
  description?: string | null
  enabled: boolean
  isAdmin: boolean
  /** 数据范围：0=本人 1=全部 */
  dataScope: number
  menuIds: number[]
}

/** 菜单扁平项（供角色分配可见菜单的树选择） */
export interface MenuFlatDto {
  id: number
  parentId?: number | null
  title: string
  /** 节点类型：0=Group，1=Page，2=Button */
  type?: number
}

/** 角色列表（对应后端 RoleManageController） */
export function listRoles() {
  return httpGet<RoleDto[]>('/api/Common/RoleManage/List')
}

/** 新增或更新角色 */
export function saveRole(dto: RoleSaveDto) {
  return httpPost<void>('/api/Common/RoleManage/Save', dto)
}

/** 删除角色 */
export function deleteRole(id: number) {
  return httpPost<void>('/api/Common/RoleManage/Delete', id)
}

/** 启用/停用角色 */
export function toggleRoleEnabled(id: number, enabled: boolean) {
  return httpPost<void>('/api/Common/RoleManage/ToggleEnabled', { id, enabled })
}

/** 全部菜单扁平列表（供角色分配可见菜单） */
export function listMenusFlat() {
  return httpGet<MenuFlatDto[]>('/api/Common/Menu/GetMenusFlat')
}

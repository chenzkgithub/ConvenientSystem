import { httpGet, httpPost, httpDelete } from '@/api/request'

// ========== 类型定义 ==========

export interface ViewPermissionDto {
  id: number
  name: string
  title: string
  sortOrder: number
  enabled: boolean
}

export interface ViewDto {
  id: number
  name: string
  title: string
  component?: string | null
  routePath?: string | null
  description?: string | null
  enabled: boolean
  sortOrder: number
  permissions: ViewPermissionDto[]
}

export interface ViewSaveDto {
  id: number
  name: string
  title: string
  component?: string | null
  routePath?: string | null
  description?: string | null
  enabled: boolean
}

export interface ViewPermissionSaveDto {
  id: number
  viewId: number
  name: string
  title: string
}

export interface ViewSaveResultDto {
  ok: boolean
  msg?: string
}

// ========== 接口 ==========

/** 获取全部视图列表（含权限点） */
export function getViews() {
  return httpGet<ViewDto[]>('/api/Common/View/GetViews')
}

/** 新增或编辑视图 */
export function saveView(dto: ViewSaveDto) {
  return httpPost<ViewSaveResultDto>('/api/Common/View/SaveView', dto)
}

/** 删除视图 */
export function deleteView(id: number) {
  return httpDelete(`/api/Common/View/DeleteView?id=${id}`)
}

/** 新增或编辑权限点 */
export function savePermission(dto: ViewPermissionSaveDto) {
  return httpPost<ViewSaveResultDto>('/api/Common/View/SavePermission', dto)
}

/** 删除权限点 */
export function deletePermission(id: number) {
  return httpDelete(`/api/Common/View/DeletePermission?id=${id}`)
}

// ========== 权限设置相关 ==========

/** 视图权限点节点 */
export interface ViewPermNodeDto {
  id: number
  name: string
  title: string
}

/** 带视图权限点的菜单扁平列表 */
export interface MenuPermFlatDto {
  id: number
  parentId: number | null
  title: string
  name: string | null
  type: number
  viewPerms: ViewPermNodeDto[] | null
}

/** 获取带视图权限点的菜单列表（供权限设置页） */
export function getMenusWithViewPerms() {
  return httpGet<MenuPermFlatDto[]>('/api/Common/View/GetMenusWithViewPerms')
}

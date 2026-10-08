import { httpGet, httpPost } from '@/api/request'
import type { MenuNode } from '@/common/types'

/** 菜单树（menus.xml，对应后端 MenuController）；opts.silent 供启动加载静默（失败有缓存兜底） */
export function getMenus(opts?: { silent?: boolean }) {
  return httpGet<MenuNode[]>('/api/Common/Menu/GetMenus', undefined, undefined, opts)
}

/** 全量菜单树（菜单管理页专用）：不受角色授权过滤，需 menu-manage:save 权限 */
export function getAllMenus() {
  return httpGet<MenuNode[]>('/api/Common/Menu/GetAllMenus')
}

/** 保存菜单树到 menus.xml */
export function saveMenus(menus: MenuNode[]) {
  return httpPost<{ ok: boolean; msg?: string }>('/api/Common/Menu/SaveMenus', menus)
}

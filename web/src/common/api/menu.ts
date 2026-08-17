import { httpGet, httpPost } from '@/api/request'
import type { MenuNode } from '@/common/types'

/** 菜单树（menus.xml，对应后端 MenuController） */
export function getMenus() {
  return httpGet<MenuNode[]>('/api/Common/Menu/GetMenus')
}

/** 保存菜单树到 menus.xml */
export function saveMenus(menus: MenuNode[]) {
  return httpPost<{ ok: boolean; msg?: string }>('/api/Common/Menu/SaveMenus', menus)
}

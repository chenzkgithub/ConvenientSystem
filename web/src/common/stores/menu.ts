import { defineStore } from 'pinia'
import { ref } from 'vue'
import { getMenus } from '@/common/api/menu'
import type { MenuNode } from '@/common/types'

/** 菜单树（来自后端 menus.xml / GetMenus），供侧栏与首页共享 */
export const useMenuStore = defineStore('menu', () => {
  const menus = ref<MenuNode[]>([])
  const loaded = ref(false)

  async function load() {
    try {
      const data = await getMenus()
      menus.value = Array.isArray(data) ? data : []
    } catch (e) {
      console.error('菜单加载失败', e)
      menus.value = []
    }
    loaded.value = true
  }

  /** 收集所有末级菜单（含 page），跳过 visible=false 和 enabled=false */
  function collectLeaves(): MenuNode[] {
    const acc: MenuNode[] = []
    const walk = (nodes: MenuNode[]) => {
      nodes.forEach((n) => {
        if (n.visible === false || n.enabled === false) return
        if (Array.isArray(n.children) && n.children.length > 0) walk(n.children)
        else if (n.page) acc.push(n)
      })
    }
    walk(menus.value)
    return acc
  }

  /** 按最顶层菜单分组，返回 [{ title, leaves }]，跳过 visible=false 和 enabled=false */
  function collectGrouped(): { title: string; leaves: MenuNode[] }[] {
    const groups: { title: string; leaves: MenuNode[] }[] = []
    for (const top of menus.value) {
      if (top.visible === false || top.enabled === false) continue
      const leaves: MenuNode[] = []
      const collect = (nodes: MenuNode[]) => {
        nodes.forEach((n) => {
          if (n.visible === false || n.enabled === false) return
          if (Array.isArray(n.children) && n.children.length > 0) collect(n.children)
          else if (n.page) leaves.push(n)
        })
      }
      if (Array.isArray(top.children) && top.children.length > 0) {
        collect(top.children)
      } else if (top.page) {
        leaves.push(top)
      }
      if (leaves.length) groups.push({ title: top.title, leaves })
    }
    return groups
  }

  /** 重置菜单状态（退出登录时调用） */
  function reset() {
    menus.value = []
    loaded.value = false
  }

  return { menus, loaded, load, collectLeaves, collectGrouped, reset }
})

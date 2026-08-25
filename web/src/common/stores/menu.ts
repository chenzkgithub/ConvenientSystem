import { defineStore } from 'pinia'
import { ref } from 'vue'
import { getMenus } from '@/common/api/menu'
import type { MenuNode } from '@/common/types'

const CACHE_KEY = 'menu_tree_cache_v1'

/** 菜单树（来自后端 menus.xml / GetMenus），供侧栏与首页共享 */
export const useMenuStore = defineStore('menu', () => {
  const menus = ref<MenuNode[]>([])
  const loaded = ref(false)

  async function load() {
    try {
      const data = await getMenus()
      menus.value = Array.isArray(data) ? data : []
      // API 成功时写入缓存，供独立窗口（standalone）API 失败时兜底
      if (menus.value.length > 0) {
        try { localStorage.setItem(CACHE_KEY, JSON.stringify(menus.value)) } catch { /* 配额不足忽略 */ }
      }
    } catch (e) {
      console.error('菜单加载失败', e)
      // API 失败时尝试从缓存恢复（独立窗口场景：与主窗口同源 localStorage 共享，
      // 但 API 调用可能因时序/认证等原因失败，用缓存兜底避免全部页面显示"开发中"）
      try {
        const raw = localStorage.getItem(CACHE_KEY)
        if (raw) {
          const cached = JSON.parse(raw)
          if (Array.isArray(cached) && cached.length > 0) {
            menus.value = cached
            console.info('菜单从缓存恢复')
          }
        }
      } catch { /* 缓存读取失败保持空 */ }
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

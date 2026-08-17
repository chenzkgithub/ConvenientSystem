import { defineStore } from 'pinia'
import { ref } from 'vue'

const RECENT_KEY = 'cs_recent_pages'
const MAX_RECENT = 8

interface RecentItem {
  path: string
  title: string
  icon?: string
  timestamp: number
}

/**
 * 最近访问页面记录：localStorage 持久化，最多保留 8 条。
 * 相同路径不重复，访问时移到最前。
 */
export const useRecentStore = defineStore('recent', () => {
  const items = ref<RecentItem[]>(loadFromStorage())

  function loadFromStorage(): RecentItem[] {
    try {
      const raw = localStorage.getItem(RECENT_KEY)
      return raw ? JSON.parse(raw) : []
    } catch {
      return []
    }
  }

  function save() {
    localStorage.setItem(RECENT_KEY, JSON.stringify(items.value))
  }

  function record(path: string, title: string, icon?: string) {
    if (!path || !title) return
    // 移除已有同路径记录
    items.value = items.value.filter((i) => i.path !== path)
    // 添加到最前
    items.value.unshift({ path, title, icon, timestamp: Date.now() })
    // 限制数量
    if (items.value.length > MAX_RECENT) {
      items.value = items.value.slice(0, MAX_RECENT)
    }
    save()
  }

  function remove(path: string) {
    items.value = items.value.filter((i) => i.path !== path)
    save()
  }

  function clear() {
    items.value = []
    save()
  }

  function reset() {
    items.value = []
    localStorage.removeItem(RECENT_KEY)
  }

  return { items, record, remove, clear, reset }
})

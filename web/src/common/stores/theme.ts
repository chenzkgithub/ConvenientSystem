import { defineStore } from 'pinia'
import { ref } from 'vue'

const THEME_KEY = 'cs_theme'

/**
 * 主题管理：支持亮色 / 暗色 / 跟随系统三种模式。
 * 切换时在 document.documentElement 上增删 .dark class，
 * main.css 中 html.dark 选择器覆盖所有 CSS 变量。
 */
export const useThemeStore = defineStore('theme', () => {
  type ThemeMode = 'light' | 'dark' | 'system'
  const mode = ref<ThemeMode>(
    (localStorage.getItem(THEME_KEY) as ThemeMode) || 'light',
  )

  /** 当前是否处于暗色（考虑 system 模式的实际效果） */
  const isDark = ref(false)

  function resolveDark(): boolean {
    if (mode.value === 'dark') return true
    if (mode.value === 'light') return false
    return window.matchMedia('(prefers-color-scheme: dark)').matches
  }

  function apply() {
    isDark.value = resolveDark()
    document.documentElement.classList.toggle('dark', isDark.value)
  }

  function setMode(m: ThemeMode) {
    mode.value = m
    localStorage.setItem(THEME_KEY, m)
    apply()
  }

  function toggle() {
    setMode(isDark.value ? 'light' : 'dark')
  }

  /** 初始化：应用主题 + 监听系统偏好变化 */
  function init() {
    apply()
    window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', () => {
      if (mode.value === 'system') apply()
    })
  }

  return { mode, isDark, setMode, toggle, init }
})

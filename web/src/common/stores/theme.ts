import { defineStore } from 'pinia'
import { ref } from 'vue'
import { useUserPrefs } from '@/common/composables/useUserPrefs'

const THEME_KEY = 'UI.ThemeMode'

/**
 * 主题管理：支持亮色 / 暗色 / 跟随系统三种模式。
 * 切换时在 document.documentElement 上增删 .dark class，
 * main.css 中 html.dark 选择器覆盖所有 CSS 变量。
 */
export const useThemeStore = defineStore('theme', () => {
  type ThemeMode = 'light' | 'dark' | 'system'
  const { getPref, setPref } = useUserPrefs()
  const mode = ref<ThemeMode>(
    (getPref(THEME_KEY, 'light') as ThemeMode) || 'light',
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
    setPref(THEME_KEY, m)
    apply()
  }

  function toggle() {
    setMode(isDark.value ? 'light' : 'dark')
  }

  /** 服务器偏好加载后同步 */
  function applyServerPrefs() {
    const { getPref } = useUserPrefs()
    const serverMode = (getPref(THEME_KEY, 'light') as ThemeMode) || 'light'
    if (mode.value !== serverMode) {
      mode.value = serverMode
      apply()
    }
  }

  /** 初始化：应用主题 + 监听系统偏好变化 */
  function init() {
    apply()
    window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', () => {
      if (mode.value === 'system') apply()
    })
  }

  return { mode, isDark, setMode, toggle, init, applyServerPrefs }
})

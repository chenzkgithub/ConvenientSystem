import { useAuthStore } from '@/common/stores/auth'

/**
 * 按钮级权限检查 composable。
 * 用法：const { has } = usePermission(); if (has('user-manage:delete')) { ... }
 * 权限码来源于登录时 JWT 的 menuCodes（含 Type=2 权限点 Name）。
 */
export function usePermission() {
  const auth = useAuthStore()
  /** 当前用户是否拥有指定权限码（菜单码或按钮权限点码） */
  const has = (code: string): boolean => auth.menuCodes.includes(code)
  return { has }
}

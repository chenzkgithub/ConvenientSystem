/**
 * 扫描 src 下所有视图组件，供菜单管理选择内部路由组件时使用。
 * 组件路径支持两种写法：
 * - 菜单配置中推荐写 /src/...（如 /src/yunhan/views/AttendanceView.vue）
 * - Vite glob 返回的 key 为相对于本文件的位置：
 *     * 同目录 common/views 下为 ./views/XXX.vue
 *     * 其它模块下为 ../xxx/views/XXX.vue
 */

// 本文件位于 src/common/viewComponents.ts，因此 ../ 指向 src/ 目录
export const viewModules = import.meta.glob('../**/views/**/*.vue')

export interface ViewOption {
  label: string
  value: string
}

/**
 * 把 glob 返回的相对 key 统一转换为规范路径 /src/...：
 * - ./views/XXX.vue（同目录 common/views） -> /src/common/views/XXX.vue
 * - ../yunhan/views/XXX.vue                -> /src/yunhan/views/XXX.vue
 */
function normalizeKey(key: string): string {
  if (key.startsWith('./views/')) {
    return '/src/common/views/' + key.replace(/^\.\/views\//, '')
  }
  if (key.startsWith('../')) {
    return key.replace(/^\.\.\//, '/src/')
  }
  return key
}

/** 把菜单中保存的 /src/... 路径转换回 glob 返回的相对 key */
function normalizeComponentPath(path?: string | null): string | undefined {
  if (!path) return undefined
  // 兼容旧数据：若保存的是 ./views/...（当前版本不应出现，仅作兜底）
  if (path.startsWith('./views/')) return path
  if (path.startsWith('/src/common/views/')) {
    return path.replace(/^\/src\/common\/views\//, './views/')
  }
  if (path.startsWith('/src/')) {
    return path.replace(/^\/src\//, '../')
  }
  return path
}

/** 组件下拉选项，value 保存为 /src/...，label 显示为去掉 /src/ 的路径 */
export const viewComponentOptions: ViewOption[] = Object.keys(viewModules)
  .map((key) => {
    const value = normalizeKey(key)
    return {
      label: value.replace(/^\/src\//, ''),
      value,
    }
  })
  .sort((a, b) => a.label.localeCompare(b.label))

/** 根据组件路径获取异步导入函数 */
export function getViewComponent(path?: string | null) {
  if (!path) return undefined

  // 尝试多种 key 格式（glob 返回的 key 可能因路径不同而有差异）
  const candidates = [
    normalizeComponentPath(path),
    // 如果 normalizeComponentPath 返回 ./views/xxx，也尝试 ../common/views/xxx
    path.startsWith('/src/common/views/')
      ? path.replace(/^\/src\//, '../')
      : null,
    // 直接使用原始路径（兼容某些 glob 实现）
    path,
  ].filter(Boolean) as string[]

  for (const key of candidates) {
    const mod = viewModules[key]
    if (mod) return mod
  }
  return undefined
}

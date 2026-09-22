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
  /** 运行环境（接口分离）：供菜单管理分组下拉与对账使用，运行时过滤以 VIEW_META 真源为准 */
  env: ViewEnv
}

/** 视图运行环境：desktop=仅桌面端 / web=仅服务器端 / both=双宿主通用 */
export type ViewEnv = 'desktop' | 'web' | 'both'

/**
 * 视图元数据注册表（接口分离的运行时真源）：
 * - 以 normalizeKey 后的 /src/... 组件路径为键，登记环境归属；未登记的视图默认 both（通用）；
 * - desktopViews 由本表派生（不再单独硬编码清单），与 router/index.ts 静态路由真正同源；
 * - DB SysView.Env 只是管理面镜像（呈现/筛选/对账），不参与运行时过滤。
 */
const VIEW_META: Record<string, { env: ViewEnv }> = {
  '/src/common/views/UniversalBuildView.vue': { env: 'desktop' },
  '/src/common/views/PipelineView.vue': { env: 'desktop' },
  '/src/common/views/GitWorkbenchView.vue': { env: 'desktop' },
  '/src/common/views/LocalMonitorView.vue': { env: 'desktop' },
  '/src/common/views/ApiSpecView.vue': { env: 'desktop' },
  '/src/common/views/CodeScanView.vue': { env: 'desktop' },
  '/src/common/views/ConfigEditorView.vue': { env: 'desktop' },
  '/src/common/views/HostsView.vue': { env: 'desktop' },
  // 双宿主锚点：桌面端（本地内网库直连）与 Web 端（服务器统计库）均可运行，模块内部按宿主自适应
  '/src/yunhan/views/AttendanceView.vue': { env: 'both' },
}

/** 查询组件运行环境（未登记默认 both）；入参先做 normalizeKey 归一，兼容 ./views/ 与 ../ 旧写法 */
export function getViewEnv(path?: string | null): ViewEnv {
  if (!path) return 'both'
  return VIEW_META[normalizeKey(path)]?.env ?? 'both'
}

/** 桌面专属视图组件路径集合（由 VIEW_META 派生）：菜单动态注册过滤与路由守卫的唯一依据 */
export const desktopViews: ReadonlySet<string> = new Set(
  Object.entries(VIEW_META)
    .filter(([, meta]) => meta.env === 'desktop')
    .map(([path]) => path),
)

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
      env: getViewEnv(value),
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

// 公共模块类型定义（菜单/导航等跨模块共享）。

/** 菜单节点（对应 menus.xml / GetMenus） */
export interface MenuNode {
  /** 菜单 Id（后端返回，保存时回传用于维护角色-菜单关联） */
  id?: number
  title: string
  /** 末级菜单的路由路径（如 /attendance）或外部 URL；分组菜单为空 */
  page?: string | null
  children: MenuNode[]
  /** 是否在悬浮按钮菜单中显示 */
  float?: boolean
  /** 是否在主界面侧栏和首页中显示（默认 true） */
  visible?: boolean
  /** 是否为外部链接（true=外链，false=内部路由），默认 false */
  external?: boolean
  /** 内部路由名称（仅内部链接有意义） */
  name?: string | null
  /** 内部路由对应的 Vue 组件路径（仅内部链接有意义） */
  component?: string | null
  /** 是否允许在菜单管理中编辑（true=允许，false=不允许），默认 true */
  editable?: boolean
  /** 是否启用（停用后不在侧栏/首页显示，也不可在权限管理中分配），默认 true */
  enabled?: boolean
}

/** 按日发送趋势数据点（对应后端 DailyTrendPointDto） */
export interface DailyTrendPoint {
  date: string
  success: number
  failed: number
  total: number
}

/** 发送趋势汇总（对应后端 SendTrendDto） */
export interface SendTrend {
  points: DailyTrendPoint[]
  totalSuccess: number
  totalFailed: number
  totalCount: number
}

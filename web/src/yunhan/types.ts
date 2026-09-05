// 昀晗（考勤）模块类型定义。
// 说明：多数业务接口返回 camelCase；部门接口(GetDeptTree)返回 snake_case，单独定义。

/** 部门视图（GetDeptTree 原始返回，snake_case） */
export interface DeptViewRaw {
  corpId?: string
  corpName?: string
  dept_id: number
  dept_name: string
  parent_id: number
  full_name?: string
  full_code: string
  lvl?: number
  deptIsDel?: number
}

/** 前端使用的部门树节点 */
export interface DeptNode {
  id: number
  name: string
  parentId: number
  full_code: string
  level: number
  children: DeptNode[]
}

/** 考勤查询入参 */
export interface RequestDto {
  fullCode: string
  userName: string
  month: string
  orderby: string
  /** 钉钉用户ID：查看明细时精确定位到具体人员，避免同名混淆 */
  ddUserId?: string
}

/** 考勤明细/汇总/排行统一数据结构（camelCase） */
export interface AttendanceRow {
  avatar?: string
  ddUserId?: string
  /** 钉钉个人 ID（用于 URL Scheme 打开聊天窗口） */
  dingCode?: string
  /** 企业 corpId（钉钉 URL Scheme 跳转联系人详情页用） */
  corpId?: string
  userName: string
  deptName?: string
  /** 部门全称（如"南昌分公司-运营部-拼多多组"） */
  fullDeptName?: string
  hiredDate?: string
  employeeStatus?: number
  workDuration: number
  leaveDuration: number
  travelDuration: number
  overtimeDuration: number
  actualDuration: number
  // 明细专有
  workDate?: string
  workTime?: string
  restTime?: string
  // 排行专有
  index?: number
  deptId?: string
}

/** 可排序的时长字段名 */
export type DurationField =
  | 'workDuration'
  | 'leaveDuration'
  | 'travelDuration'
  | 'overtimeDuration'
  | 'actualDuration'

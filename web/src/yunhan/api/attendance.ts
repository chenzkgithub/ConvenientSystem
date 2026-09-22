import { httpGet, httpPost, localGet, localPost } from '@/api/request'
import { IS_DESKTOP_HOST } from '@/common/hostContext'
import type { DeptViewRaw, AttendanceRow, RequestDto } from '@/yunhan/types'

/** 接口归属：mixed=桌面端走本地控制器（直连内网库），Web 端走服务器 API（审计脚本依据，勿删） */
export const API_SIDE = 'mixed' as const

/** 考勤宿主前缀：桌面端本地控制器 api/local/attendance；Web 端服务器 api/YunHan/Attendance */
const PREFIX = IS_DESKTOP_HOST ? '/api/local/attendance' : '/api/YunHan/Attendance'

/**
 * 双宿主请求通道：PREFIX 按宿主切换后，请求函数也必须按宿主切换——
 * local 通道自带非桌面端守卫（直接抛错不发请求），Web 端必须走远程 http 通道。
 */
function hostGet<T>(url: string, opts?: { silent?: boolean }) {
  return IS_DESKTOP_HOST
    ? localGet<T>(url, undefined, undefined, opts)
    : httpGet<T>(url, undefined, undefined, opts)
}

function hostPost<T>(url: string, body: unknown) {
  return IS_DESKTOP_HOST
    ? localPost<T>(url, body)
    : httpPost<T>(url, body)
}

/** 组织架构（snake_case 原始返回）；opts.silent 供本地模式探测（不弹 loading/错误提示） */
export function getDeptTree(opts?: { silent?: boolean }) {
  return hostGet<DeptViewRaw[]>(`${PREFIX}/GetDeptTree`, opts)
}

/** 考勤汇总 */
export function getAttendance(req: RequestDto) {
  return hostPost<AttendanceRow[]>(`${PREFIX}/GetAttendance`, req)
}

/** 考勤明细 */
export function getAttendanceDtl(req: RequestDto) {
  return hostPost<AttendanceRow[]>(`${PREFIX}/GetAttendanceDtl`, req)
}

/** 当月排行前100 */
export function getDailyRanking(req: RequestDto) {
  return hostPost<AttendanceRow[]>(`${PREFIX}/GetDailyRanking`, req)
}

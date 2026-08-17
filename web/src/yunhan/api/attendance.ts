import { httpGet, httpPost } from '@/api/request'
import type { DeptViewRaw, AttendanceRow, RequestDto } from '@/yunhan/types'

/** 组织架构（snake_case 原始返回） */
export function getDeptTree() {
  return httpGet<DeptViewRaw[]>('/api/YunHan/Attendance/GetDeptTree')
}

/** 考勤汇总 */
export function getAttendance(req: RequestDto) {
  return httpPost<AttendanceRow[]>('/api/YunHan/Attendance/GetAttendance', req)
}

/** 考勤明细 */
export function getAttendanceDtl(req: RequestDto) {
  return httpPost<AttendanceRow[]>('/api/YunHan/Attendance/GetAttendanceDtl', req)
}

/** 当月排行前100 */
export function getDailyRanking(req: RequestDto) {
  return httpPost<AttendanceRow[]>('/api/YunHan/Attendance/GetDailyRanking', req)
}

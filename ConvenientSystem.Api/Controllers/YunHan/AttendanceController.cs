using ConvenientSystem.Api.Auth;
using ConvenientSystem.Shared.Entity.YunHan;
using ConvenientSystem.Shared.Model.YunHan;
using ConvenientSystem.Service.YunHan;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ConvenientSystem.Api.Controllers.YunHan
{
    /// <summary>
    /// 考勤查询接口：均为只读查询且不区分访问者身份，全部标 [AllowAnonymous]
    /// 供外部公开页面（链接带 public=1）免登录访问。
    /// 类级 [PermissionAuthorize] 保留：后续新增的写操作默认仍受权限约束，需要公开时才单独标注。
    /// </summary>
    [Area("YunHan")]
    [PermissionAuthorize("attendance")]
    public class AttendanceController : BaseController
    {
        private readonly IAttendanceService _attendanceService;

        public AttendanceController(IAttendanceService attendanceService)
        {
            _attendanceService = attendanceService;
        }

        /// <summary>
        /// 获取部门
        /// </summary>
        [HttpGet]
        [AllowAnonymous]
        public async Task<ActionResult<List<DeptView>>> GetDeptTree()
            => Ok(await _attendanceService.GetDeptTreeAsync());

        /// <summary>
        /// 获取考勤数据
        /// </summary>
        /// <param name="requet">入参格式 2026-07</param>
        [HttpPost]
        [AllowAnonymous]
        public async Task<ActionResult<List<AttendanceSumDto>>> GetAttendance(RequestDto requet)
            => Ok(await _attendanceService.GetAttendanceAsync(requet, HttpContext.RequestAborted));

        /// <summary>
        /// 获取考勤明细数据
        /// </summary>
        /// <param name="requet">入参格式 2026-07</param>
        [HttpPost]
        [AllowAnonymous]
        public async Task<ActionResult<List<AttendanceDto>>> GetAttendanceDtl(RequestDto requet)
            => Ok(await _attendanceService.GetAttendanceDtlAsync(requet));

        /// <summary>
        /// 获取当月前100排行
        /// </summary>
        /// <param name="requet">入参格式 2026-07</param>
        [HttpPost]
        [AllowAnonymous]
        public async Task<ActionResult<List<AttendanceSumDto>>> GetDailyRanking(RequestDto requet)
            => Ok(await _attendanceService.GetDailyRankingAsync(requet, HttpContext.RequestAborted));
    }
}

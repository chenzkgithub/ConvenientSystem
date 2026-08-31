using ConvenientSystem.Service.YunHan;
using ConvenientSystem.Shared.Entity.YunHan;
using ConvenientSystem.Shared.Model.YunHan;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ConvenientSystem.Controllers.YunHan;

/// <summary>
/// 桌面端本地考勤查询控制器：直连内网 SQL Server，不经过云 API。
/// 路由与云 API 端 AttendanceController 完全一致（api/YunHan/Attendance/{action}），
/// 前端无需改动。仅当 appsettings.json 配置了 YhSystemDb 连接串时生效。
/// </summary>
[ApiController]
[Route("api/[area]/[controller]/[action]")]
[Area("YunHan")]
[AllowAnonymous]
public class AttendanceController : ControllerBase
{
    private readonly IAttendanceService _attendanceService;

    public AttendanceController(IAttendanceService attendanceService)
    {
        _attendanceService = attendanceService;
    }

    [HttpGet]
    public async Task<ActionResult<List<DeptView>>> GetDeptTree()
        => Ok(await _attendanceService.GetDeptTreeAsync());

    [HttpPost]
    public async Task<ActionResult<List<AttendanceSumDto>>> GetAttendance(RequestDto requet)
        => Ok(await _attendanceService.GetAttendanceAsync(requet, HttpContext.RequestAborted));

    [HttpPost]
    public async Task<ActionResult<List<AttendanceDto>>> GetAttendanceDtl(RequestDto requet)
        => Ok(await _attendanceService.GetAttendanceDtlAsync(requet));

    [HttpPost]
    public async Task<ActionResult<List<AttendanceSumDto>>> GetDailyRanking(RequestDto requet)
        => Ok(await _attendanceService.GetDailyRankingAsync(requet, HttpContext.RequestAborted));
}

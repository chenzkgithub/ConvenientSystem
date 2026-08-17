using ConvenientSystem.Shared.Entity.YunHan;
using ConvenientSystem.Shared.Model.YunHan;

namespace ConvenientSystem.Service.YunHan
{
    /// <summary>
    /// 考勤查询业务服务：部门树、考勤汇总、考勤明细与当月排行。
    /// </summary>
    public interface IAttendanceService
    {
        /// <summary>查询全部部门（视图直查）</summary>
        Task<List<DeptView>> GetDeptTreeAsync();

        /// <summary>按人分组汇总考勤数据</summary>
        Task<List<AttendanceSumDto>> GetAttendanceAsync(RequestDto request, CancellationToken ct);

        /// <summary>查询考勤明细数据</summary>
        Task<List<AttendanceDto>> GetAttendanceDtlAsync(RequestDto request);

        /// <summary>当月前 100 名排行；月份格式非 yyyy-MM 时抛 BadRequestException</summary>
        Task<List<AttendanceSumDto>> GetDailyRankingAsync(RequestDto request, CancellationToken ct);
    }
}

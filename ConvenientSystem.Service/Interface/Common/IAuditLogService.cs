using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// 审计日志业务服务：分页查询写操作审计记录。
    /// </summary>
    public interface IAuditLogService
    {
        /// <summary>分页查询审计日志（按时间倒序）。</summary>
        PagedResult<AuditLogDto> GetList(string? account, string? module, bool? success,
            DateTime? startTime, DateTime? endTime, int page, int size);

        /// <summary>按日审计操作趋势（默认近 N 天，带数据权限过滤）。</summary>
        SendTrendDto GetTrend(int days);

        /// <summary>按日登录活跃趋势：基于审计日志中的登录操作统计近 N 天成功/失败登录次数。</summary>
        SendTrendDto GetLoginTrend(int days);
    }
}

using ConvenientSystem.Shared.Model.Common;
using ConvenientSystem.Shared.Model.Sms;

namespace ConvenientSystem.Service.Sms
{
    /// <summary>
    /// 短信发送日志业务服务：日志分页查询与发送量统计。
    /// </summary>
    public interface ISmsLogService
    {
        /// <summary>按条件分页查询发送日志（手机号脱敏返回）</summary>
        PagedResult<SmsLogDto> GetList(int? taskId, string? phone, byte? status,
            DateTime? startTime, DateTime? endTime, int page, int size, string? sortField = null, string? sortOrder = null);

        /// <summary>获取发送统计信息</summary>
        SmsStatisticsDto GetStatistics();

        /// <summary>按日发送趋势（含成功/失败），受数据权限过滤；days 为往前天数（含今天）</summary>
        SendTrendDto GetTrend(int days);

        /// <summary>获取配额使用情况</summary>
        SmsQuotaDto GetQuota();
    }
}

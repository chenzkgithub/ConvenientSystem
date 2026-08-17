namespace ConvenientSystem.Shared.Common.Sms
{
    /// <summary>
    /// 短信配额与频率限制服务契约
    /// </summary>
    public interface ISmsQuotaService
    {
        /// <summary>检查配额是否充足（返回剩余可发条数）</summary>
        (bool ok, string? message, int dailyRemaining, int monthlyRemaining) CheckQuota(int requestCount);

        /// <summary>检查频率限制（同一手机号 1 分钟 1 条、1 小时 5 条）</summary>
        (bool ok, string? message) CheckFrequency(string phone);

        /// <summary>获取当前配额使用情况</summary>
        Model.Sms.SmsQuotaDto GetQuotaStatus();

        /// <summary>获取统计信息</summary>
        Model.Sms.SmsStatisticsDto GetStatistics();
    }
}

using ConvenientSystem.Shared.Entity.Sms;
using FreeSql;

namespace ConvenientSystem.Shared.Common.Sms
{
    /// <summary>
    /// 短信配额与频率限制服务
    /// - 配额：每日/每月上限（从 SmsQuota 表读取）
    /// - 频率：同一手机号 1 分钟 1 条、1 小时 5 条（硬编码）
    /// </summary>
    public class SmsQuotaService : ISmsQuotaService
    {
        private readonly IFreeSql _fsql;

        public SmsQuotaService([FromKeyedServices("ConvenientSystemDb")] IFreeSql fsql)
        {
            _fsql = fsql;
        }

        /// <summary>
        /// 检查配额是否充足（返回剩余可发条数）
        /// </summary>
        public (bool ok, string? message, int dailyRemaining, int monthlyRemaining) CheckQuota(int requestCount)
        {
            var dailyMax = GetMax("Daily", 100);
            var monthlyMax = GetMax("Monthly", 3000);

            var today = _fsql.Select<SmsLogEntity>()
                .Where(l => l.CreateTime >= DateTime.Today && l.CreateTime < DateTime.Today.AddDays(1))
                .Count();
            var monthStart = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            var monthCount = _fsql.Select<SmsLogEntity>()
                .Where(l => l.CreateTime >= monthStart)
                .Count();

            var dailyRemaining = (int)Math.Max(0, dailyMax - today);
            var monthlyRemaining = (int)Math.Max(0, monthlyMax - monthCount);

            if (dailyRemaining < requestCount)
                return (false, $"今日配额不足（剩余 {dailyRemaining} 条，需要 {requestCount} 条）", dailyRemaining, monthlyRemaining);
            if (monthlyRemaining < requestCount)
                return (false, $"本月配额不足（剩余 {monthlyRemaining} 条，需要 {requestCount} 条）", dailyRemaining, monthlyRemaining);

            return (true, null, dailyRemaining, monthlyRemaining);
        }

        /// <summary>
        /// 检查频率限制（同一手机号 1 分钟 1 条、1 小时 5 条）
        /// </summary>
        public (bool ok, string? message) CheckFrequency(string phone)
        {
            var oneMinAgo = DateTime.Now.AddMinutes(-1);
            var oneHourAgo = DateTime.Now.AddHours(-1);

            var minCount = _fsql.Select<SmsLogEntity>()
                .Where(l => l.Phone == phone && l.CreateTime >= oneMinAgo)
                .Count();
            if (minCount >= 1)
                return (false, $"手机号 {phone} 1 分钟内已发送过，请稍后再试");

            var hourCount = _fsql.Select<SmsLogEntity>()
                .Where(l => l.Phone == phone && l.CreateTime >= oneHourAgo)
                .Count();
            if (hourCount >= 5)
                return (false, $"手机号 {phone} 1 小时内已发送 5 条，达到频率上限");

            return (true, null);
        }

        /// <summary>
        /// 获取当前配额使用情况
        /// </summary>
        public Model.Sms.SmsQuotaDto GetQuotaStatus()
        {
            var dailyMax = GetMax("Daily", 100);
            var monthlyMax = GetMax("Monthly", 3000);

            var today = _fsql.Select<SmsLogEntity>()
                .Where(l => l.CreateTime >= DateTime.Today && l.CreateTime < DateTime.Today.AddDays(1))
                .Count();
            var monthStart = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            var monthCount = _fsql.Select<SmsLogEntity>()
                .Where(l => l.CreateTime >= monthStart)
                .Count();

            return new Model.Sms.SmsQuotaDto
            {
                DailyMax = dailyMax,
                MonthlyMax = monthlyMax,
                DailyUsed = (int)today,
                MonthlyUsed = (int)monthCount
            };
        }

        /// <summary>
        /// 获取统计信息
        /// </summary>
        public Model.Sms.SmsStatisticsDto GetStatistics()
        {
            var dailyMax = GetMax("Daily", 100);
            var todayStart = DateTime.Today;
            var monthStart = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);

            var todayTotal = _fsql.Select<SmsLogEntity>()
                .Where(l => l.CreateTime >= todayStart && l.CreateTime < todayStart.AddDays(1))
                .Count();
            var monthTotal = _fsql.Select<SmsLogEntity>()
                .Where(l => l.CreateTime >= monthStart)
                .Count();
            var todaySuccess = _fsql.Select<SmsLogEntity>()
                .Where(l => l.CreateTime >= todayStart && l.CreateTime < todayStart.AddDays(1) && l.Status == 1)
                .Count();

            var successRate = todayTotal > 0 ? Math.Round((double)todaySuccess / todayTotal * 100, 1) : 100;

            return new Model.Sms.SmsStatisticsDto
            {
                TodayCount = (int)todayTotal,
                MonthCount = (int)monthTotal,
                SuccessRate = successRate,
                DailyRemaining = (int)Math.Max(0, dailyMax - todayTotal)
            };
        }

        private int GetMax(string quotaType, int defaultValue)
        {
            var quota = _fsql.Select<SmsQuotaEntity>()
                .Where(q => q.QuotaType == quotaType)
                .First();
            return quota?.MaxCount ?? defaultValue;
        }
    }
}

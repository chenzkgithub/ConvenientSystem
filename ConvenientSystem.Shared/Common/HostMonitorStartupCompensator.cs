using ConvenientSystem.Shared.Jobs;
using Hangfire;

namespace ConvenientSystem.Shared.Common
{
    /// <summary>
    /// 应用启动时注册主机资源监控定时任务：
    /// - 每分钟巡检一次，对到期的启用目标采集本机指标（各目标间隔由 IntervalMinutes 控制）
    /// </summary>
    public class HostMonitorStartupCompensator
    {
        private readonly ILogger<HostMonitorStartupCompensator> _logger;

        public HostMonitorStartupCompensator(ILogger<HostMonitorStartupCompensator> logger)
        {
            _logger = logger;
        }

        /// <summary>启动时注册主机监控巡检定时任务</summary>
        public void Compensate()
        {
            // Cron: 分 时 日 月 星期；每分钟触发，具体目标是否到期由 Job 内部按 IntervalMinutes 判定
            RecurringJob.AddOrUpdate<HostMonitorCheckJob>(
                "主机监控定时巡检",
                job => job.CheckDueAsync(default),
                "* * * * *",
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai")
                });

            _logger.LogInformation("主机监控启动补偿：定时巡检任务已注册，Cron=* * * * *");
        }
    }
}

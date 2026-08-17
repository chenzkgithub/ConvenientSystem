using ConvenientSystem.Shared.Jobs;
using FreeSql;
using Hangfire;

namespace ConvenientSystem.Shared.Common.Sms
{
    /// <summary>
    /// 应用启动时补偿：扫描数据库中所有"待执行"的短信任务，
    /// 重新 Schedule 到 Hangfire，避免应用重启后任务丢失。
    /// </summary>
    public class SmsStartupCompensator
    {
        private readonly IFreeSql _fsql;
        private readonly ILogger<SmsStartupCompensator> _logger;

        public SmsStartupCompensator(
            [FromKeyedServices("ConvenientSystemDb")] IFreeSql fsql,
            ILogger<SmsStartupCompensator> logger)
        {
            _fsql = fsql;
            _logger = logger;
        }

        /// <summary>
        /// 补偿待执行任务（在应用启动后调用一次）
        /// </summary>
        public void Compensate()
        {
            var pendingTasks = _fsql.Select<Entity.Sms.SmsTaskEntity>()
                .Where(t => t.Status == 0 && t.SendTime > DateTime.Now)
                .ToList();

            if (pendingTasks.Count == 0)
            {
                _logger.LogInformation("短信启动补偿：无待执行任务");
                return;
            }

            _logger.LogInformation("短信启动补偿：发现 {Count} 个待执行任务", pendingTasks.Count);

            foreach (var task in pendingTasks)
            {
                var delay = task.SendTime - DateTime.Now;
                var jobId = BackgroundJob.Schedule<SmsSendJob>(
                    job => job.SendAsync(task.Id, default),
                    delay);

                _fsql.Update<Entity.Sms.SmsTaskEntity>()
                    .Set(t => t.HangfireJobId, jobId)
                    .Set(t => t.UpdateTime, DateTime.Now)
                    .Where(t => t.Id == task.Id)
                    .ExecuteAffrows();

                _logger.LogInformation("短信启动补偿：任务 {TaskId} 已重新 Schedule，HangfireJobId={JobId}，延迟 {Delay}",
                    task.Id, jobId, delay);
            }
        }
    }
}

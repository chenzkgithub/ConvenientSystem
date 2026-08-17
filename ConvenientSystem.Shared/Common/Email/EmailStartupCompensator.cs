using ConvenientSystem.Shared.Entity.Email;
using ConvenientSystem.Shared.Jobs;
using FreeSql;
using Hangfire;

namespace ConvenientSystem.Shared.Common.Email
{
    /// <summary>
    /// 应用启动时补偿：扫描数据库中所有启用的邮件定时任务，
    /// 重新注册到 Hangfire，避免重启后任务丢失。
    /// </summary>
    public class EmailStartupCompensator
    {
        private readonly IFreeSql _fsql;
        private readonly ILogger<EmailStartupCompensator> _logger;

        public EmailStartupCompensator(
            [FromKeyedServices("ConvenientSystemDb")] IFreeSql fsql,
            ILogger<EmailStartupCompensator> logger)
        {
            _fsql = fsql;
            _logger = logger;
        }

        /// <summary>
        /// 启动时重新注册所有启用的邮件定时任务
        /// </summary>
        public void Compensate()
        {
            var enabledTasks = _fsql.Select<EmailTaskEntity>()
                .Where(t => t.Enabled)
                .ToList();

            if (enabledTasks.Count == 0)
            {
                _logger.LogInformation("邮件启动补偿：无启用的定时任务");
                return;
            }

            _logger.LogInformation("邮件启动补偿：发现 {Count} 个启用的定时任务", enabledTasks.Count);

            foreach (var task in enabledTasks)
            {
                var jobId = ScheduleJob(task);
                if (!string.IsNullOrEmpty(jobId))
                {
                    // 更新 HangfireJobId
                    _fsql.Update<EmailTaskEntity>()
                        .Set(t => t.HangfireJobId, jobId)
                        .Where(t => t.Id == task.Id)
                        .ExecuteAffrows();

                    _logger.LogInformation("邮件启动补偿：任务 {TaskId}({Name}) 已重新注册，JobId={JobId}",
                        task.Id, task.Name, jobId);
                }
            }
        }

        private string? ScheduleJob(EmailTaskEntity task)
        {
            // 用任务名称作为 Job ID，在 Hangfire Dashboard 中一目了然
            var jobIdStr = $"邮件-{task.Name}";

            switch (task.ScheduleType)
            {
                case "once":
                    if (task.SendTime.HasValue && task.SendTime.Value > DateTime.Now)
                    {
                        var delay = task.SendTime.Value - DateTime.Now;
                        return BackgroundJob.Schedule<EmailSendJob>(
                            job => job.SendAsync(task.Id, default), delay);
                    }
                    else if (task.SendTime.HasValue)
                    {
                        // 时间已过，跳过
                        _logger.LogWarning("邮件启动补偿：任务 {TaskId} 的定时时间已过，跳过", task.Id);
                        return null;
                    }
                    return null;

                case "daily":
                    {
                        var time = ParseDailyTime(task.DailyTime);
                        var cron = $"{time.Minutes} {time.Hours} * * *";
                        RecurringJob.AddOrUpdate<EmailSendJob>(
                            jobIdStr,
                            job => job.SendAsync(task.Id, default),
                            cron,
                            new RecurringJobOptions { TimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai") });
                        return jobIdStr;
                    }

                case "weekly":
                    {
                        var time = ParseDailyTime(task.DailyTime);
                        var days = ParseWeekDays(task.WeekDays);
                        var daysStr = string.Join(",", days);
                        var cron = $"{time.Minutes} {time.Hours} * * {daysStr}";
                        RecurringJob.AddOrUpdate<EmailSendJob>(
                            jobIdStr,
                            job => job.SendAsync(task.Id, default),
                            cron,
                            new RecurringJobOptions { TimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai") });
                        return jobIdStr;
                    }

                case "cron":
                    if (!string.IsNullOrWhiteSpace(task.CronExpression))
                    {
                        RecurringJob.AddOrUpdate<EmailSendJob>(
                            jobIdStr,
                            job => job.SendAsync(task.Id, default),
                            task.CronExpression.Trim(),
                            new RecurringJobOptions { TimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai") });
                        return jobIdStr;
                    }
                    return null;

                default:
                    return null;
            }
        }

        private static TimeSpan ParseDailyTime(string? dailyTime)
        {
            if (string.IsNullOrWhiteSpace(dailyTime)) return new TimeSpan(9, 0, 0);
            var parts = dailyTime.Split(':');
            if (parts.Length >= 2 && int.TryParse(parts[0], out var h) && int.TryParse(parts[1], out var m))
                return new TimeSpan(h, m, 0);
            return new TimeSpan(9, 0, 0);
        }

        private static List<int> ParseWeekDays(string? weekDays)
        {
            if (string.IsNullOrWhiteSpace(weekDays)) return [1, 2, 3, 4, 5];
            return weekDays.Split(',')
                .Select(s => s.Trim())
                .Where(s => int.TryParse(s, out _))
                .Select(int.Parse)
                .ToList();
        }
    }
}

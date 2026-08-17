using ConvenientSystem.Shared.Common.Email;
using ConvenientSystem.Shared.Common.Webhook;
using ConvenientSystem.Shared.Entity.Email;
using FreeSql;
using Hangfire;

namespace ConvenientSystem.Shared.Jobs
{
    /// <summary>
    /// 邮件发送 Hangfire Job
    /// - 由 EmailTaskController 创建任务时 Schedule / AddOrUpdate 到 Hangfire
    /// - 读取任务配置，替换变量后调用 EmailService 发送
    /// - 发送结果写入 EmailLog
    /// </summary>
    public class EmailSendJob
    {
        private readonly IFreeSql _fsql;
        private readonly IEmailService _emailService;
        private readonly WebhookNotifier _notifier;
        private readonly ILogger<EmailSendJob> _logger;

        public EmailSendJob(
            [FromKeyedServices("ConvenientSystemDb")] IFreeSql fsql,
            IEmailService emailService,
            WebhookNotifier notifier,
            ILogger<EmailSendJob> logger)
        {
            _fsql = fsql;
            _emailService = emailService;
            _notifier = notifier;
            _logger = logger;
        }

        /// <summary>
        /// 执行邮件发送任务
        /// </summary>
        [AutomaticRetry(Attempts = 2, DelaysInSeconds = new[] { 60, 300 })]
        public async Task SendAsync(int taskId, CancellationToken ct = default)
        {
            var task = _fsql.Select<EmailTaskEntity>()
                .Where(t => t.Id == taskId)
                .First();

            if (task == null)
            {
                _logger.LogWarning("邮件任务 {TaskId} 不存在", taskId);
                return;
            }

            // 变量替换
            var now = DateTime.Now;
            var variables = new Dictionary<string, string>
            {
                ["日期"] = now.ToString("yyyy-MM-dd"),
                ["时间"] = now.ToString("HH:mm:ss"),
                ["星期"] = GetWeekDayName(now.DayOfWeek)
            };
            var subject = RenderTemplate(task.Subject, variables);
            var content = RenderTemplate(task.Content, variables);

            // 发送
            var result = await _emailService.SendAsync(task.Recipients, subject, content);

            // 写日志
            var log = new EmailLogEntity
            {
                TaskId = taskId,
                TaskName = task.Name,
                Recipients = task.Recipients,
                Subject = subject,
                Content = content,
                Status = result.Success ? (byte)1 : (byte)0,
                ErrorMessage = result.ErrorMessage,
                CostMs = result.CostMs,
                CreatedById = task.CreatedById,
                CreateTime = DateTime.Now
            };
            _fsql.Insert(log).ExecuteAffrows();

            // 更新任务上次发送时间
            _fsql.Update<EmailTaskEntity>()
                .Set(t => t.LastSendTime, now)
                .Set(t => t.UpdateTime, now)
                .Where(t => t.Id == taskId)
                .ExecuteAffrows();

            if (result.Success)
            {
                // 邮件发送成功后，把邮件内容同步推送到默认机器人
                await _notifier.SendToDefaultAsync(subject, content);
            }
            else
            {
                // 推送默认机器人失败提醒（异常已在内部吞掉），再抛出交给 Hangfire 重试
                await _notifier.SendToDefaultAsync("邮件任务执行失败提醒",
                    $"任务ID：{taskId}\n任务名称：{task.Name}\n错误：{result.ErrorMessage}\n时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                throw new Exception($"邮件发送失败：{result.ErrorMessage}");
            }
        }

        /// <summary>
        /// 测试发送（不关联任务，不写日志）
        /// </summary>
        public async Task<EmailSendResult> TestSendAsync(string recipients, string subject, string content)
        {
            var now = DateTime.Now;
            var variables = new Dictionary<string, string>
            {
                ["日期"] = now.ToString("yyyy-MM-dd"),
                ["时间"] = now.ToString("HH:mm:ss"),
                ["星期"] = GetWeekDayName(now.DayOfWeek)
            };
            subject = RenderTemplate(subject, variables);
            content = RenderTemplate(content, variables);

            return await _emailService.SendAsync(recipients, subject, content);
        }

        private static string RenderTemplate(string template, Dictionary<string, string> variables)
        {
            var result = template;
            foreach (var kv in variables)
            {
                result = result.Replace($"{{{kv.Key}}}", kv.Value);
            }
            return result;
        }

        private static string GetWeekDayName(DayOfWeek day) => day switch
        {
            DayOfWeek.Sunday => "星期日",
            DayOfWeek.Monday => "星期一",
            DayOfWeek.Tuesday => "星期二",
            DayOfWeek.Wednesday => "星期三",
            DayOfWeek.Thursday => "星期四",
            DayOfWeek.Friday => "星期五",
            DayOfWeek.Saturday => "星期六",
            _ => ""
        };
    }
}

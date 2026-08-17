using ConvenientSystem.Shared.Common.Sms;
using ConvenientSystem.Shared.Common.Webhook;
using ConvenientSystem.Shared.Entity.Sms;
using FreeSql;
using Hangfire;

namespace ConvenientSystem.Shared.Jobs
{
    /// <summary>
    /// 短信发送 Hangfire Job
    /// - 由 SmsTaskController 创建任务时 Schedule 到 Hangfire
    /// - 自动重试 3 次（1 分钟、5 分钟、15 分钟间隔）
    /// - 遍历任务下所有待发送收件人，逐条调用 ISmsProvider（通过 SmsProviderFactory 动态选择）
    /// </summary>
    public class SmsSendJob
    {
        private readonly IFreeSql _fsql;
        private readonly ISmsProviderFactory _providerFactory;
        private readonly ISmsQuotaService _quotaService;
        private readonly WebhookNotifier _notifier;
        private readonly ILogger<SmsSendJob> _logger;

        public SmsSendJob(
            [FromKeyedServices("ConvenientSystemDb")] IFreeSql fsql,
            ISmsProviderFactory providerFactory,
            ISmsQuotaService quotaService,
            WebhookNotifier notifier,
            ILogger<SmsSendJob> logger)
        {
            _fsql = fsql;
            _providerFactory = providerFactory;
            _quotaService = quotaService;
            _notifier = notifier;
            _logger = logger;
        }

        /// <summary>
        /// 执行短信发送任务
        /// </summary>
        [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 60, 300, 900 })]
        public async Task SendAsync(int taskId, CancellationToken ct = default)
        {
            _logger.LogInformation("开始执行短信任务 {TaskId}", taskId);

            var task = _fsql.Select<Entity.Sms.SmsTaskEntity>()
                .Where(t => t.Id == taskId)
                .First();
            if (task == null)
            {
                _logger.LogWarning("短信任务 {TaskId} 不存在", taskId);
                return;
            }

            // 已取消则跳过
            if (task.Status == 3)
            {
                _logger.LogInformation("短信任务 {TaskId} 已被取消，跳过执行", taskId);
                return;
            }

            var template = _fsql.Select<SmsTemplateEntity>()
                .Where(t => t.Id == task.TemplateId)
                .First();
            if (template == null)
            {
                _logger.LogError("短信任务 {TaskId} 关联的模板 {TemplateId} 不存在", taskId, task.TemplateId);
                UpdateTaskStatus(taskId, 4, "模板不存在");
                return;
            }

            // 标记为执行中
            _fsql.Update<Entity.Sms.SmsTaskEntity>()
                .Set(t => t.Status, (byte)1)
                .Set(t => t.UpdateTime, DateTime.Now)
                .Where(t => t.Id == taskId)
                .ExecuteAffrows();

            var recipients = _fsql.Select<SmsRecipientEntity>()
                .Where(r => r.TaskId == taskId && r.Status == 0)
                .ToList();

            int successCount = 0;
            int failCount = 0;

            foreach (var recipient in recipients)
            {
                ct.ThrowIfCancellationRequested();

                // 频率检查
                var freqCheck = _quotaService.CheckFrequency(recipient.Phone);
                if (!freqCheck.ok)
                {
                    MarkRecipientFail(recipient.Id, freqCheck.message);
                    failCount++;
                    continue;
                }

                // 变量替换
                var variables = new Dictionary<string, string>
                {
                    ["姓名"] = recipient.Name,
                    ["公司"] = "义乌市昀晗贸易有限公司"
                };
                var content = SmsTemplateRenderer.Render(template.Content, variables);

                // 发送
                var result = await _providerFactory.GetProvider().SendAsync(recipient.Phone, content, template.Signature);

                // 写日志
                var log = new SmsLogEntity
                {
                    TaskId = taskId,
                    RecipientId = recipient.Id,
                    Phone = recipient.Phone,
                    Content = content,
                    ProviderMsgId = result.ProviderMsgId,
                    Status = (byte)(result.Success ? 1 : 0),
                    ErrorMessage = result.ErrorMessage,
                    CostMs = result.CostMs
                };
                _fsql.Insert(log).ExecuteAffrows();

                // 更新收件人状态
                if (result.Success)
                {
                    _fsql.Update<SmsRecipientEntity>()
                        .Set(r => r.Status, (byte)1)
                        .Set(r => r.SentTime, DateTime.Now)
                        .Where(r => r.Id == recipient.Id)
                        .ExecuteAffrows();
                    successCount++;
                }
                else
                {
                    MarkRecipientFail(recipient.Id, result.ErrorMessage);
                    failCount++;
                }

                // 发送间隔 200ms，避免触发阿里云 QPS 限制
                await Task.Delay(200, ct);
            }

            // 更新任务统计
            _fsql.Update<Entity.Sms.SmsTaskEntity>()
                .Set(t => t.Status, failCount == 0 ? (byte)2 : (byte)4)
                .Set(t => t.SuccessCount, successCount)
                .Set(t => t.FailCount, failCount)
                .Set(t => t.UpdateTime, DateTime.Now)
                .Where(t => t.Id == taskId)
                .ExecuteAffrows();

            _logger.LogInformation("短信任务 {TaskId} 完成：成功 {Success} 失败 {Fail}",
                taskId, successCount, failCount);

            // 有失败则推送群机器人（不阻断任务，异常已在内部吞掉）
            if (failCount > 0)
            {
                await _notifier.SendToDefaultAsync("短信任务执行失败提醒",
                    $"任务ID：{taskId}\n任务名称：{task.Name}\n成功：{successCount}  失败：{failCount}\n时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            }
        }

        private void MarkRecipientFail(int recipientId, string? errorMsg)
        {
            _fsql.Update<SmsRecipientEntity>()
                .Set(r => r.Status, (byte)2)
                .Set(r => r.ErrorMessage, errorMsg)
                .Set(r => r.SentTime, DateTime.Now)
                .Where(r => r.Id == recipientId)
                .ExecuteAffrows();
        }

        private void UpdateTaskStatus(int taskId, byte status, string? errorMsg = null)
        {
            _fsql.Update<Entity.Sms.SmsTaskEntity>()
                .Set(t => t.Status, status)
                .Set(t => t.UpdateTime, DateTime.Now)
                .Where(t => t.Id == taskId)
                .ExecuteAffrows();
        }
    }
}

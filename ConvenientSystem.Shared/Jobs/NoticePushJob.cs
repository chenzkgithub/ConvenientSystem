using System.Net;
using ConvenientSystem.Shared.Common;
using ConvenientSystem.Shared.Common.Email;
using ConvenientSystem.Shared.Common.Sms;
using ConvenientSystem.Shared.Common.Webhook;
using ConvenientSystem.Shared.Entity.Common;
using ConvenientSystem.Shared.Entity.Email;
using ConvenientSystem.Shared.Entity.Sms;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ConvenientSystem.Shared.Jobs
{
    /// <summary>
    /// 系统通知联动推送 Job：新通知发布时异步执行邮件/短信/群机器人推送（按发布时勾选的开关）。
    /// 各通道相互独立，失败只记警告日志互不影响，且不向外抛异常（避免 Hangfire 无限重试）。
    /// </summary>
    public class NoticePushJob : JobBase
    {
        /// <summary>邮件日志中的任务名（与邮件任务区分，TaskId 固定为 0）</summary>
        private const string EmailTaskName = "系统通知推送";

        private readonly IEmailService _emailService;
        private readonly ISmsProviderFactory _smsProviderFactory;
        private readonly ISmsQuotaService _smsQuotaService;
        private readonly WebhookNotifier _webhookNotifier;
        private readonly ILogger<NoticePushJob> _logger;

        public NoticePushJob(
            [FromKeyedServices("ConvenientSystemDb")] IFreeSql fsql,
            IJobExecutionLogService jobLog,
            IEmailService emailService,
            ISmsProviderFactory smsProviderFactory,
            ISmsQuotaService smsQuotaService,
            WebhookNotifier webhookNotifier,
            ILogger<NoticePushJob> logger) : base(fsql, jobLog)
        {
            _emailService = emailService;
            _smsProviderFactory = smsProviderFactory;
            _smsQuotaService = smsQuotaService;
            _webhookNotifier = webhookNotifier;
            _logger = logger;
        }

        /// <summary>对指定通知执行联动推送（邮件/短信/群机器人按开关执行）。</summary>
        public Task PushAsync(int noticeId)
            => ExecuteWithLog("系统通知推送", nameof(PushAsync), noticeId, async () =>
        {
            var notice = Fsql.Select<SysNoticeEntity>().Where(n => n.Id == noticeId).First();
            if (notice == null || !notice.Enabled) return;
            if (!notice.SendEmail && !notice.SendSms && !notice.SendWebhook) return;

            var users = FilterByScope(noticeId, Fsql.Select<SysUserEntity>().Where(u => u.Enabled).ToList());
            // 外部推送（邮件/短信）跳过发布人本人：自己刚发布的内容无需再发到自己的邮箱/手机；
            // 站内可见性无此排除——发布人同样能看到自己的通知（见 NoticeService.GetMyList）
            users = users.Where(u => u.Id != notice.CreatedById).ToList();
            if (users.Count == 0)
            {
                _logger.LogInformation("系统通知[{Id}]推送跳过：定向范围内无启用用户", noticeId);
                return;
            }

            if (notice.SendEmail)
                await PushEmailAsync(notice, users);
            if (notice.SendSms)
                await PushSmsAsync(notice, users);
            if (notice.SendWebhook)
                await _webhookNotifier.SendToDefaultAsync(notice.Title, notice.Content);
        });

        /// <summary>按通知定向范围过滤接收用户：未定向（用户表与角色表均无记录）时全员接收。</summary>
        private List<SysUserEntity> FilterByScope(int noticeId, List<SysUserEntity> users)
        {
            var userTargets = Fsql.Select<SysNoticeUserEntity>()
                .Where(t => t.NoticeId == noticeId).ToList();
            var roleTargets = Fsql.Select<SysNoticeRoleEntity>()
                .Where(t => t.NoticeId == noticeId).ToList();
            if (userTargets.Count == 0 && roleTargets.Count == 0) return users;

            var directUserIds = userTargets.Select(t => t.UserId).ToHashSet();
            var roleUserIds = new HashSet<Guid>();
            if (roleTargets.Count > 0)
            {
                var roleIds = roleTargets.Select(t => t.RoleId).ToList();
                roleUserIds = Fsql.Select<SysUserRoleEntity>()
                    .Where(r => roleIds.Contains(r.RoleId))
                    .ToList(r => r.UserId).ToHashSet();
            }

            return users.Where(u => directUserIds.Contains(u.Id) || roleUserIds.Contains(u.Id)).ToList();
        }

        /// <summary>邮件通道：向范围内已填邮箱的启用用户发送，结果写入 EmailLog。</summary>
        private async Task PushEmailAsync(SysNoticeEntity notice, List<SysUserEntity> users)
        {
            try
            {
                var recipients = users
                    .Where(u => !string.IsNullOrWhiteSpace(u.Email))
                    .Select(u => u.Email!.Trim())
                    .Distinct()
                    .ToList();
                if (recipients.Count == 0)
                {
                    _logger.LogInformation("系统通知[{Id}]邮件推送跳过：无已填邮箱的用户", notice.Id);
                    return;
                }

                var subject = $"【系统通知】{notice.Title}";
                var body = "<div style=\"white-space:pre-wrap;\">" + WebUtility.HtmlEncode(notice.Content) + "</div>";
                var result = await _emailService.SendAsync(string.Join(";", recipients), subject, body);

                Fsql.Insert(new EmailLogEntity
                {
                    TaskId = 0,
                    TaskName = EmailTaskName,
                    Recipients = string.Join(";", recipients),
                    Subject = subject,
                    Content = notice.Content.Length > 2000 ? notice.Content[..2000] : notice.Content,
                    Status = (byte)(result.Success ? 1 : 0),
                    ErrorMessage = result.ErrorMessage,
                    CostMs = result.CostMs
                }).ExecuteAffrows();

                if (!result.Success)
                    _logger.LogWarning("系统通知[{Id}]邮件推送失败：{Error}", notice.Id, result.ErrorMessage);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "系统通知[{Id}]邮件推送异常", notice.Id);
            }
        }

        /// <summary>短信通道：向范围内已填合法手机号的启用用户发送（受配额与频率限制约束）。</summary>
        private async Task PushSmsAsync(SysNoticeEntity notice, List<SysUserEntity> users)
        {
            try
            {
                var phones = users
                    .Where(u => SmsPhoneHelper.IsValid(u.Phone ?? string.Empty))
                    .Select(u => u.Phone!.Trim())
                    .Distinct()
                    .ToList();
                if (phones.Count == 0)
                {
                    _logger.LogInformation("系统通知[{Id}]短信推送跳过：无已填手机号的用户", notice.Id);
                    return;
                }

                var (quotaOk, quotaMsg, _, _) = _smsQuotaService.CheckQuota(phones.Count);
                if (!quotaOk)
                {
                    _logger.LogWarning("系统通知[{Id}]短信推送跳过：{Msg}", notice.Id, quotaMsg);
                    return;
                }

                var signature = Fsql.Select<SmsProviderConfigEntity>()
                    .OrderByDescending(c => c.Id).First()?.DefaultSignature ?? "zk";

                // 短信按单条长度控制：标题+正文超长截断
                var content = $"{notice.Title}：{notice.Content}";
                if (content.Length > 70) content = content[..67] + "...";

                var provider = _smsProviderFactory.GetProvider();
                foreach (var phone in phones)
                {
                    var (freqOk, freqMsg) = _smsQuotaService.CheckFrequency(phone);
                    if (!freqOk)
                    {
                        _logger.LogWarning("系统通知[{Id}]短信跳过 {Phone}：{Msg}", notice.Id, SmsPhoneHelper.Mask(phone), freqMsg);
                        continue;
                    }

                    var result = await provider.SendAsync(phone, content, signature);
                    if (!result.Success)
                        _logger.LogWarning("系统通知[{Id}]短信推送失败 {Phone}：{Error}", notice.Id, SmsPhoneHelper.Mask(phone), result.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "系统通知[{Id}]短信推送异常", notice.Id);
            }
        }
    }
}

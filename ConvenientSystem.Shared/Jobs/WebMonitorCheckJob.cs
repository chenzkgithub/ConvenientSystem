using ConvenientSystem.Shared.Common;
using ConvenientSystem.Shared.Common.Email;
using ConvenientSystem.Shared.Entity.Common;
using ConvenientSystem.Shared.Entity.Email;
using FreeSql;
using Hangfire;
using System.Diagnostics;
using System.Text;

namespace ConvenientSystem.Shared.Jobs
{
    /// <summary>
    /// 网站/API 监控探测 Hangfire 定时 Job（每分钟触发一次）：
    /// - 遍历启用的监控目标，按各自探测间隔判定是否到期，到期则发起 HTTP 探测
    /// - 探测判定：HTTP 状态码 = 期望值，且配置了期望关键字时响应体必须包含（不区分大小写）
    /// - 每次探测写 WebMonitorLog（保留 30 天，每日凌晨清理），并回写目标最近状态/耗时/时间
    /// - 状态变化（正常↔异常）且开启邮件告警时，给拥有 web-monitor 菜单权限的有邮箱用户发送告警/恢复邮件
    /// </summary>
    public class WebMonitorCheckJob : JobBase
    {
        private readonly IEmailService _emailService;
        private readonly ILogger<WebMonitorCheckJob> _logger;

        /// <summary>系统告警在 EmailLog 中的任务名</summary>
        private const string TaskName = "网站监控告警";

        /// <summary>探测结果：正常</summary>
        public const byte StatusOk = 1;
        /// <summary>探测结果：异常</summary>
        public const byte StatusFail = 2;

        /// <summary>共享 HttpClient：超时由各目标 TimeoutSeconds 通过 CTS 单独控制</summary>
        private static readonly HttpClient Http = new(new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
            ConnectTimeout = TimeSpan.FromSeconds(30)
        })
        { Timeout = Timeout.InfiniteTimeSpan };

        public WebMonitorCheckJob(
            [FromKeyedServices("ConvenientSystemDb")] IFreeSql fsql,
            IJobExecutionLogService jobLog,
            IEmailService emailService,
            ILogger<WebMonitorCheckJob> logger) : base(fsql, jobLog)
        {
            _emailService = emailService;
            _logger = logger;
        }

        /// <summary>每分钟巡检：探测所有到期的启用目标；每天凌晨 3 点清理 30 天前的探测日志</summary>
        [AutomaticRetry(Attempts = 0)]
        public Task CheckDueAsync(CancellationToken ct = default)
            => ExecuteWithLog("网站监控定时巡检", nameof(CheckDueAsync), null, async () =>
        {
            var now = DateTime.Now;

            // 每天凌晨 3:00 档清理过期日志（保留 30 天）
            if (now.Hour == 3 && now.Minute == 0)
            {
                var removed = Fsql.Delete<WebMonitorLogEntity>()
                    .Where(l => l.CheckAt < now.AddDays(-30))
                    .ExecuteAffrows();
                if (removed > 0)
                    _logger.LogInformation("网站监控：已清理 30 天前的探测日志 {Count} 条", removed);
            }

            var targets = Fsql.Select<WebMonitorTargetEntity>()
                .Where(t => t.Enabled)
                .ToList();
            // 按各自探测间隔判定到期（未探测过的立即探测）
            var due = targets
                .Where(t => t.LastCheckAt == null || t.LastCheckAt <= now.AddMinutes(-Math.Max(1, t.IntervalMinutes)))
                .ToList();
            if (due.Count == 0) return;

            foreach (var target in due)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    await CheckTargetAsync(target, notify: true, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "网站监控：{Name} 探测过程发生未预期异常", target.Name);
                }
            }
        });

        /// <summary>
        /// 对单个目标执行一次探测：写探测日志、回写最近状态；状态变化时可选邮件告警。
        /// 供定时巡检与页面"立即检测"共用。
        /// </summary>
        public async Task<WebMonitorLogEntity> CheckTargetAsync(WebMonitorTargetEntity target, bool notify, CancellationToken ct = default)
        {
            var prevStatus = target.LastStatus;
            int? statusCode = null;
            int? latencyMs = null;
            string? error = null;

            var sw = Stopwatch.StartNew();
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, target.TimeoutSeconds)));

                using var req = new HttpRequestMessage(new HttpMethod(target.Method), target.Url);
                using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
                statusCode = (int)resp.StatusCode;

                // 配置了期望关键字时需读取响应体校验（HEAD 无响应体，关键字校验直接视为不通过）
                if (!string.IsNullOrWhiteSpace(target.ExpectKeyword))
                {
                    var body = await resp.Content.ReadAsStringAsync(timeoutCts.Token);
                    if (!body.Contains(target.ExpectKeyword, StringComparison.OrdinalIgnoreCase))
                        error = $"响应体未包含关键字「{target.ExpectKeyword}」";
                }

                if (error == null && statusCode != target.ExpectStatus)
                    error = $"状态码 {statusCode}，期望 {target.ExpectStatus}";
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                error = $"探测超时（{target.TimeoutSeconds} 秒）";
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                error = Truncate(ex.InnerException?.Message ?? ex.Message);
            }
            sw.Stop();
            latencyMs = (int)sw.ElapsedMilliseconds;

            var newStatus = error == null ? StatusOk : StatusFail;
            var now = DateTime.Now;

            var log = new WebMonitorLogEntity
            {
                TargetId = target.Id,
                Status = newStatus,
                HttpStatusCode = statusCode,
                LatencyMs = latencyMs,
                ErrorMsg = error,
                CheckAt = now
            };
            log.Id = Fsql.Insert(log).ExecuteIdentity();

            // 回写目标最近状态
            Fsql.Update<WebMonitorTargetEntity>()
                .Set(t => t.LastStatus, newStatus)
                .Set(t => t.LastLatencyMs, latencyMs)
                .Set(t => t.LastErrorMsg, error)
                .Set(t => t.LastCheckAt, now)
                .Where(t => t.Id == target.Id)
                .ExecuteAffrows();
            target.LastStatus = newStatus;
            target.LastLatencyMs = latencyMs;
            target.LastErrorMsg = error;
            target.LastCheckAt = now;

            if (error != null)
                _logger.LogWarning("网站监控：{Name} 探测异常：{Error}（耗时 {Ms}ms）", target.Name, error, latencyMs);

            // 状态变化（首次探测不告警）且开启邮件通知时发送告警/恢复邮件
            if (notify && target.NotifyEmail && prevStatus.HasValue && prevStatus.Value != newStatus)
            {
                try
                {
                    await NotifyStatusChangeAsync(target, newStatus, error, latencyMs, now, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "网站监控：{Name} 状态告警邮件发送失败", target.Name);
                }
            }

            return log;
        }

        /// <summary>状态变化告警邮件：收件人 = 启用且有邮箱且拥有 web-monitor 菜单权限的用户</summary>
        private async Task NotifyStatusChangeAsync(WebMonitorTargetEntity target, byte status,
            string? error, int? latencyMs, DateTime checkAt, CancellationToken ct)
        {
            var recipients = GetAlertRecipients();
            if (recipients.Count == 0) return;

            var isOk = status == StatusOk;
            var subject = isOk
                ? $"【网站监控】{target.Name} 已恢复正常"
                : $"【网站监控告警】{target.Name} 探测异常";

            var sb = new StringBuilder();
            sb.Append("<div style=\"font-family:'Microsoft YaHei',sans-serif;font-size:14px;color:#303133;line-height:1.8\">");
            sb.Append($"<div style=\"border-left:4px solid {(isOk ? "#67c23a" : "#f56c6c")};padding:10px 14px;"
                + $"background:{(isOk ? "#f0f9eb" : "#fef0f0")};border-radius:4px;font-weight:bold\">");
            sb.Append(isOk ? "监控目标已恢复正常" : "监控目标探测异常，请及时关注");
            sb.Append("</div>");
            sb.Append("<table border=\"0\" cellspacing=\"0\" cellpadding=\"0\" style=\"border-collapse:collapse;margin-top:12px;font-size:13px\">");
            AppendRow(sb, "监控目标", System.Net.WebUtility.HtmlEncode(target.Name));
            AppendRow(sb, "监控地址", $"<a href=\"{System.Net.WebUtility.HtmlEncode(target.Url)}\" style=\"color:#409eff\">{System.Net.WebUtility.HtmlEncode(target.Url)}</a>");
            AppendRow(sb, "探测结果", isOk ? "<b style=\"color:#67c23a\">正常</b>" : "<b style=\"color:#f56c6c\">异常</b>");
            if (error != null) AppendRow(sb, "异常原因", System.Net.WebUtility.HtmlEncode(error));
            AppendRow(sb, "探测耗时", latencyMs.HasValue ? $"{latencyMs} ms" : "—");
            AppendRow(sb, "探测时间", checkAt.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.Append("</table>");
            sb.Append("<p style=\"color:#909399;font-size:12px;margin-top:14px\">本邮件由网站监控自动发送，请勿回复。</p>");
            sb.Append("</div>");

            var sw = Stopwatch.StartNew();
            var result = await _emailService.SendAsync(string.Join(";", recipients), subject, sb.ToString());
            sw.Stop();

            Fsql.Insert(new EmailLogEntity
            {
                TaskId = 0,
                TaskName = TaskName,
                Recipients = string.Join(";", recipients),
                Subject = subject,
                Content = sb.ToString(),
                Status = (byte)(result.Success ? 1 : 0),
                ErrorMessage = result.ErrorMessage,
                CostMs = (int)sw.ElapsedMilliseconds,
            }).ExecuteAffrows();

            if (result.Success)
                _logger.LogInformation("网站监控：{Name} 状态变化告警邮件已发送（{Count} 人）", target.Name, recipients.Count);
            else
                _logger.LogWarning("网站监控：{Name} 告警邮件发送失败：{Err}", target.Name, result.ErrorMessage);
        }

        /// <summary>告警收件人：启用且有邮箱、且通过启用角色拥有 web-monitor 菜单权限的用户邮箱</summary>
        private List<string> GetAlertRecipients()
        {
            var menuId = Fsql.Select<SysMenuEntity>()
                .Where(m => m.Name == "web-monitor")
                .First(m => m.Id);
            if (menuId == 0) return new List<string>();

            var roleIds = Fsql.Select<SysRoleMenuEntity>()
                .Where(rm => rm.MenuId == menuId)
                .ToList(rm => rm.RoleId);
            if (roleIds.Count == 0) return new List<string>();

            var enabledRoleIds = Fsql.Select<SysRoleEntity>()
                .Where(r => r.Enabled)
                .ToList(r => r.Id)
                .Where(id => roleIds.Contains(id))
                .ToHashSet();
            if (enabledRoleIds.Count == 0) return new List<string>();

            var userIds = Fsql.Select<SysUserRoleEntity>()
                .Where(ur => enabledRoleIds.Contains(ur.RoleId))
                .ToList(ur => ur.UserId)
                .Distinct()
                .ToList();
            if (userIds.Count == 0) return new List<string>();

            return Fsql.Select<SysUserEntity>()
                .Where(u => u.Enabled && u.Email != null && u.Email != "" && userIds.Contains(u.Id))
                .ToList(u => u.Email!);
        }

        private static void AppendRow(StringBuilder sb, string label, string value)
            => sb.Append($"<tr><td style=\"padding:6px 12px;color:#909399;white-space:nowrap\">{label}</td>"
                + $"<td style=\"padding:6px 12px\">{value}</td></tr>");

        /// <summary>异常信息截断至列宽（500）</summary>
        private static string Truncate(string s) => s.Length <= 500 ? s : s[..500];
    }
}

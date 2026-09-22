using ConvenientSystem.Shared.Common;
using ConvenientSystem.Shared.Common.Email;
using ConvenientSystem.Shared.Entity.Common;
using ConvenientSystem.Shared.Entity.Email;
using ConvenientSystem.Shared.Model.Common;
using FreeSql;
using Hangfire;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text;


namespace ConvenientSystem.Shared.Jobs
{
    /// <summary>
    /// 每天定时为当天开奖的彩种随机生成选号记录。
    /// 仅面向在「个人配置 → 彩票设置」勾选了“定时生成选号”（UserConfig: Lottery.RandomPick=true）的启用用户，
    /// 每个彩种生成 10 注随机号码，保存到 LotteryRecordEntity 表，
    /// 归属到下一期（期号 = 最新开奖期号 + 1，开奖日 = 最近开奖日）。
    /// 生成完成后将本人全部选号整合为一封邮件发送到用户自己绑定的邮箱（样式与开奖结果通知邮件一致）。
    /// </summary>
    public class LotteryRandomPickJob : JobBase
    {
        private readonly IEmailService _emailService;
        private readonly ILogger<LotteryRandomPickJob> _logger;

        /// <summary>个人配置键：是否定时生成选号（勾选才参与）</summary>
        public const string RandomPickConfigKey = "Lottery.RandomPick";

        /// <summary>邮件在 EmailLog 中的任务名（用于日志展示）</summary>
        private const string TaskName = "随机选号通知";

        public LotteryRandomPickJob(
            [FromKeyedServices("ConvenientSystemDb")] IFreeSql fsql,
            IJobExecutionLogService jobLog,
            IEmailService emailService,
            ILogger<LotteryRandomPickJob> logger) : base(fsql, jobLog)
        {
            _emailService = emailService;
            _logger = logger;
        }

        /// <summary>
        /// 每天下午 1 点执行：为当天开奖的每个彩种、勾选了“定时生成选号”的启用用户随机生成 10 注选号记录，
        /// 并将本人选号汇总为一封邮件发送到本人邮箱。
        /// </summary>
        [AutomaticRetry(Attempts = 2, DelaysInSeconds = new[] { 300, 900 })]
        public Task DailyRandomPickAsync(CancellationToken ct = default)
            => ExecuteWithLog("随机生成彩种号码", nameof(DailyRandomPickAsync), null, async () =>
        {
            var today = DateTime.Today;

            // 判断当天哪些彩种开奖（按开奖星期规则）
            var drawTypes = LotteryTypes.All
                .Where(t => LotteryTypes.GetDrawDays(t).Contains(today.DayOfWeek))
                .ToList();
            if (drawTypes.Count == 0)
            {
                _logger.LogInformation("当天（{Date}）无彩种开奖，跳过随机生成", today.ToString("yyyy-MM-dd"));
                return;
            }

            // 参与用户：勾选了“定时生成选号”的启用用户（个人配置 UserConfig）
            var optedInUserIds = Fsql.Select<UserConfigEntity>()
                .Where(c => c.ConfigKey == RandomPickConfigKey && c.ConfigValue == "true")
                .ToList(c => c.UserId)
                .ToHashSet();
            var users = Fsql.Select<SysUserEntity>()
                .Where(u => u.Enabled)
                .ToList()
                .Where(u => optedInUserIds.Contains(u.Id))
                .ToList();
            if (users.Count == 0)
            {
                _logger.LogInformation("无勾选「定时生成选号」的启用用户，跳过随机生成");
                return;
            }

            var totalGenerated = 0;
            // 每个用户本次生成的选号（发邮件用）
            var generatedByUser = new Dictionary<Guid, List<LotteryRecordEntity>>();
            foreach (var t in drawTypes)
            {
                ct.ThrowIfCancellationRequested();
                var typeName = LotteryTypes.GetName(t);
                var positional = LotteryTypes.IsPositional(t);

                // 下一期期号与开奖日（与 LotteryService.GetNextIssueAndDate 同口径）
                var (issue, drawDate) = GetNextIssueAndDate(t);
                if (issue == null || drawDate == null)
                {
                    _logger.LogWarning("{Type}无法确定下一期期号/开奖日，跳过", typeName);
                    continue;
                }

                // 为每个用户生成 10 注
                foreach (var user in users)
                {
                    ct.ThrowIfCancellationRequested();
                    var entities = new List<LotteryRecordEntity>();
                    for (var i = 0; i < 10; i++)
                    {
                        var (front, back) = GenerateRandomNumbers(t);
                        entities.Add(new LotteryRecordEntity
                        {
                            UserId = user.Id,
                            LotteryType = t,
                            FrontNumbers = FormatNumbers(front, positional),
                            BackNumbers = FormatNumbers(back, positional),
                            IssueNumber = issue,
                            DrawDate = drawDate,
                        });
                    }
                    var affrows = await Fsql.Insert(entities).ExecuteAffrowsAsync();
                    totalGenerated += affrows;

                    if (!generatedByUser.TryGetValue(user.Id, out var list))
                        generatedByUser[user.Id] = list = new List<LotteryRecordEntity>();
                    list.AddRange(entities);
                }

                _logger.LogInformation("{Type}已为 {Count} 个用户各生成 10 注随机选号（期号 {Issue}）",
                    typeName, users.Count, issue);
            }

            _logger.LogInformation("当天（{Date}）随机生成完成，共 {Count} 条选号记录",
                today.ToString("yyyy-MM-dd"), totalGenerated);

            // 选号生成完毕，逐用户发送汇总邮件（只发到用户自己绑定的邮箱）
            await SendNotifyEmailsAsync(users, generatedByUser, today, ct);
        });

        // ──────────────────── 邮件通知 ────────────────────

        /// <summary>
        /// 逐用户发送选号汇总邮件：一人一封，收件人为本人邮箱（数据权限：邮件内仅含本人选号）。
        /// 未绑定邮箱只跳过发送（选号记录已生成不受影响）；发送失败写 EmailLog（Status=0）不影响其他用户。
        /// </summary>
        private async Task SendNotifyEmailsAsync(
            List<SysUserEntity> users,
            Dictionary<Guid, List<LotteryRecordEntity>> generatedByUser,
            DateTime today,
            CancellationToken ct)
        {
            var subject = $"随机选号通知 {today:yyyy-MM-dd}";
            var subtitle = $"{today:yyyy-MM-dd} · 当天开奖彩种随机选号";

            var okCount = 0;
            var failCount = 0;
            var skipCount = 0;
            foreach (var user in users)
            {
                ct.ThrowIfCancellationRequested();

                if (!generatedByUser.TryGetValue(user.Id, out var userRecords) || userRecords.Count == 0)
                    continue;

                if (string.IsNullOrEmpty(user.Email))
                {
                    skipCount++;
                    _logger.LogWarning("用户 {Account} 未绑定邮箱，选号已生成但跳过邮件通知", user.Account);
                    continue;
                }

                var body = BuildBody(user.DisplayName ?? user.Account, subtitle, userRecords);
                var sw = Stopwatch.StartNew();
                var result = await _emailService.SendAsync(user.Email!, subject, body);
                sw.Stop();

                Fsql.Insert(new EmailLogEntity
                {
                    TaskId = 0,
                    TaskName = TaskName,
                    Recipients = user.Email!,
                    Subject = subject,
                    Content = body,
                    Status = (byte)(result.Success ? 1 : 0),
                    ErrorMessage = result.ErrorMessage,
                    CostMs = (int)sw.ElapsedMilliseconds,
                    // 系统自动发送，无创建人（CreatedById 保持 null，列表展示为“系统”）
                }).ExecuteAffrows();

                if (result.Success) okCount++;
                else
                {
                    failCount++;
                    _logger.LogWarning("选号通知发送失败 user={User}: {Err}", user.Account, result.ErrorMessage);
                }
            }

            _logger.LogInformation("当天（{Date}）随机选号邮件通知完成：成功 {Ok}，失败 {Fail}，未绑定邮箱跳过 {Skip}",
                today.ToString("yyyy-MM-dd"), okCount, failCount, skipCount);
        }

        // ──────────────────── 邮件内容构建 ────────────────────

        /// <summary>
        /// 构建邮件 HTML 正文：全部彩种选号汇总（头部横幅 + 彩种色卡片 + 号码球 + 逐注选号表格），
        /// 与开奖结果通知邮件（LotteryResultNotifyJob.BuildBody）同一套内联样式。
        /// </summary>
        private static string BuildBody(string userName, string subtitle, List<LotteryRecordEntity> records)
        {
            var recordsByType = records
                .GroupBy(r => r.LotteryType)
                .ToDictionary(g => g.Key, g => g.ToList());

            var sb = new StringBuilder();
            // 外层灰底 + 居中白色卡片（邮箱客户端兼容：全部内联样式）
            sb.Append("<div style=\"font-family:'Microsoft YaHei','PingFang SC',sans-serif;background:#f2f4f7;padding:16px 8px\">");
            // 整体横向滚动容器：手机窄屏不压缩内容，而是整页横向滚动查看
            sb.Append("<div style=\"overflow-x:auto;-webkit-overflow-scrolling:touch\">");
            // min-width 保证表格列完整展示不换行；max-width 加宽至 900 提升桌面端展示空间
            sb.Append("<div style=\"max-width:900px;min-width:720px;margin:0 auto;background:#fff;border-radius:10px;overflow:hidden;border:1px solid #e4e7ed\">");

            // 头部横幅（蓝色系：选号为生成类通知，区别于开奖通知的红色横幅）
            sb.Append("<div style=\"background:#2563eb;color:#fff;padding:18px 24px\">");
            sb.Append("<div style=\"font-size:20px;font-weight:bold\">随机选号通知</div>");
            sb.Append($"<div style=\"font-size:13px;margin-top:4px\">{subtitle}</div>");
            sb.Append("</div>");

            sb.Append("<div style=\"padding:20px 24px;font-size:14px;color:#303133;line-height:1.8\">");
            sb.Append($"<p style=\"margin:0 0 14px\">{System.Net.WebUtility.HtmlEncode(userName)}，您好：</p>");
            sb.Append("<p style=\"margin:0 0 14px\">系统已为当天开奖的彩种各随机生成 10 注选号（如下），开奖后请以「开奖结果通知」邮件中的中奖验证为准。</p>");

            foreach (var (t, list) in recordsByType)
            {
                var typeName = LotteryTypes.GetName(t);
                var positional = LotteryTypes.IsPositional(t);
                var color = TypeColor(t);
                var first = list[0];

                // 彩种卡片
                sb.Append("<div style=\"border:1px solid #e4e7ed;border-radius:8px;padding:16px;margin-bottom:16px\">");
                // 彩种徽章（彩种色高亮）+ 期号 + 开奖日期（完整不换行）
                sb.Append("<p style=\"margin:0 0 12px\">"
                    + $"<span style=\"display:inline-block;background:{color};color:#fff;font-weight:bold;"
                    + "padding:3px 12px;border-radius:4px;font-size:14px\">" + typeName + "</span>"
                    + $"<span style=\"font-weight:bold;font-size:15px;margin-left:10px\">第 {first.IssueNumber} 期</span>"
                    + $"<span style=\"color:#909399;font-size:12px;margin-left:10px;white-space:nowrap\">开奖日期 {first.DrawDate:yyyy-MM-dd}</span></p>");

                // 逐注选号表格（与开奖通知邮件“您的选号及中奖结果”表格同款式，无中奖列）
                sb.Append("<table border=\"0\" cellspacing=\"0\" cellpadding=\"0\" style=\"border-collapse:collapse;width:100%;font-size:12px;border:1px solid #e4e7ed\">");
                // 居中样式写在每个单元格上（部分邮箱客户端不继承 tr 的 text-align）
                sb.Append("<tr style=\"background:#f5f7fa\"><th style=\"padding:6px 8px;border:1px solid #e4e7ed;white-space:nowrap;text-align:center\">序号</th>"
                    + "<th style=\"padding:6px 8px;border:1px solid #e4e7ed;white-space:nowrap;text-align:center\">您的选号</th></tr>");
                for (var i = 0; i < list.Count; i++)
                {
                    var front = LotteryPrizeHelper.ParseNumbers(list[i].FrontNumbers);
                    var back = LotteryPrizeHelper.ParseNumbers(list[i].BackNumbers);
                    sb.Append("<tr>"
                        + $"<td style=\"padding:6px 8px;border:1px solid #e4e7ed;text-align:center\">{i + 1}</td>"
                        + $"<td style=\"padding:6px 8px;border:1px solid #e4e7ed;text-align:center\">{RenderBalls(positional, front, back, 22)}</td>"
                        + "</tr>");
                }
                sb.Append("</table>");
                sb.Append("</div>");
            }

            sb.Append("<p style=\"color:#909399;font-size:12px;margin:16px 0 0;text-align:center\">本邮件由系统自动发送，请勿回复。</p>");
            sb.Append("</div></div></div></div>");
            return sb.ToString();
        }

        /// <summary>彩种主题色（徽章强调，与开奖通知邮件同色表）</summary>
        private static string TypeColor(string t) => t switch
        {
            "SSQ" => "#e6393a",
            "DLT" => "#2563eb",
            "PL5" => "#e6a23c",
            "FC3D" => "#67c23a",
            _ => "#606266"
        };

        /// <summary>号码球渲染：前区红球 + 后区蓝球（顺序型为纯数字红球）</summary>
        private static string RenderBalls(bool positional, int[] front, int[] back, int size)
        {
            var sb = new StringBuilder();
            sb.Append("<span style=\"white-space:nowrap\">");
            foreach (var n in front) sb.Append(Ball(positional ? n.ToString() : n.ToString("D2"), "#e6393a", size));
            if (!positional)
            {
                sb.Append("<span style=\"color:#c0c4cc;margin:0 4px\">+</span>");
                foreach (var n in back) sb.Append(Ball(n.ToString("D2"), "#2563eb", size));
            }
            sb.Append("</span>");
            return sb.ToString();
        }

        private static string Ball(string num, string color, int size) =>
            $"<span style=\"display:inline-block;width:{size}px;height:{size}px;line-height:{size}px;border-radius:50%;"
            + $"background:{color};color:#fff;font-weight:700;font-size:{Math.Max(12, size / 2)}px;text-align:center;margin-right:4px\">{num}</span>";

        // ──────────────────── 号码生成 ────────────────────

        /// <summary>按彩种分区规则随机生成一注号码（前区+后区）</summary>
        private static (int[] front, int[] back) GenerateRandomNumbers(string type)
        {
            var zones = LotteryTypes.GetPickZones(type);
            var front = new List<int>();
            var back = new List<int>();

            foreach (var zone in zones)
            {
                var pool = zone.Numbers;
                var target = zone.Source == "back" ? back : front;

                if (zone.Positional)
                {
                    // 位置型：每个分区选 1 个（允许重复，如 PL5 可以出 5,5,5,5,5）
                    target.Add(pool[Random.Shared.Next(pool.Length)]);
                }
                else
                {
                    // 池选型：选 Pick 个不重复的
                    var available = pool.ToList();
                    for (var i = 0; i < zone.Pick && available.Count > 0; i++)
                    {
                        var idx = Random.Shared.Next(available.Count);
                        target.Add(available[idx]);
                        available.RemoveAt(idx);
                    }
                }
            }

            return (front.ToArray(), back.ToArray());
        }

        /// <summary>号码数组 → 逗号分隔字符串：位置型按位原样存储，池选型升序补零</summary>
        private static string FormatNumbers(int[] numbers, bool positional)
        {
            if (numbers == null || numbers.Length == 0) return string.Empty;
            IEnumerable<int> seq = positional ? numbers : numbers.OrderBy(n => n);
            return string.Join(",", seq.Select(n => positional ? n.ToString() : n.ToString("D2")));
        }

        /// <summary>下一期期号与开奖日（与 LotteryService.GetNextIssueAndDate 同口径）</summary>
        private (string? issue, DateTime? drawDate) GetNextIssueAndDate(string type)
        {
            var latest = Fsql.Select<LotteryDrawEntity>()
                .Where(d => d.LotteryType == type)
                .OrderByDescending(d => d.IssueNumber)
                .First();
            if (latest == null) return (null, null);

            string? issue = long.TryParse(latest.IssueNumber, out var num)
                ? (num + 1).ToString()
                : null;

            var from = latest.DrawDate.Date >= DateTime.Today
                ? latest.DrawDate.Date.AddDays(1)
                : DateTime.Today;
            return (issue, LotteryTypes.NextDrawDate(type, from));
        }
    }
}

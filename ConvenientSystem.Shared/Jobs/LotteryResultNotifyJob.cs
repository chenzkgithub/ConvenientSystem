using ConvenientSystem.Shared.Common;
using ConvenientSystem.Shared.Common.Email;
using ConvenientSystem.Shared.Common.Webhook;
using ConvenientSystem.Shared.Entity.Common;
using ConvenientSystem.Shared.Entity.Email;
using ConvenientSystem.Shared.Model.Common;
using FreeSql;
using Hangfire;
using System.Diagnostics;
using System.Text;


namespace ConvenientSystem.Shared.Jobs
{
    /// <summary>
    /// 当天开奖结果邮件通知 Hangfire 定时 Job（每天 22:10 固定执行，独立于爬取触发）：
    /// - 按开奖星期规则判断当天哪些彩种开奖（大乐透周一/三/六，双色球周二/四/日，排列五/福彩3D 每天）
    /// - 从库中读取当天开奖结果；缺失时调用爬取 Job 拉取最新一期入库后再读
    /// 将当天开奖的所有彩种全国开奖结果（开奖号码 + 官网通告·全国中奖情况）整合为一封邮件，
    ///   发送给全部有邮箱的启用用户（需拥有对应彩种菜单权限，无权限彩种不纳入）；开奖当天选过号的彩种附带本人逐注中奖结果
    /// 数据权限：选号记录按 UserId 归属，邮件中仅包含该用户本人的选号与中奖信息
    /// 幂等：按当天日期查重 EmailLog，重复触发不重复发送
    /// </summary>
    public class LotteryResultNotifyJob : JobBase
    {
        private readonly IEmailService _emailService;
        private readonly LotteryDrawCrawlJob _crawlJob;
        private readonly WebhookNotifier _webhookNotifier;
        private readonly ILogger<LotteryResultNotifyJob> _logger;

        /// <summary>系统通知在 EmailLog 中的任务名（用于查重与日志展示）</summary>
        private const string TaskName = "开奖结果通知";

        /// <summary>共享 HttpClient（下载官网通告 PDF 用）</summary>
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

        public LotteryResultNotifyJob(
            [FromKeyedServices("ConvenientSystemDb")] IFreeSql fsql,
            IJobExecutionLogService jobLog,
            IEmailService emailService,
            LotteryDrawCrawlJob crawlJob,
            WebhookNotifier webhookNotifier,
            ILogger<LotteryResultNotifyJob> logger) : base(fsql, jobLog)
        {
            _emailService = emailService;
            _crawlJob = crawlJob;
            _webhookNotifier = webhookNotifier;
            _logger = logger;
        }

        /// <summary>
        /// 每天固定执行：汇总当天开奖的所有彩种，整合为一封邮件发送给每个用户。
        /// 当天无彩种开奖或某彩种开奖数据缺失时，改用该彩种最新一期开奖结果。
        /// </summary>
        [AutomaticRetry(Attempts = 2, DelaysInSeconds = new[] { 300, 900 })]
        public Task DailyNotifyAsync(CancellationToken ct = default)
            => ExecuteWithLog("当天开奖结果邮件汇总", nameof(DailyNotifyAsync), null, async () =>
        {
            var today = DateTime.Today;
            var tomorrow = today.AddDays(1);

            // 判断当天哪些彩种开奖（按开奖星期规则）
            var drawTypes = LotteryTypes.All
                .Where(t => LotteryTypes.GetDrawDays(t).Contains(today.DayOfWeek))
                .ToList();

            // 当天无彩种开奖时，改为发送全部彩种的最新一期
            var isLatestFallback = false;
            if (drawTypes.Count == 0)
            {
                _logger.LogInformation("当天（{Date}）无彩种开奖，改为发送最新一期开奖结果", today.ToString("yyyy-MM-dd"));
                drawTypes = LotteryTypes.All.ToList();
                isLatestFallback = true;
            }

            // 从库读取当天开奖结果；缺失则拉取最新一期入库后再读
            var draws = new List<LotteryDrawEntity>();
            foreach (var t in drawTypes)
            {
                ct.ThrowIfCancellationRequested();
                var draw = GetTodayDraw(t, today);
                if (draw == null)
                {
                    _logger.LogInformation("{Type}库中无当天开奖，尝试拉取最新一期入库", LotteryTypes.GetName(t));
                    try
                    {
                        // since=今天：翻到早于今天的期即停，等效于只补拉最新开奖
                        await _crawlJob.CrawlAsync(t, 900, false, today, ct: ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "{Type}补拉最新开奖失败", LotteryTypes.GetName(t));
                    }
                    draw = GetTodayDraw(t, today);
                }
                if (draw == null)
                {
                    // 当天开奖数据缺失（官方接口可能延迟），改为取最新一期
                    draw = GetLatestDraw(t);
                    if (draw != null)
                    {
                        _logger.LogInformation("{Type}当天开奖数据缺失，改用最新一期（第 {Issue} 期）",
                            LotteryTypes.GetName(t), draw.IssueNumber);
                    }
                }
                if (draw == null)
                {
                    _logger.LogWarning("{Type}无任何开奖数据，不纳入通知", LotteryTypes.GetName(t));
                    continue;
                }
                draws.Add(draw);
            }
            if (draws.Count == 0)
            {
                _logger.LogWarning("无任何开奖数据，跳过通知");
                return;
            }

            // 允许重复触发：Hangfire 重试或手动再次执行都会重新发送，不跳过已通知过的当天汇总
            var subject = isLatestFallback
                ? $"【开奖结果】{today:yyyy-MM-dd} 最新一期开奖汇总"
                : $"【开奖结果】{today:yyyy-MM-dd} 当天开奖汇总";

            // 彩种菜单权限过滤：用户→角色（启用）→SysRoleMenu→SysMenu.Name，与登录权限码生成同口径（admin 角色不做特殊放行）
            var permByUser = LoadMenuPermissions(draws);

            // 全部启用的、有邮箱的用户
            var users = Fsql.Select<SysUserEntity>()
                .Where(u => u.Enabled && u.Email != null && u.Email != "")
                .ToList();
            if (users.Count == 0)
            {
                _logger.LogInformation("无有邮箱的用户，跳过开奖通知");
                return;
            }

            // 当天开奖彩种的选号记录（数据权限：邮件内仅取本人记录）
            // 优先按期号归属的开奖日期匹配；历史记录无开奖日期时回退按选号当天匹配
            // 兼容“最新一期”回退场景：开奖日期可能非当天，按实际开奖日期范围查询
            var typeList = draws.Select(d => d.LotteryType).ToList();
            var minDrawDate = draws.Min(d => d.DrawDate);
            var maxDrawDateExclusive = draws.Max(d => d.DrawDate).AddDays(1);
            var records = Fsql.Select<LotteryRecordEntity>()
                .Where(r => typeList.Contains(r.LotteryType)
                    && ((r.DrawDate >= minDrawDate && r.DrawDate < maxDrawDateExclusive)
                        || (r.DrawDate == null && r.CreatedAt >= today && r.CreatedAt < tomorrow)))
                .ToList()
                .GroupBy(r => r.UserId)
                .ToDictionary(g => g.Key, g => g.ToList());

            // 官网通告 PDF 渲染成 JPEG 图片内嵌正文（与前端弹窗 pdf.js 展示口径一致）：
            // 逐彩种下载渲染一次，全部用户共享同一批图片；下载/渲染失败回退为外链展示
            var pdfCidsByDraw = new Dictionary<int, string[]>();
            var inlineImages = new List<EmailInlineImage>();
            foreach (var draw in draws.Where(d => !string.IsNullOrWhiteSpace(d.NoticeUrl)))
            {
                ct.ThrowIfCancellationRequested();
                var pdf = await DownloadPdfAsync(draw.NoticeUrl!, ct);
                if (pdf == null) continue;

                var pages = PdfImageRenderer.RenderToJpeg(pdf);
                if (pages.Count == 0)
                {
                    _logger.LogWarning("{Type}第 {Issue} 期通告 PDF 渲染失败，邮件回退为外链展示",
                        LotteryTypes.GetName(draw.LotteryType), draw.IssueNumber);
                    continue;
                }

                var cids = new string[pages.Count];
                for (var i = 0; i < pages.Count; i++)
                {
                    cids[i] = $"notice{draw.Id}p{i}";
                    inlineImages.Add(new EmailInlineImage { ContentId = cids[i], Data = pages[i] });
                }
                pdfCidsByDraw[draw.Id] = cids;
            }

            var okCount = 0;
            var failCount = 0;
            var skipCount = 0;
            // 各彩种判奖规则（库内生效版本，无则内置兜底）：一次取齐，全部用户邮件共用同一份
            var rulesByType = typeList.Distinct()
                .ToDictionary(t => t, t => LotteryRuleCache.Get(Fsql, t));
            foreach (var user in users)
            {
                ct.ThrowIfCancellationRequested();

                // 菜单权限过滤：仅发送用户有权限的彩种；一个有权限的彩种都没有则不发
                permByUser.TryGetValue(user.Id, out var menuPerms);
                var userDraws = draws.Where(d => menuPerms != null && menuPerms.Contains(MenuNameOf(d.LotteryType))).ToList();
                if (userDraws.Count == 0)
                {
                    skipCount++;
                    continue;
                }
                var permittedTypes = userDraws.Select(d => d.LotteryType).ToHashSet();

                records.TryGetValue(user.Id, out var allRecords);
                var userRecords = allRecords?.Where(r => permittedTypes.Contains(r.LotteryType)).ToList();
                var subtitle = isLatestFallback
                    ? $"{today:yyyy-MM-dd} · 最新一期开奖彩种全国开奖结果与您的中奖验证"
                    : $"{today:yyyy-MM-dd} · 当天开奖彩种全国开奖结果与您的中奖验证";
                var body = BuildBody(user.DisplayName ?? user.Account, subtitle, userDraws, pdfCidsByDraw, userRecords, rulesByType);

                var sw = Stopwatch.StartNew();
                var result = await _emailService.SendAsync(user.Email!, subject, body, inlineImages);
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
                    _logger.LogWarning("开奖通知发送失败 user={User}: {Err}", user.Account, result.ErrorMessage);
                }
            }

            _logger.LogInformation("当天（{Date}）开奖汇总邮件通知完成：成功 {Ok}，失败 {Fail}，无彩种菜单权限跳过 {Skip}",
                today.ToString("yyyy-MM-dd"), okCount, failCount, skipCount);

            // 向默认机器人推送开奖结果汇总（markdown 格式，异常吞掉不影响邮件流程）
            var webhookSubtitle = isLatestFallback
                ? $"{today:yyyy-MM-dd} · 最新一期开奖彩种全国开奖结果"
                : $"{today:yyyy-MM-dd} · 当天开奖彩种全国开奖结果";
            var webhookTitle = $"开奖结果汇总 {today:yyyy-MM-dd}";
            // 查询全部启用用户用于机器人消息中展示选号归属
            var webhookUserNames = Fsql.Select<SysUserEntity>()
                .Where(u => u.Enabled)
                .ToList()
                .ToDictionary(u => u.Id, u => u.DisplayName ?? u.Account);
            var webhookContent = BuildWebhookContent(draws, isLatestFallback, webhookSubtitle, records, webhookUserNames, rulesByType);
            await _webhookNotifier.SendToDefaultAsync(webhookTitle, webhookContent);
            _logger.LogInformation("开奖结果汇总已推送到默认机器人");
        });

        /// <summary>
        /// 构建机器人推送内容（markdown 富文本，兼容企业微信 / 钉钉 markdown 语法）。
        /// 信息分区与邮件 BuildBody 保持一致：头部标题 → 逐彩种（开奖号码 → 官网通告·全国中奖情况 → 本期选号及中奖结果）。
        /// 邮件按用户分别发送，机器人则为广播消息展示全部用户的选号与逐注中奖结果。
        /// 需要 WebhookConfig.UseCard=true 才能渲染样式；UseCard=false 时作为纯文本发送。
        /// </summary>
        private static string BuildWebhookContent(
            List<LotteryDrawEntity> draws, bool isLatestFallback, string subtitle,
            Dictionary<Guid, List<LotteryRecordEntity>> records,
            Dictionary<Guid, string> userNames,
            Dictionary<string, LotteryRuleDto> rulesByType)
        {
            var sb = new StringBuilder();

            // 头部标题（与邮件 BuildBody 头部横幅一致）
            sb.AppendLine("## 开奖结果每日汇总");
            sb.AppendLine($"**{subtitle}**\n");

            if (isLatestFallback)
                sb.AppendLine("> 📌 今日无开奖，以下为最新一期开奖结果\n");

            // 展平全部选号记录按彩种分组（与邮件 recordsByType 口径一致，但跨全部用户）
            var recordsByType = records.Values.SelectMany(r => r)
                .GroupBy(r => r.LotteryType)
                .ToDictionary(g => g.Key, g => g.ToList());

            for (var i = 0; i < draws.Count; i++)
            {
                var draw = draws[i];
                var t = draw.LotteryType;
                var typeName = LotteryTypes.GetName(t);
                var positional = LotteryTypes.IsPositional(t);
                var front = LotteryPrizeHelper.ParseNumbers(draw.FrontNumbers);
                var back = LotteryPrizeHelper.ParseNumbers(draw.BackNumbers);
                var numbers = LotteryPrizeHelper.FormatResult(positional, front, back);

                // 彩种标题行（与邮件彩种徽章+期号+开奖日期一致）
                sb.AppendLine($"### {typeName} 第{draw.IssueNumber}期");
                sb.AppendLine($"开奖日期：{draw.DrawDate:yyyy-MM-dd}\n");

                // 开奖号码（与邮件开奖号码行一致，代码块高亮）
                sb.AppendLine($"**开奖号码：** `{numbers}`");

                // 官网通告 · 全国中奖情况（与邮件 AppendNotice 分区一致）
                var grades = LotteryPrizeHelper.ParsePrizeDetail(draw.PrizeDetail);
                var hasMeta = draw.SalesAmount.HasValue || draw.PoolBalance.HasValue;
                var hasArea = !string.IsNullOrWhiteSpace(draw.PrizeArea);
                var hasPdf = !string.IsNullOrWhiteSpace(draw.NoticeUrl);
                if (grades.Count > 0 || hasMeta || hasArea || hasPdf)
                {
                    sb.AppendLine("\n**官网通告 · 全国中奖情况**\n");

                    // 奖级明细（与邮件奖级表格一致：奖级 | 全国中奖注数 | 单注奖金）
                    if (grades.Count > 0)
                    {
                        foreach (var g in grades)
                        {
                            var count = g.Count.HasValue ? g.Count.Value.ToString("N0") : "—";
                            var money = g.Money.HasValue ? g.Money.Value.ToString("N0") : "—";
                            sb.AppendLine($"- {g.Grade}：{count} 注，单注 {money} 元");
                        }
                    }

                    // 销量 / 奖池（与邮件当期销量·奖池滚存一致）
                    if (draw.SalesAmount.HasValue)
                        sb.AppendLine($"- 📊 当期销量：{draw.SalesAmount.Value:N0} 元");
                    if (draw.PoolBalance.HasValue)
                        sb.AppendLine($"- 💰 奖池滚存：{draw.PoolBalance.Value:N0} 元");

                    // 一等奖中奖地区（与邮件一等奖中奖地区一致）
                    if (hasArea)
                        sb.AppendLine($"> 📍 一等奖中奖地区：{draw.PrizeArea}");

                    // 官网通告原文链接（与邮件 PDF 链接一致）
                    if (hasPdf)
                        sb.AppendLine($"[📄 查看官网通告原文]({draw.NoticeUrl})");
                }

                // 本期选号及中奖结果（与邮件"您的选号及中奖结果"一致，展示全部用户的选号与逐注中奖结果）
                if (recordsByType.TryGetValue(t, out var typeRecords) && typeRecords.Count > 0)
                {
                    var rules = rulesByType.TryGetValue(t, out var r0) ? r0 : LotteryRuleDefaults.Get(t);
                    sb.AppendLine("\n**本期选号及中奖结果**\n");

                    var winTotal = 0;
                    var moneyTotal = 0m;
                    var netTotal = 0m;
                    var moneyKnown = false;
                    for (var j = 0; j < typeRecords.Count; j++)
                    {
                        var rec = typeRecords[j];
                        var pickFront = LotteryPrizeHelper.ParseNumbers(rec.FrontNumbers);
                        var pickBack = LotteryPrizeHelper.ParseNumbers(rec.BackNumbers);
                        var pickStr = LotteryPrizeHelper.FormatResult(positional, pickFront, pickBack);
                        var hit = LotteryPrizeHelper.CalcHit(t, positional, pickFront, pickBack, front, back, grades, rules);

                        var userName = userNames.TryGetValue(rec.UserId, out var name) ? name : "未知用户";
                        var hitDetail = positional ? $"{hit.FrontHitCount} 位" : $"{hit.FrontHitCount}+{hit.BackHitCount}";

                        // 奖金：官方明细缺该奖级时回落规则表固定奖金，两者都无则以 — 占位
                        string moneyStr;
                        if (hit.IsWin)
                        {
                            winTotal++;
                            var row = LotteryPrizeHelper.MatchGrade(hit.Prize, t, grades, rules);
                            if ((row?.Money ?? LotteryPrizeHelper.FixedMoney(rules, hit.Prize)) is decimal m)
                            {
                                moneyKnown = true;
                                var tax = LotteryPrizeHelper.CalcTax(m);
                                moneyTotal += m;
                                netTotal += m - tax;
                                moneyStr = tax > 0 ? $"¥{m:N0}（税后 ¥{m - tax:N0}）" : $"¥{m:N0}";
                            }
                            else
                                moneyStr = "—";
                        }
                        else
                            moneyStr = "—";

                        sb.AppendLine($"- {userName}：`{pickStr}` → {hitDetail} | {hit.Prize} | {moneyStr}");
                    }

                    if (winTotal > 0)
                    {
                        var moneyText = moneyKnown
                            ? (netTotal < moneyTotal
                                ? $"，合计奖金 ¥{moneyTotal:N0}（税后实得 ¥{netTotal:N0}）"
                                : $"，合计奖金 ¥{moneyTotal:N0}")
                            : string.Empty;
                        sb.AppendLine($"\n> 🎉 共 {winTotal} 注中奖{moneyText}");
                    }
                }

                // 彩种间分隔线（最后一个不加）
                if (i < draws.Count - 1)
                    sb.AppendLine("\n---\n");
            }

            sb.AppendLine("\n---\n> 🤖 本消息由系统自动发送，请勿回复");
            return sb.ToString().TrimEnd();
        }

        /// <summary>彩种代码 → 对应菜单 Name（与 db/init.sql 内置菜单一致）</summary>
        private static string MenuNameOf(string type) => type switch
        {
            LotteryTypes.SSQ => "lottery-ssq",
            LotteryTypes.PL5 => "lottery-pl5",
            LotteryTypes.FC3D => "lottery-fc3d",
            _ => "lottery"
        };

        /// <summary>
        /// 加载用户→彩种菜单 Name 集合：用户→角色（启用）→SysRoleMenu→SysMenu，
        /// 与登录权限码生成同口径（所有角色均按 SysRoleMenu 配置，不做 admin 特殊放行）
        /// </summary>
        private Dictionary<Guid, HashSet<string>> LoadMenuPermissions(List<LotteryDrawEntity> draws)
        {
            var menuNames = draws.Select(d => MenuNameOf(d.LotteryType)).Distinct().ToList();

            // 彩种菜单 Id→Name（菜单停用后不再授予权限）
            var menus = Fsql.Select<SysMenuEntity>()
                .Where(m => m.Enabled && menuNames.Contains(m.Name!))
                .ToList(m => new { m.Id, m.Name })
                .ToDictionary(m => m.Id, m => m.Name!);
            var result = new Dictionary<Guid, HashSet<string>>();
            if (menus.Count == 0) return result;

            // 角色→彩种菜单 Name 集合（仅启用角色）
            var menuIds = menus.Keys.ToList();
            var roleMenus = Fsql.Select<SysRoleMenuEntity>()
                .Where(rm => menuIds.Contains(rm.MenuId))
                .ToList(rm => new { rm.RoleId, rm.MenuId });
            var enabledRoleIds = Fsql.Select<SysRoleEntity>()
                .Where(r => r.Enabled)
                .ToList(r => r.Id)
                .ToHashSet();
            var permByRole = roleMenus
                .Where(rm => enabledRoleIds.Contains(rm.RoleId))
                .GroupBy(rm => rm.RoleId)
                .ToDictionary(g => g.Key, g => g.Select(rm => menus[rm.MenuId]).ToHashSet());
            if (permByRole.Count == 0) return result;

            // 用户→多角色权限并集
            var roleIds = permByRole.Keys.ToList();
            var userRoles = Fsql.Select<SysUserRoleEntity>()
                .Where(ur => roleIds.Contains(ur.RoleId))
                .ToList(ur => new { ur.UserId, ur.RoleId });
            foreach (var ur in userRoles)
            {
                if (!result.TryGetValue(ur.UserId, out var set))
                    result[ur.UserId] = set = new HashSet<string>();
                set.UnionWith(permByRole[ur.RoleId]);
            }
            return result;
        }

        /// <summary>查询指定彩种当天的开奖记录（同一天多期时取期号最大的一期）</summary>
        private LotteryDrawEntity? GetTodayDraw(string type, DateTime today)
            => Fsql.Select<LotteryDrawEntity>()
                .Where(d => d.LotteryType == type && d.DrawDate >= today && d.DrawDate < today.AddDays(1))
                .OrderByDescending(d => d.IssueNumber)
                .First();

        /// <summary>查询指定彩种最新一期开奖记录（不限日期，取开奖日期+期号最大的一期）</summary>
        private LotteryDrawEntity? GetLatestDraw(string type)
            => Fsql.Select<LotteryDrawEntity>()
                .Where(d => d.LotteryType == type)
                .OrderByDescending(d => d.DrawDate)
                .OrderByDescending(d => d.IssueNumber)
                .First();

        /// <summary>下载官网通告 PDF（失败返回 null，调用方回退为外链展示）</summary>
        private async Task<byte[]?> DownloadPdfAsync(string url, CancellationToken ct)
        {
            try
            {
                using var resp = await Http.GetAsync(url, ct);
                if (!resp.IsSuccessStatusCode) return null;
                return await resp.Content.ReadAsByteArrayAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "下载官网通告 PDF 失败：{Url}", url);
                return null;
            }
        }

        // ──────────────────── 邮件内容构建 ────────────────────

        /// <summary>构建邮件 HTML 正文：全部开奖彩种汇总（头部横幅 + 彩种色卡片 + 号码球 + 官网通告 + 本人逐注中奖结果）</summary>
        private static string BuildBody(string userName, string subtitle, List<LotteryDrawEntity> draws,
            Dictionary<int, string[]> pdfCidsByDraw, List<LotteryRecordEntity>? userRecords,
            Dictionary<string, LotteryRuleDto> rulesByType)
        {
            var recordsByType = (userRecords ?? new List<LotteryRecordEntity>())
                .GroupBy(r => r.LotteryType)
                .ToDictionary(g => g.Key, g => g.ToList());

            var sb = new StringBuilder();
            // 外层灰底 + 居中白色卡片（邮箱客户端兼容：全部内联样式）
            sb.Append("<div style=\"font-family:'Microsoft YaHei','PingFang SC',sans-serif;background:#f2f4f7;padding:16px 8px\">");
            // 整体横向滚动容器：手机窄屏不压缩内容，而是整页横向滚动查看
            sb.Append("<div style=\"overflow-x:auto;-webkit-overflow-scrolling:touch\">");
            // min-width 保证表格列完整展示不换行；max-width 加宽至 900 提升桌面端展示空间
            sb.Append("<div style=\"max-width:900px;min-width:720px;margin:0 auto;background:#fff;border-radius:10px;overflow:hidden;border:1px solid #e4e7ed\">");

            // 头部横幅
            sb.Append("<div style=\"background:#d63031;color:#fff;padding:18px 24px\">");
            sb.Append("<div style=\"font-size:20px;font-weight:bold\">开奖结果每日汇总</div>");
            sb.Append($"<div style=\"font-size:13px;margin-top:4px\">{subtitle}</div>");
            sb.Append("</div>");

            sb.Append("<div style=\"padding:20px 24px;font-size:14px;color:#303133;line-height:1.8\">");
            sb.Append($"<p style=\"margin:0 0 14px\">{System.Net.WebUtility.HtmlEncode(userName)}，您好：</p>");

            foreach (var draw in draws)
            {
                var t = draw.LotteryType;
                var typeName = LotteryTypes.GetName(t);
                var positional = LotteryTypes.IsPositional(t);
                var color = TypeColor(t);
                var drawFront = LotteryPrizeHelper.ParseNumbers(draw.FrontNumbers);
                var drawBack = LotteryPrizeHelper.ParseNumbers(draw.BackNumbers);

                // 彩种卡片
                sb.Append("<div style=\"border:1px solid #e4e7ed;border-radius:8px;padding:16px;margin-bottom:16px\">");
                // 彩种徽章（彩种色高亮）+ 期号 + 开奖日期（完整不换行）
                sb.Append("<p style=\"margin:0 0 12px\">"
                    + $"<span style=\"display:inline-block;background:{color};color:#fff;font-weight:bold;"
                    + "padding:3px 12px;border-radius:4px;font-size:14px\">" + typeName + "</span>"
                    + $"<span style=\"font-weight:bold;font-size:15px;margin-left:10px\">第 {draw.IssueNumber} 期</span>"
                    + $"<span style=\"color:#909399;font-size:12px;margin-left:10px;white-space:nowrap\">开奖日期 {draw.DrawDate:yyyy-MM-dd}</span></p>");
                // 开奖号码（红蓝球样式）
                sb.Append("<p style=\"margin:0 0 12px\"><b>开奖号码：</b>" + RenderBalls(positional, drawFront, drawBack, 26) + "</p>");

                // 官网通告 · 全国中奖情况（奖级表 + 销量/奖池 + 中奖地区 + 通告 PDF 图片）
                pdfCidsByDraw.TryGetValue(draw.Id, out var imageCids);
                AppendNotice(sb, draw, imageCids, color);

                // 本人当天该彩种的选号与中奖结果（有选号才展示）
                if (recordsByType.TryGetValue(t, out var records) && records.Count > 0)
                {
                    sb.Append("<p style=\"margin:14px 0 8px;font-weight:bold;border-left:3px solid " + color + ";padding-left:8px\">您的选号及中奖结果</p>");
                    // 外层 overflow-x 容器：表格超宽时横向滚动，不撑破邮件页面
                    sb.Append("<div style=\"overflow-x:auto;-webkit-overflow-scrolling:touch\">");
                    sb.Append("<table border=\"0\" cellspacing=\"0\" cellpadding=\"0\" style=\"border-collapse:collapse;width:100%;font-size:12px;border:1px solid #e4e7ed\">");
                    // 居中样式写在每个单元格上（部分邮箱客户端不继承 tr 的 text-align）
                    sb.Append("<tr style=\"background:#f5f7fa\"><th style=\"padding:6px 8px;border:1px solid #e4e7ed;white-space:nowrap;text-align:center\">序号</th>"
                        + "<th style=\"padding:6px 8px;border:1px solid #e4e7ed;white-space:nowrap;text-align:center\">开奖日期</th>"
                        + "<th style=\"padding:6px 8px;border:1px solid #e4e7ed;white-space:nowrap;text-align:center\">选号时间</th>"
                        + "<th style=\"padding:6px 8px;border:1px solid #e4e7ed;white-space:nowrap;text-align:center\">您的选号</th>"
                        + "<th style=\"padding:6px 8px;border:1px solid #e4e7ed;white-space:nowrap;text-align:center\">命中</th>"
                        + "<th style=\"padding:6px 8px;border:1px solid #e4e7ed;white-space:nowrap;text-align:center\">中奖结果</th>"
                        + "<th style=\"padding:6px 8px;border:1px solid #e4e7ed;white-space:nowrap;text-align:center\">奖金</th></tr>");

                    // 奖金与个税一律取官方当期明细（与验奖弹窗同一口径）
                    var grades = LotteryPrizeHelper.ParsePrizeDetail(draw.PrizeDetail);
                    // 判奖规则：库内生效版本（自官网条文抓取），无则内置兜底规则
                    var rules = rulesByType.TryGetValue(t, out var r0) ? r0 : LotteryRuleDefaults.Get(t);
                    var winTotal = 0;
                    var moneyTotal = 0m;
                    var netTotal = 0m;
                    var moneyKnown = false;
                    for (var i = 0; i < records.Count; i++)
                    {
                        var front = LotteryPrizeHelper.ParseNumbers(records[i].FrontNumbers);
                        var back = LotteryPrizeHelper.ParseNumbers(records[i].BackNumbers);
                        var hit = LotteryPrizeHelper.CalcHit(t, positional, front, back, drawFront, drawBack, grades, rules);
                        var isWin = hit.IsWin;
                        if (isWin) winTotal++;

                        // 奖金列：官方明细缺该奖级时回落规则表固定奖金，两者都无则以 — 占位，不编造金额
                        var moneyCell = "—";
                        if (isWin)
                        {
                            var row = LotteryPrizeHelper.MatchGrade(hit.Prize, t, grades, rules);
                            if ((row?.Money ?? LotteryPrizeHelper.FixedMoney(rules, hit.Prize)) is decimal m)
                            {
                                moneyKnown = true;
                                var tax = LotteryPrizeHelper.CalcTax(m);
                                moneyTotal += m;
                                netTotal += m - tax;
                                moneyCell = tax > 0
                                    ? $"<span style=\"color:#f56c6c;font-weight:bold\">¥{m:N0}</span>"
                                        + $"<br/><span style=\"color:#909399;font-size:11px\">税后 ¥{m - tax:N0}</span>"
                                    : $"<span style=\"color:#f56c6c;font-weight:bold\">¥{m:N0}</span>";
                            }
                        }

                        var rowBg = i % 2 == 1 ? " style=\"background:#fafafa\"" : "";
                        var drawDate = (records[i].DrawDate ?? draw.DrawDate).ToString("yyyy-MM-dd");
                        // 命中列取紧凑形式（池选型 3+1、位置型 2 位）：号码已逐位高亮，此列只做计数，避免长文案撑宽邮件表格
                        var hitCell = positional ? $"{hit.FrontHitCount} 位" : $"{hit.FrontHitCount}+{hit.BackHitCount}";
                        sb.Append($"<tr{rowBg}>"
                            + $"<td style=\"padding:6px 8px;border:1px solid #e4e7ed;white-space:nowrap;text-align:center\">{i + 1}</td>"
                            + $"<td style=\"padding:6px 8px;border:1px solid #e4e7ed;white-space:nowrap;color:#606266;text-align:center\">{drawDate}</td>"
                            + $"<td style=\"padding:6px 8px;border:1px solid #e4e7ed;white-space:nowrap;color:#606266;text-align:center\">{records[i].CreatedAt:yyyy-MM-dd HH:mm:ss}</td>"
                            + $"<td style=\"padding:6px 8px;border:1px solid #e4e7ed;white-space:nowrap;text-align:center\">{RenderPickText(positional, front, back, hit)}</td>"
                            + $"<td style=\"padding:6px 8px;border:1px solid #e4e7ed;white-space:nowrap;color:#606266;text-align:center\">{hitCell}</td>"
                            + $"<td style=\"padding:6px 8px;border:1px solid #e4e7ed;color:{(isWin ? "#f56c6c" : "#909399")};font-weight:bold;white-space:nowrap;text-align:center\">{hit.Prize}</td>"
                            + $"<td style=\"padding:6px 8px;border:1px solid #e4e7ed;white-space:nowrap;text-align:center\">{moneyCell}</td></tr>");
                    }
                    sb.Append("</table></div>");
                    if (winTotal > 0)
                    {
                        // 合计金额仅在官方奖金可得时追加，避免给出不完整的金额造成误解
                        var moneyText = moneyKnown
                            ? (netTotal < moneyTotal
                                ? $"，合计奖金 ¥{moneyTotal:N0}（税后实得 ¥{netTotal:N0}）"
                                : $"，合计奖金 ¥{moneyTotal:N0}")
                            : string.Empty;
                        sb.Append("<p style=\"margin:10px 0 0;padding:8px 12px;background:#fef0f0;border:1px solid #fbc4c4;border-radius:6px;color:#f56c6c;font-weight:bold\">恭喜！您本期共 "
                            + winTotal + " 注中奖" + moneyText + "。</p>");
                    }
                    else
                    {
                        sb.Append("<p style=\"margin:10px 0 0;color:#909399\">很遗憾本期未中奖，再接再厉！</p>");
                    }
                }
                sb.Append("</div>");
            }

            sb.Append("<p style=\"color:#909399;font-size:12px;margin:16px 0 0;text-align:center\">本邮件由系统自动发送，请勿回复。</p>");
            sb.Append("</div></div></div></div>");
            return sb.ToString();
        }

        /// <summary>彩种主题色（徽章/边框强调，区分度高）</summary>
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

        /// <summary>
        /// 选号紧凑文本渲染：前区红字 + 后区蓝字（不换行；比号码球省宽，用于选号结果表格列）。
        /// 传入命中明细时，命中号码加黄底下划线突出、未命中号码转灰，以便一眼看出对了哪几个。
        /// 邮件客户端对 CSS 支持有限，此处只用 color/background/border-bottom 等兼容性最好的属性。
        /// </summary>
        private static string RenderPickText(bool positional, int[] front, int[] back, LotteryHitResultDto? hit = null)
        {
            var sb = new StringBuilder();
            sb.Append("<span style=\"white-space:nowrap;font-weight:bold\">");
            sb.Append(string.Join(" ", front.Select((n, i) =>
            {
                var text = positional ? n.ToString() : n.ToString("D2");
                var isHit = hit != null && (positional
                    ? i < hit.PositionHits.Length && hit.PositionHits[i]
                    : hit.FrontHits.Contains(n));
                return NumberSpan(text, isHit, hit != null, "#e6393a");
            })));
            if (!positional && back.Length > 0)
            {
                sb.Append(" <span style=\"color:#c0c4cc\">+</span> ");
                sb.Append(string.Join(" ", back.Select(n =>
                    NumberSpan(n.ToString("D2"), hit != null && hit.BackHits.Contains(n), hit != null, "#2563eb"))));
            }
            sb.Append("</span>");
            return sb.ToString();
        }

        /// <summary>单个号码文本：命中加黄底下划线，未命中转灰；无命中明细时保持原色</summary>
        private static string NumberSpan(string text, bool isHit, bool hasHitInfo, string color)
        {
            if (!hasHitInfo) return $"<span style=\"color:{color}\">{text}</span>";
            return isHit
                ? $"<span style=\"color:{color};background:#fff7e6;border-bottom:2px solid #f7b500;padding:0 2px\">{text}</span>"
                : $"<span style=\"color:#c0c4cc\">{text}</span>";
        }

        /// <summary>追加官网通告·全国中奖情况（与走势图双击弹窗同口径：奖级表 + 销量/奖池 + 中奖地区 + 通告 PDF 图片/链接）</summary>
        private static void AppendNotice(StringBuilder sb, LotteryDrawEntity draw, string[]? imageCids, string color)
        {
            var grades = LotteryPrizeHelper.ParsePrizeDetail(draw.PrizeDetail);
            var hasMeta = draw.SalesAmount.HasValue || draw.PoolBalance.HasValue;
            var hasArea = !string.IsNullOrWhiteSpace(draw.PrizeArea);
            var hasPdf = !string.IsNullOrWhiteSpace(draw.NoticeUrl);
            if (grades.Count == 0 && !hasMeta && !hasArea && !hasPdf) return;

            sb.Append("<p style=\"margin:12px 0 8px;font-weight:bold;border-left:3px solid " + color + ";padding-left:8px\">官网通告 · 全国中奖情况</p>");

            if (grades.Count > 0)
            {
                sb.Append("<table border=\"0\" cellspacing=\"0\" cellpadding=\"0\" style=\"border-collapse:collapse;font-size:13px;border:1px solid #e4e7ed\">");
                sb.Append("<tr style=\"background:#f5f7fa\"><th style=\"padding:8px;border:1px solid #e4e7ed;text-align:center\">奖级</th>"
                    + "<th style=\"padding:8px;border:1px solid #e4e7ed;text-align:center\">全国中奖注数</th>"
                    + "<th style=\"padding:8px;border:1px solid #e4e7ed;text-align:center\">单注奖金（元）</th></tr>");
                for (var i = 0; i < grades.Count; i++)
                {
                    var g = grades[i];
                    var rowBg = i % 2 == 1 ? " style=\"background:#fafafa\"" : "";
                    sb.Append($"<tr{rowBg}>"
                        + $"<td style=\"padding:8px;border:1px solid #e4e7ed;text-align:center\">{System.Net.WebUtility.HtmlEncode(g.Grade)}</td>"
                        + $"<td style=\"padding:8px;border:1px solid #e4e7ed;text-align:center\">{(g.Count.HasValue ? g.Count.Value.ToString("N0") : "—")}</td>"
                        + $"<td style=\"padding:8px;border:1px solid #e4e7ed;text-align:center\">{(g.Money.HasValue ? g.Money.Value.ToString("N0") : "—")}</td></tr>");
                }
                sb.Append("</table>");
            }

            if (hasMeta)
            {
                var parts = new List<string>();
                if (draw.SalesAmount.HasValue) parts.Add($"当期销量：{draw.SalesAmount.Value:N0} 元");
                if (draw.PoolBalance.HasValue) parts.Add($"奖池滚存：{draw.PoolBalance.Value:N0} 元");
                sb.Append($"<p style=\"color:#606266;margin:8px 0 0\">{string.Join("；", parts)}</p>");
            }

            if (hasArea)
            {
                sb.Append("<p style=\"color:#606266;margin:8px 0 0\"><b style=\"color:#f56c6c\">一等奖中奖地区：</b>"
                    + System.Net.WebUtility.HtmlEncode(draw.PrizeArea!) + "</p>");
            }

            if (hasPdf)
            {
                // 渲染成功时直接内嵌 PDF 逐页图片（CID 引用，不依赖收件方加载外链）；同时保留原文链接兜底
                if (imageCids is { Length: > 0 })
                {
                    foreach (var cid in imageCids)
                    {
                        sb.Append($"<img src=\"cid:{cid}\" alt=\"官网通告\" "
                            + "style=\"max-width:100%;border:1px solid #e4e7ed;border-radius:4px;margin-top:8px;display:block\" />");
                    }
                }
                sb.Append($"<p style=\"margin:8px 0 0\"><a href=\"{System.Net.WebUtility.HtmlEncode(draw.NoticeUrl!)}\" style=\"color:#409eff\">"
                    + "查看官网通告原文（PDF，含中奖地区等完整通告）</a></p>");
            }
        }
    }
}

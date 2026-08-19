using ConvenientSystem.Shared.Common;
using ConvenientSystem.Shared.Entity.Common;
using ConvenientSystem.Shared.Jobs;
using ConvenientSystem.Shared.Model.Common;
using FreeSql;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// 开奖结果每日汇总服务实现。
    /// </summary>
    public class LotterySummaryService : ILotterySummaryService
    {
        private readonly IFreeSql _fsql;
        private readonly LotteryDrawCrawlJob _crawlJob;
        private readonly ILogger<LotterySummaryService> _logger;

        public LotterySummaryService(
            [FromKeyedServices("ConvenientSystemDb")] IFreeSql fsql,
            LotteryDrawCrawlJob crawlJob,
            ILogger<LotterySummaryService> logger)
        {
            _fsql = fsql;
            _crawlJob = crawlJob;
            _logger = logger;
        }

        /// <inheritdoc />
        public async Task<LotteryResultSummaryDto> GetSummaryAsync(DateTime? date = null, CancellationToken ct = default)
        {
            var today = date?.Date ?? DateTime.Today;
            var tomorrow = today.AddDays(1);

            // 按开奖星期规则判断当天哪些彩种开奖
            var drawTypes = LotteryTypes.All
                .Where(t => LotteryTypes.GetDrawDays(t).Contains(today.DayOfWeek))
                .ToList();

            // 当天无彩种开奖时，改为发送全部彩种的最新一期
            var isLatestFallback = false;
            if (drawTypes.Count == 0)
            {
                _logger.LogInformation("当天（{Date}）无彩种开奖，改为取最新一期开奖结果", today.ToString("yyyy-MM-dd"));
                drawTypes = LotteryTypes.All.ToList();
                isLatestFallback = true;
            }

            // 读取当天开奖结果；缺失则拉取最新一期入库后再读
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
                    draw = GetLatestDraw(t);
                    if (draw != null)
                    {
                        _logger.LogInformation("{Type}当天开奖数据缺失，改用最新一期（第 {Issue} 期）",
                            LotteryTypes.GetName(t), draw.IssueNumber);
                    }
                }
                if (draw == null)
                {
                    _logger.LogWarning("{Type}无任何开奖数据，不纳入汇总", LotteryTypes.GetName(t));
                    continue;
                }
                draws.Add(draw);
            }

            if (draws.Count == 0)
            {
                _logger.LogWarning("无任何开奖数据，返回空汇总");
                return new LotteryResultSummaryDto
                {
                    Date = today,
                    IsLatestFallback = isLatestFallback,
                    Title = $"开奖结果汇总 {today:yyyy-MM-dd}",
                    Subtitle = "暂无开奖数据",
                    Draws = new List<LotterySummaryDrawDto>()
                };
            }

            // 全部启用用户（用于显示用户名）
            var userNames = _fsql.Select<SysUserEntity>()
                .Where(u => u.Enabled)
                .ToList()
                .ToDictionary(u => u.Id, u => u.DisplayName ?? u.Account);

            // 汇总日期范围内该彩种的全部选号记录
            var typeList = draws.Select(d => d.LotteryType).ToList();
            var minDrawDate = draws.Min(d => d.DrawDate);
            var maxDrawDateExclusive = draws.Max(d => d.DrawDate).AddDays(1);
            var records = _fsql.Select<LotteryRecordEntity>()
                .Where(r => typeList.Contains(r.LotteryType)
                    && ((r.DrawDate >= minDrawDate && r.DrawDate < maxDrawDateExclusive)
                        || (r.DrawDate == null && r.CreatedAt >= today && r.CreatedAt < tomorrow)))
                .ToList()
                .GroupBy(r => r.UserId)
                .ToDictionary(g => g.Key, g => g.ToList());

            // 各彩种判奖规则（库内生效版本，无则内置兜底）
            var rulesByType = typeList.Distinct()
                .ToDictionary(t => t, t => LotteryRuleCache.Get(_fsql, t));

            var subtitle = isLatestFallback
                ? $"{today:yyyy-MM-dd} · 最新一期开奖彩种全国开奖结果与中奖验证"
                : $"{today:yyyy-MM-dd} · 当天开奖彩种全国开奖结果与中奖验证";

            var result = new LotteryResultSummaryDto
            {
                Date = today,
                IsLatestFallback = isLatestFallback,
                Title = $"开奖结果汇总 {today:yyyy-MM-dd}",
                Subtitle = subtitle,
                Draws = draws.Select(d => MapDraw(d, records, userNames, rulesByType)).ToList()
            };
            return result;
        }

        private static LotterySummaryDrawDto MapDraw(LotteryDrawEntity draw,
            Dictionary<Guid, List<LotteryRecordEntity>> recordsByUser,
            Dictionary<Guid, string> userNames,
            Dictionary<string, LotteryRuleDto> rulesByType)
        {
            var t = draw.LotteryType;
            var positional = LotteryTypes.IsPositional(t);
            var drawFront = LotteryPrizeHelper.ParseNumbers(draw.FrontNumbers);
            var drawBack = LotteryPrizeHelper.ParseNumbers(draw.BackNumbers);
            var grades = LotteryPrizeHelper.ParsePrizeDetail(draw.PrizeDetail);
            var rules = rulesByType.TryGetValue(t, out var r0) ? r0 : LotteryRuleDefaults.Get(t);

            var dto = new LotterySummaryDrawDto
            {
                Type = t,
                TypeName = LotteryTypes.GetName(t),
                Color = TypeColor(t),
                Positional = positional,
                IssueNumber = draw.IssueNumber,
                DrawDate = draw.DrawDate,
                Front = drawFront,
                Back = drawBack,
                Grades = grades,
                SalesAmount = draw.SalesAmount,
                PoolBalance = draw.PoolBalance,
                PrizeArea = draw.PrizeArea,
                NoticeUrl = draw.NoticeUrl,
                Records = new List<LotterySummaryRecordDto>()
            };

            foreach (var userRecords in recordsByUser.Values)
            {
                foreach (var rec in userRecords.Where(r => r.LotteryType == t))
                {
                    var pickFront = LotteryPrizeHelper.ParseNumbers(rec.FrontNumbers);
                    var pickBack = LotteryPrizeHelper.ParseNumbers(rec.BackNumbers);
                    var hit = LotteryPrizeHelper.CalcHit(t, positional, pickFront, pickBack, drawFront, drawBack, grades, rules);

                    decimal? money = null;
                    decimal? tax = null;
                    decimal? net = null;
                    if (hit.IsWin)
                    {
                        var row = LotteryPrizeHelper.MatchGrade(hit.Prize, t, grades, rules);
                        if ((row?.Money ?? LotteryPrizeHelper.FixedMoney(rules, hit.Prize)) is decimal m)
                        {
                            money = m;
                            tax = LotteryPrizeHelper.CalcTax(m);
                            net = m - tax;
                        }
                    }

                    dto.Records.Add(new LotterySummaryRecordDto
                    {
                        UserName = userNames.TryGetValue(rec.UserId, out var name) ? name : "未知用户",
                        Front = pickFront,
                        Back = pickBack,
                        CreatedAt = rec.CreatedAt,
                        DrawDate = rec.DrawDate,
                        Hit = hit,
                        Prize = hit.Prize,
                        Money = money,
                        Tax = tax,
                        Net = net
                    });
                }
            }

            return dto;
        }

        private LotteryDrawEntity? GetTodayDraw(string type, DateTime today)
            => _fsql.Select<LotteryDrawEntity>()
                .Where(d => d.LotteryType == type && d.DrawDate >= today && d.DrawDate < today.AddDays(1))
                .OrderByDescending(d => d.IssueNumber)
                .First();

        private LotteryDrawEntity? GetLatestDraw(string type)
            => _fsql.Select<LotteryDrawEntity>()
                .Where(d => d.LotteryType == type)
                .OrderByDescending(d => d.DrawDate)
                .OrderByDescending(d => d.IssueNumber)
                .First();

        private static string TypeColor(string t) => t switch
        {
            LotteryTypes.SSQ => "#e6393a",
            LotteryTypes.DLT => "#2563eb",
            LotteryTypes.PL5 => "#e6a23c",
            LotteryTypes.FC3D => "#67c23a",
            _ => "#606266"
        };
    }
}

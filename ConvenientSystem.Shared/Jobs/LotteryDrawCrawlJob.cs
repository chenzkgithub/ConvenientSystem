using ConvenientSystem.Shared.Common;
using ConvenientSystem.Shared.Entity.Common;
using ConvenientSystem.Shared.Model.Common;
using FreeSql;
using Hangfire;
using System.Text.Json;

namespace ConvenientSystem.Shared.Jobs
{
    /// <summary>
    /// 彩票开奖数据自动爬取 Hangfire Job（多彩种：大乐透/双色球/排列五/福彩3D）
    /// 数据源均为官方公开接口：
    /// - 双色球/福彩3D：福彩官网 cwl.gov.cn（单页最多 900 条）
    /// - 大乐透/排列五：体彩官网 webapi.sporttery.cn（单页上限 100 条，接口限制）
    /// 每次运行全量分页拉取，按期号去重入库
    /// </summary>
    public class LotteryDrawCrawlJob
    {
        private readonly IFreeSql _fsql;
        private readonly ILogger<LotteryDrawCrawlJob> _logger;

        /// <summary>共享 HttpClient（官方接口直连用）</summary>
        private static readonly HttpClient Http = new();

        public LotteryDrawCrawlJob(
            [FromKeyedServices("ConvenientSystemDb")] IFreeSql fsql,
            ILogger<LotteryDrawCrawlJob> logger)
        {
            _fsql = fsql;
            _logger = logger;
        }

        /// <summary>号码来源：福彩官网 / 体彩官网</summary>
        private enum ScraperSource { Cwl, Sporttery }

        /// <summary>
        /// 彩种爬取规则：来源、号码数量、是否位置型、单页上限（接口限制）、福彩名/体彩游戏编号
        /// </summary>
        private sealed record CrawlRule(ScraperSource Source, int FrontCount, int BackCount, bool Positional,
            int MaxPageSize, string? CwlName = null, string? SportGameNo = null);

        private static CrawlRule GetRule(string type) => type switch
        {
            LotteryTypes.SSQ => new CrawlRule(ScraperSource.Cwl, 6, 1, false, 900, CwlName: "ssq"),
            LotteryTypes.PL5 => new CrawlRule(ScraperSource.Sporttery, 5, 0, true, 100, SportGameNo: "350133"),
            LotteryTypes.FC3D => new CrawlRule(ScraperSource.Cwl, 3, 0, true, 900, CwlName: "3d"),
            _ => new CrawlRule(ScraperSource.Sporttery, 5, 2, false, 100, SportGameNo: "85"),
        };

        /// <summary>
        /// 执行数据爬取：每次全量拉取（每页 pageSize 条，受接口单页上限约束），分页遍历至无更多数据，按期号去重入库
        /// updateExisting=true 时为回填模式：额外为库中已有但缺中奖明细的开奖补充官方奖级数据（不影响号码）；
        /// since 非空时翻到早于该日期的期即提前结束（回填只需覆盖到最早选号记录）
        /// </summary>
        [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 60, 300, 900 })]
        public async Task<int> CrawlAsync(string type = LotteryTypes.DLT, int pageSize = 900,
            bool updateExisting = false, DateTime? since = null, CancellationToken ct = default)
        {
            var t = LotteryTypes.Normalize(type);
            var rule = GetRule(t);
            var typeName = LotteryTypes.GetName(t);

            // 实际单页条数受接口上限约束（体彩官网单页最多 100 条）
            var fetchSize = Math.Min(pageSize, rule.MaxPageSize);

            var existingCount = _fsql.Select<LotteryDrawEntity>().Where(d => d.LotteryType == t).Count();
            _logger.LogInformation("开始全量爬取{Type}（每页 {Size} 条），当前已有 {Count} 条记录", typeName, fetchSize, existingCount);

            try
            {
                var allDraws = new List<LotteryDrawEntity>();

                // 分页遍历拉取（每页 fetchSize 条）
                var pageNo = 1;
                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    _logger.LogInformation("{Type}爬取第 {Page} 页...", typeName, pageNo);

                    var draws = await CrawlPageAsync(t, rule, pageNo, fetchSize, ct);
                    if (draws.Count == 0)
                    {
                        _logger.LogInformation("第 {Page} 页无数据，{Type}爬取完成", pageNo, typeName);
                        break;
                    }

                    allDraws.AddRange(draws);
                    _logger.LogInformation("第 {Page} 页解析到 {Count} 条，累计 {Total} 条",
                        pageNo, draws.Count, allDraws.Count);

                    // 回填模式：已翻到早于目标日期的期，更早的无需采集
                    if (since != null && draws.Min(d => d.DrawDate) < since.Value)
                        break;

                    // 如果返回数量少于单页条数，说明已经是最后一页
                    if (draws.Count < fetchSize)
                        break;

                    pageNo++;

                    // 每页间隔 1 秒，避免被封
                    await Task.Delay(1000, ct);
                }

                if (allDraws.Count == 0)
                {
                    _logger.LogWarning("{Type}爬取结果为空", typeName);
                    return 0;
                }

                // 去重：排除同彩种已存在的期号
                var existingIssues = _fsql.Select<LotteryDrawEntity>()
                    .Where(d => d.LotteryType == t)
                    .ToList(d => d.IssueNumber)
                    .ToHashSet();

                var newDraws = allDraws
                    .Where(d => !existingIssues.Contains(d.IssueNumber))
                    .ToList();

                if (newDraws.Count > 0)
                {
                    _fsql.Insert(newDraws).ExecuteAffrows();
                    _logger.LogInformation("成功导入 {Count} 条{Type}开奖记录", newDraws.Count, typeName);

                    // 开奖结果邮件已改为每日 22:10 独立定时任务（LotteryResultNotifyJob.DailyNotifyAsync），此处不再触发
                }
                else
                {
                    _logger.LogInformation("无新增开奖记录（全部 {Count} 期已存在）", allDraws.Count);
                }

                // 回填模式：为库中已有但缺中奖明细的开奖补充官方奖级数据
                if (updateExisting)
                    BackfillPrizeDetail(t, typeName, allDraws);

                return newDraws.Count;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "爬取{Type}开奖数据失败", typeName);
                throw;
            }
        }

        /// <summary>
        /// 当日补拉：仅重拉最近 days 天的开奖，给库内缺奖级明细/通告链接的期补齐。
        /// 各奖级注数与单注奖金的官方发布常晚于开奖号码（体彩详细通告可能延后 1~2 小时），
        /// 22:00 主爬往往抓不到，故当晚再跑一轮；每日重跑 + days 天窗口使当晚仍未发布的期次日仍能被补上。
        /// 两个不能改的地方：
        /// 1. since 必须在方法内按运行时计算——Hangfire 会把定时任务表达式的实参序列化固化到存储，
        ///    直接传 DateTime.Today 会永远停留在注册当天；
        /// 2. pageSize 取小值——回填走 IssueNumber 的 IN 查询，条数过大会顶到 SQL Server 2100 参数上限。
        /// </summary>
        public Task<int> BackfillRecentAsync(string type = LotteryTypes.DLT, int days = 2,
            CancellationToken ct = default)
            => CrawlAsync(type, 30, true, DateTime.Today.AddDays(-days), ct);

        /// <summary>爬取指定页的数据（按彩种规则分流到福彩/体彩官网接口）</summary>
        private async Task<List<LotteryDrawEntity>> CrawlPageAsync(string type, CrawlRule rule, int pageNo, int fetchSize, CancellationToken ct)
        {
            try
            {
                return rule.Source switch
                {
                    ScraperSource.Cwl => await CrawlCwlPageAsync(type, rule, pageNo, fetchSize, ct),
                    _ => await CrawlSportteryPageAsync(type, rule, pageNo, fetchSize, ct),
                };
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "爬取第 {Page} 页异常", pageNo);
                return new List<LotteryDrawEntity>();
            }
        }

        // ──────────────────── 数据源 1：福彩官网（双色球/福彩3D） ────────────────────

        /// <summary>福彩官网开奖查询接口（双色球 name=ssq / 福彩3D name=3d）</summary>
        private async Task<List<LotteryDrawEntity>> CrawlCwlPageAsync(string type, CrawlRule rule, int pageNo, int fetchSize, CancellationToken ct)
        {
            var url = "http://www.cwl.gov.cn/cwl_admin/front/cwlkj/search/kjxx/findDrawNotice"
                + $"?name={rule.CwlName}&issueCount=0&issueStart=&issueEnd=&dayStart=&dayEnd="
                + $"&pageNo={pageNo}&pageSize={fetchSize}&week=&systemType=PC";

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("Referer", "http://www.cwl.gov.cn/");
            req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");

            using var resp = await Http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("福彩接口第 {Page} 页返回 {Status}", pageNo, (int)resp.StatusCode);
                return new List<LotteryDrawEntity>();
            }

            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("result", out var items) || items.ValueKind != JsonValueKind.Array)
                return new List<LotteryDrawEntity>();

            var draws = new List<LotteryDrawEntity>();
            foreach (var item in items.EnumerateArray())
            {
                var code = item.TryGetProperty("code", out var c) ? c.GetString() : null;
                var date = item.TryGetProperty("date", out var d) ? d.GetString() : null;
                var red = item.TryGetProperty("red", out var r) ? r.GetString() : null;
                var blue = item.TryGetProperty("blue", out var b) ? b.GetString() : null;
                if (string.IsNullOrWhiteSpace(code)) continue;

                // 福彩号码：red 为前区（福彩3D 的 red 即按位号码），blue 为后区（仅双色球有）
                var drawResult = string.IsNullOrWhiteSpace(blue) ? red ?? "" : $"{red} + {blue}";
                var draw = BuildDraw(type, rule, code, date ?? "", drawResult);
                if (draw != null)
                {
                    FillCwlPrize(type, draw, item);
                    draws.Add(draw);
                }
            }
            return draws;
        }

        // ──────────────────── 数据源 2：体彩官网（大乐透/排列五） ────────────────────

        /// <summary>体彩官网开奖查询接口（大乐透 gameNo=85 / 排列五 gameNo=350133，单页上限 100 条）</summary>
        private async Task<List<LotteryDrawEntity>> CrawlSportteryPageAsync(string type, CrawlRule rule, int pageNo, int fetchSize, CancellationToken ct)
        {
            var url = "https://webapi.sporttery.cn/gateway/lottery/getHistoryPageListV1.qry"
                + $"?gameNo={rule.SportGameNo}&provinceId=0&pageSize={fetchSize}&isVerify=1&pageNo={pageNo}";

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("Referer", "https://static.sporttery.cn/");
            req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");

            using var resp = await Http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("体彩接口第 {Page} 页返回 {Status}", pageNo, (int)resp.StatusCode);
                return new List<LotteryDrawEntity>();
            }

            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("value", out var valueEl)
                || !valueEl.TryGetProperty("list", out var items)
                || items.ValueKind != JsonValueKind.Array)
                return new List<LotteryDrawEntity>();

            var draws = new List<LotteryDrawEntity>();
            foreach (var item in items.EnumerateArray())
            {
                var num = item.TryGetProperty("lotteryDrawNum", out var n) ? n.GetString() : null;
                var time = item.TryGetProperty("lotteryDrawTime", out var t) ? t.GetString() : null;
                var result = item.TryGetProperty("lotteryDrawResult", out var r) ? r.GetString() : null;
                if (string.IsNullOrWhiteSpace(num) || string.IsNullOrWhiteSpace(result)) continue;

                var draw = BuildDraw(type, rule, num, time ?? "", result);
                if (draw != null)
                {
                    FillSportteryPrize(draw, item);
                    draws.Add(draw);
                }
            }
            return draws;
        }

        // ──────────────────── 公共：号码解析与实体构建 ────────────────────

        /// <summary>
        /// 由期号、时间文本、号码文本构建开奖实体
        /// 号码支持空格、逗号、加号分隔；位置型允许 0，池选型过滤 0
        /// </summary>
        private LotteryDrawEntity? BuildDraw(string type, CrawlRule rule, string issueNumber, string drawTime, string drawResult)
        {
            var allNumbers = drawResult
                .Split(new[] { ',', ' ', '+', '-' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.TryParse(s.Trim(), out var n) ? n : -1)
                .Where(n => rule.Positional ? n >= 0 : n > 0)
                .ToArray();

            var expected = rule.FrontCount + rule.BackCount;
            if (allNumbers.Length < expected)
            {
                _logger.LogDebug("号码不足: issue={Issue}, result={Result}, count={Count}, expected={Expected}",
                    issueNumber, drawResult, allNumbers.Length, expected);
                return null;
            }

            var front = allNumbers.Take(rule.FrontCount).ToArray();
            var back = allNumbers.Skip(rule.FrontCount).Take(rule.BackCount).ToArray();

            return new LotteryDrawEntity
            {
                LotteryType = type,
                IssueNumber = issueNumber,
                DrawDate = ParseDrawDate(drawTime),
                FrontNumbers = FormatNumbers(front, rule.Positional),
                BackNumbers = FormatNumbers(back, rule.Positional),
            };
        }

        /// <summary>解析开奖日期（兼容 "2026-08-06(四)" 等带后缀格式），失败时取当前时间</summary>
        private static DateTime ParseDrawDate(string raw)
        {
            if (!string.IsNullOrWhiteSpace(raw))
            {
                var head = raw.Split('(')[0].Trim();
                if (DateTime.TryParse(head, out var dt))
                    return dt;
            }
            return DateTime.Now;
        }

        /// <summary>号码数组 → 逗号分隔字符串：位置型按位原样存储，池选型升序补零</summary>
        private static string FormatNumbers(int[] numbers, bool positional)
        {
            if (numbers == null || numbers.Length == 0) return string.Empty;
            IEnumerable<int> seq = positional ? numbers : numbers.OrderBy(n => n);
            return string.Join(",", seq.Select(n => positional ? n.ToString() : n.ToString("D2")));
        }

        // ──────────────────── 官方中奖明细解析（官网通告：奖级/全国注数/单注奖金） ────────────────────

        /// <summary>回填模式：把本次爬到的官网通告数据写入库中缺失的开奖记录
        /// 缺失口径按彩种：全部彩种查缺中奖明细；体彩另查缺通告 PDF 链接，双色球另查缺中奖地区</summary>
        private void BackfillPrizeDetail(string type, string typeName, List<LotteryDrawEntity> crawled)
        {
            var withDetail = crawled.Where(d => !string.IsNullOrEmpty(d.PrizeDetail)
                || !string.IsNullOrEmpty(d.PrizeArea) || !string.IsNullOrEmpty(d.NoticeUrl)).ToList();
            if (withDetail.Count == 0) return;

            var issues = withDetail.Select(d => d.IssueNumber).ToList();
            var map = withDetail.ToDictionary(d => d.IssueNumber);
            var missing = type switch
            {
                // 体彩（大乐透/排列五）：中奖明细或通告 PDF 链接缺失
                LotteryTypes.DLT or LotteryTypes.PL5 => _fsql.Select<LotteryDrawEntity>()
                    .Where(d => d.LotteryType == type && issues.Contains(d.IssueNumber)
                        && (d.PrizeDetail == null || d.PrizeDetail == ""
                            || d.NoticeUrl == null || d.NoticeUrl == ""))
                    .ToList(),
                // 双色球：中奖明细或中奖地区缺失
                LotteryTypes.SSQ => _fsql.Select<LotteryDrawEntity>()
                    .Where(d => d.LotteryType == type && issues.Contains(d.IssueNumber)
                        && (d.PrizeDetail == null || d.PrizeDetail == ""
                            || d.PrizeArea == null || d.PrizeArea == ""))
                    .ToList(),
                // 福彩3D：官网不提供地区/PDF，仅查中奖明细
                _ => _fsql.Select<LotteryDrawEntity>()
                    .Where(d => d.LotteryType == type && issues.Contains(d.IssueNumber)
                        && (d.PrizeDetail == null || d.PrizeDetail == ""))
                    .ToList(),
            };
            if (missing.Count == 0) return;

            foreach (var row in missing)
            {
                var src = map[row.IssueNumber];
                // 源数据无值时保留库内已有值，避免误清空
                row.PrizeDetail = src.PrizeDetail ?? row.PrizeDetail;
                row.SalesAmount = src.SalesAmount ?? row.SalesAmount;
                row.PoolBalance = src.PoolBalance ?? row.PoolBalance;
                row.PrizeArea = src.PrizeArea ?? row.PrizeArea;
                row.NoticeUrl = src.NoticeUrl ?? row.NoticeUrl;
            }
            _fsql.Update<LotteryDrawEntity>().SetSource(missing)
                .UpdateColumns(d => new { d.PrizeDetail, d.SalesAmount, d.PoolBalance, d.PrizeArea, d.NoticeUrl })
                .ExecuteAffrows();
            _logger.LogInformation("回填 {Count} 条{Type}历史开奖官网通告数据", missing.Count, typeName);
        }

        /// <summary>福彩官网奖级明细：prizegrades（type 奖级序号 / typenum 全国注数 / typemoney 单注奖金）+ sales/poolmoney；content 为一等奖中奖地区文本（仅双色球有值）</summary>
        private static void FillCwlPrize(string type, LotteryDrawEntity draw, JsonElement item)
        {
            draw.SalesAmount = GetMoney(item, "sales");
            draw.PoolBalance = GetMoney(item, "poolmoney");
            var area = GetStr(item, "content");
            if (!string.IsNullOrWhiteSpace(area))
            {
                // 防御性截断：多省份中奖时文本较长，超出列宽会导致整批插入失败
                draw.PrizeArea = area.Length > 500 ? area[..500] : area;
            }
            if (!item.TryGetProperty("prizegrades", out var grades) || grades.ValueKind != JsonValueKind.Array)
                return;

            var list = new List<object>();
            foreach (var g in grades.EnumerateArray())
            {
                var gtype = g.TryGetProperty("type", out var tEl) && tEl.ValueKind == JsonValueKind.Number
                    ? tEl.GetInt32() : 0;
                var num = GetStr(g, "typenum");
                var money = GetStr(g, "typemoney");
                if (string.IsNullOrWhiteSpace(num) && string.IsNullOrWhiteSpace(money)) continue;

                // 双色球 type 1-6 → 一至六等奖，type 7 → 福运奖（仅执行特别规定期间有值，
                // 未执行时官方返回的注数与奖金均为空，已被上面的空值判断跳过，
                // 因此明细里出现福运奖就等于当期在执行，判奖侧据此决定 3+0 是否中奖）；
                // 福彩3D type 1/2/3 → 单选/组选3/组选6
                var grade = type == LotteryTypes.SSQ
                    ? gtype switch
                    {
                        >= 1 and <= 6 => $"第{"一二三四五六"[gtype - 1]}等奖",
                        7 => LotteryPrizeHelper.FuyunGrade,
                        _ => null,
                    }
                    : gtype switch { 1 => "单选", 2 => "组选3", 3 => "组选6", _ => null };
                if (grade == null) continue;

                list.Add(new { grade, count = ParseCount(num), money = ParseMoney(money) });
            }
            if (list.Count > 0) draw.PrizeDetail = JsonSerializer.Serialize(list);
        }

        /// <summary>体彩官网奖级明细：prizeLevelList（prizeLevel 奖级 / stakeCount 全国注数 / stakeAmount 单注奖金）+ totalSaleAmount/poolBalanceAfterdraw；drawPdfUrl 为官方开奖通告 PDF（含中奖地区等完整通告）</summary>
        private static void FillSportteryPrize(LotteryDrawEntity draw, JsonElement item)
        {
            draw.SalesAmount = GetMoney(item, "totalSaleAmount");
            draw.PoolBalance = GetMoney(item, "poolBalanceAfterdraw");
            var pdfUrl = GetStr(item, "drawPdfUrl");
            if (!string.IsNullOrWhiteSpace(pdfUrl)) draw.NoticeUrl = pdfUrl;
            if (!item.TryGetProperty("prizeLevelList", out var levels) || levels.ValueKind != JsonValueKind.Array)
                return;

            var list = new List<object>();
            foreach (var l in levels.EnumerateArray())
            {
                var grade = l.TryGetProperty("prizeLevel", out var gEl) ? gEl.GetString() : null;
                // 追加奖级与基础奖级重复，仅保留基础奖级
                if (string.IsNullOrWhiteSpace(grade) || grade.Contains("追加")) continue;

                list.Add(new
                {
                    grade,
                    count = ParseCount(l.TryGetProperty("stakeCount", out var c) ? c.GetString() : null),
                    money = ParseMoney(l.TryGetProperty("stakeAmount", out var m) ? m.GetString() : null),
                });
            }
            if (list.Count > 0) draw.PrizeDetail = JsonSerializer.Serialize(list);
        }

        private static string? GetStr(JsonElement el, string prop)
            => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        private static decimal? GetMoney(JsonElement el, string prop)
            => ParseMoney(GetStr(el, prop));

        /// <summary>金额文本 → decimal（兼容千分位逗号），非法返回 null</summary>
        private static decimal? ParseMoney(string? raw)
            => decimal.TryParse((raw ?? string.Empty).Replace(",", "").Trim(), out var v) ? v : null;

        /// <summary>注数文本 → long（兼容千分位逗号），非法返回 null</summary>
        private static long? ParseCount(string? raw)
            => long.TryParse((raw ?? string.Empty).Replace(",", "").Trim(), out var v) ? v : null;
    }
}

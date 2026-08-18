using ConvenientSystem.Shared.Common;
using ConvenientSystem.Shared.Entity.Common;
using ConvenientSystem.Shared.Model.Common;
using FreeSql;
using Hangfire;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;


namespace ConvenientSystem.Shared.Jobs
{
    /// <summary>
    /// 彩票玩法规则自动抓取 Hangfire Job（每日一次，四彩种）。
    /// 数据源为中彩网「游戏规则」栏目公开条文页（静态 HTML），按标题定位文章以抗改版。
    /// 条文内容变化时不直接生效：新版本一律入库为待审核，由用户在页面比对新旧差异后确认启用；
    /// 库内尚无生效版本时（首次部署），仅当解析结果与内置兜底规则判奖完全等价才自动生效，
    /// 避免第一次运行就必须手工点四遍，同时保证自动生效的版本判奖行为可信。
    /// </summary>
    public class LotteryRuleCrawlJob : JobBase
    {
        private readonly ILogger<LotteryRuleCrawlJob> _logger;

        /// <summary>共享 HttpClient（官网条文页直连用）</summary>
        private static readonly HttpClient Http = new();

        /// <summary>「游戏规则」栏目索引页：比写死文章 ID 更抗官网改版</summary>
        private const string IndexUrl = "https://www.zhcw.com/czfw/cpbd/yxgz/";

        private const string SiteRoot = "https://www.zhcw.com";

        /// <summary>索引页链接：href 与锚文本</summary>
        private static readonly Regex LinkTag = new(@"<a\s[^>]*href=""([^""]+\.shtml)""[^>]*>([\s\S]*?)</a>",
            RegexOptions.IgnoreCase);

        private static readonly Regex AnyTag = new(@"<[^>]+>");

        /// <summary>规则文章标题特征：彩种关键词 + "游戏规则"</summary>
        private static string TitleKeyword(string type) => type switch
        {
            LotteryTypes.SSQ => "双色球",
            LotteryTypes.PL5 => "排列5",
            LotteryTypes.FC3D => "福彩3D",
            _ => "超级大乐透",
        };

        /// <summary>索引页定位失败时的兜底文章地址（现行有效条文）</summary>
        private static string FallbackUrl(string type) => type switch
        {
            LotteryTypes.SSQ => SiteRoot + "/c/2026-01-29/557266.shtml",
            LotteryTypes.PL5 => SiteRoot + "/c/2019-08-10/558099.shtml",
            LotteryTypes.FC3D => SiteRoot + "/c/2019-08-14/557268.shtml",
            _ => SiteRoot + "/c/2026-01-31/557270.shtml",
        };

        public LotteryRuleCrawlJob(
            [FromKeyedServices("ConvenientSystemDb")] IFreeSql fsql,
            IJobExecutionLogService jobLog,
            ILogger<LotteryRuleCrawlJob> logger) : base(fsql, jobLog)
        {
            _logger = logger;
        }

        /// <summary>
        /// 抓取全部彩种玩法规则，返回入库的新版本数（含自动生效与待审核）
        /// </summary>
        [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 60, 300, 900 })]
        public async Task<int> CrawlAllAsync(CancellationToken ct = default)
        {
            var total = 0;
            foreach (var type in LotteryTypes.All)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    total += await CrawlAsync(type, ct);
                }
                catch (Exception ex)
                {
                    // 单个彩种抓取失败不影响其他彩种（判奖有生效版本或内置规则兜底）
                    _logger.LogError(ex, "{Type}玩法规则抓取失败", LotteryTypes.GetName(type));
                }
                await Task.Delay(1000, ct);
            }
            _logger.LogInformation("玩法规则抓取完成，新增 {Count} 个版本", total);
            return total;
        }

        /// <summary>
        /// 抓取单个彩种玩法规则：解析条文 → 与生效版本比对内容指纹 → 无变化只更新抓取时间，
        /// 有变化入库新版本（首次且判奖等价时自动生效，否则待审核）。返回入库的新版本数（0 或 1）
        /// </summary>
        [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 60, 300, 900 })]
        public Task<int> CrawlAsync(string type = LotteryTypes.DLT, CancellationToken ct = default)
        {
            var t = LotteryTypes.Normalize(type);
            var jobName = $"{LotteryTypes.GetName(t)}玩法规则抓取";
            return ExecuteWithLog<int>(jobName, nameof(CrawlAsync), new { type }, async () =>
            {
            var typeName = LotteryTypes.GetName(t);

            var url = await ResolveArticleUrlAsync(t, ct) ?? FallbackUrl(t);
            var html = await FetchAsync(url, ct);
            if (html == null)
            {
                _logger.LogWarning("{Type}玩法规则页抓取失败：{Url}", typeName, url);
                return 0;
            }

            var text = LotteryRuleParser.HtmlToText(html);
            var parsed = LotteryRuleParser.Parse(t, text);
            if (parsed == null)
            {
                // 官网改版导致条文结构不认识时宁可不入库，判奖继续用生效版本或内置规则
                _logger.LogWarning("{Type}玩法规则解析失败（条文结构不符预期），本次不入库：{Url}", typeName, url);
                return 0;
            }

            var gradeJson = LotteryRuleCache.Serialize(parsed.Grades);
            var hash = Sha256(text + "\n" + gradeJson);
            var now = DateTime.Now;

            var active = Fsql.Select<LotteryRuleEntity>()
                .Where(r => r.LotteryType == t && r.Status == LotteryRuleStatus.Active)
                .OrderByDescending(r => r.Version)
                .First();

            if (active != null && active.ContentHash == hash)
            {
                Fsql.Update<LotteryRuleEntity>()
                    .Set(r => r.CrawledAt, now)
                    .Set(r => r.SourceUrl, url)
                    .Where(r => r.Id == active.Id)
                    .ExecuteAffrows();
                _logger.LogInformation("{Type}玩法规则无变化（版本 {Version}）", typeName, active.Version);
                return 0;
            }

            // 同一份差异条文可能连续多天抓到，已在待审队列里的不重复堆版本
            var samePending = Fsql.Select<LotteryRuleEntity>()
                .Where(r => r.LotteryType == t && r.Status == LotteryRuleStatus.Pending && r.ContentHash == hash)
                .Any();
            if (samePending)
            {
                _logger.LogInformation("{Type}玩法规则变更已在待审核队列中，跳过入库", typeName);
                return 0;
            }

            var maxVersion = Fsql.Select<LotteryRuleEntity>()
                .Where(r => r.LotteryType == t)
                .Max(r => (int?)r.Version) ?? 0;

            var row = new LotteryRuleEntity
            {
                LotteryType = t,
                Version = maxVersion + 1,
                SourceUrl = url,
                RuleText = text,
                GradeJson = gradeJson,
                GradeCount = parsed.Grades.Count,
                ContentHash = hash,
                CrawledAt = now,
            };

            // 首次抓取：与内置兜底规则判奖等价才自动生效，不等价说明官网条文与代码认知不一致，必须人工确认
            var firstTime = active == null;
            var diff = firstTime
                ? LotteryRuleComparer.Diff(t, LotteryRuleDefaults.Get(t), parsed)
                : LotteryRuleComparer.Diff(t, ActiveRule(t, active!), parsed);

            if (firstTime && diff.Count == 0)
            {
                row.Status = LotteryRuleStatus.Active;
                row.EffectiveAt = now;
                row.ReviewedBy = "系统";
                row.Remark = "首次抓取，与内置规则判奖一致，自动生效";
            }
            else
            {
                row.Status = LotteryRuleStatus.Pending;
                row.Remark = Truncate(diff.Count == 0
                    ? "条文内容有更新，判奖结果不变"
                    : "判奖差异：" + string.Join("；", diff), 500);
            }

            row.Id = (int)Fsql.Insert(row).ExecuteIdentity();

            // 旧的待审核版本已被本次更新取代，避免审核页出现多份过期待审
            Fsql.Update<LotteryRuleEntity>()
                .Set(r => r.Status, LotteryRuleStatus.Replaced)
                .Where(r => r.LotteryType == t && r.Status == LotteryRuleStatus.Pending && r.Id != row.Id)
                .ExecuteAffrows();

            if (row.Status == LotteryRuleStatus.Active)
            {
                LotteryRuleCache.Invalidate(t);
                _logger.LogInformation("{Type}玩法规则首次入库并自动生效（版本 {Version}，{Count} 个奖级）",
                    typeName, row.Version, row.GradeCount);
            }
            else
            {
                _logger.LogWarning("{Type}玩法规则发生变更，已入库待审核（版本 {Version}）：{Remark}",
                    typeName, row.Version, row.Remark);
            }
            return 1;
            });
        }

        /// <summary>生效版本的规则（JSON 不可用时退回内置规则，只用于差异比对）</summary>
        private static LotteryRuleDto ActiveRule(string type, LotteryRuleEntity active)
        {
            var grades = LotteryRuleCache.Deserialize(active.GradeJson);
            return grades == null
                ? LotteryRuleDefaults.Get(type)
                : new LotteryRuleDto { LotteryType = type, Grades = grades };
        }

        /// <summary>从「游戏规则」索引页按标题定位彩种规则文章地址</summary>
        private async Task<string?> ResolveArticleUrlAsync(string type, CancellationToken ct)
        {
            var html = await FetchAsync(IndexUrl, ct);
            if (html == null) return null;

            var keyword = TitleKeyword(type);
            foreach (Match m in LinkTag.Matches(html))
            {
                var title = AnyTag.Replace(m.Groups[2].Value, string.Empty).Trim();
                if (!title.Contains(keyword) || !title.Contains("游戏规则")) continue;

                var href = m.Groups[1].Value.Trim();
                return href.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? href
                    : SiteRoot + (href.StartsWith('/') ? href : "/" + href);
            }
            _logger.LogWarning("索引页未找到{Keyword}游戏规则链接，改用兜底地址", keyword);
            return null;
        }

        /// <summary>抓取页面 HTML（官网为 UTF-8，按字节解码避免响应头缺 charset 时乱码）</summary>
        private async Task<string?> FetchAsync(string url, CancellationToken ct)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("Referer", SiteRoot + "/");
            req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");

            using var resp = await Http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("规则页 {Url} 返回 {Status}", url, (int)resp.StatusCode);
                return null;
            }
            var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
            return Encoding.UTF8.GetString(bytes);
        }

        /// <summary>条文内容指纹（条文原文 + 解析结果，任一变化都算新版本）</summary>
        private static string Sha256(string content)
            => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

        private static string Truncate(string text, int max)
            => text.Length <= max ? text : text.Substring(0, max);
    }
}

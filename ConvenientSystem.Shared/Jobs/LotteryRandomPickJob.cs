using ConvenientSystem.Shared.Common;
using ConvenientSystem.Shared.Entity.Common;
using ConvenientSystem.Shared.Model.Common;
using FreeSql;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace ConvenientSystem.Shared.Jobs
{
    /// <summary>
    /// 每天定时为当天开奖的彩种随机生成选号记录。
    /// 每个彩种为每个启用用户生成 10 注随机号码，保存到 LotteryRecordEntity 表，
    /// 归属到下一期（期号 = 最新开奖期号 + 1，开奖日 = 最近开奖日）。
    /// </summary>
    public class LotteryRandomPickJob
    {
        private readonly IFreeSql _fsql;
        private readonly ILogger<LotteryRandomPickJob> _logger;

        public LotteryRandomPickJob(
            [FromKeyedServices("ConvenientSystemDb")] IFreeSql fsql,
            ILogger<LotteryRandomPickJob> logger)
        {
            _fsql = fsql;
            _logger = logger;
        }

        /// <summary>
        /// 每天下午 1 点执行：为当天开奖的每个彩种、每个启用用户随机生成 10 注选号记录
        /// </summary>
        [AutomaticRetry(Attempts = 2, DelaysInSeconds = new[] { 300, 900 })]
        public async Task DailyRandomPickAsync(CancellationToken ct = default)
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

            // 全部启用的用户
            var users = _fsql.Select<SysUserEntity>()
                .Where(u => u.Enabled)
                .ToList();
            if (users.Count == 0)
            {
                _logger.LogInformation("无启用用户，跳过随机生成");
                return;
            }

            var totalGenerated = 0;
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
                    var affrows = await _fsql.Insert(entities).ExecuteAffrowsAsync();
                    totalGenerated += affrows;
                }

                _logger.LogInformation("{Type}已为 {Count} 个用户各生成 10 注随机选号（期号 {Issue}）",
                    typeName, users.Count, issue);
            }

            _logger.LogInformation("当天（{Date}）随机生成完成，共 {Count} 条选号记录",
                today.ToString("yyyy-MM-dd"), totalGenerated);
        }

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
            var latest = _fsql.Select<LotteryDrawEntity>()
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

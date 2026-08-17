using ConvenientSystem.Shared.Entity.Common;
using ConvenientSystem.Shared.Model.Common;
using Microsoft.AspNetCore.Http;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// 彩票智能分析服务：基于历史开奖数据的多维度评分与号码推荐。
    /// 5 维信号：热号频率、冷号回补、遗漏极值、连号趋势、区间均衡。
    /// </summary>
    public class LotteryAnalysisService : ILotteryAnalysisService
    {
        private readonly IFreeSql _fsql;

        // 5 维权重（合计 1.0）
        private const double W_HOT = 0.25;
        private const double W_COLD = 0.30;
        private const double W_MISS = 0.15;
        private const double W_CONSEC = 0.15;
        private const double W_ZONE = 0.15;

        public LotteryAnalysisService(
            [FromKeyedServices("ConvenientSystemDb")] IFreeSql fsql)
        {
            _fsql = fsql;
        }

        public LotteryAnalysisDto Predict(string type, int periods = 100)
        {
            var t = LotteryTypes.Normalize(type);
            var positional = LotteryTypes.IsPositional(t);
            periods = Math.Clamp(periods, 30, 500);

            // 1. 查询最近 N 期开奖记录（按期号正序：从旧到新）
            var draws = _fsql.Select<LotteryDrawEntity>()
                .Where(d => d.LotteryType == t)
                .OrderByDescending(d => d.IssueNumber)
                .Take(periods)
                .ToList()
                .Select(MapToDto)
                .Reverse() // 逆转为从旧到新
                .ToList();

            if (draws.Count == 0)
                return new LotteryAnalysisDto { Type = t, TypeName = LotteryTypes.GetName(t) };

            var pickZones = LotteryTypes.GetPickZones(t);
            var frontZone = pickZones.FirstOrDefault(z => z.Source == "front")
                ?? pickZones.First();
            var backZone = pickZones.FirstOrDefault(z => z.Source == "back");

            // 2. 对每个分区计算评分
            var frontScores = ScoreZone(draws, frontZone, periods);
            var backScores = backZone != null && !positional
                ? ScoreZone(draws, backZone, periods)
                : new List<NumberScoreDto>();

            // 位置型彩种：每个位（分区）独立评分，合并到 frontScores
            if (positional)
            {
                frontScores = new List<NumberScoreDto>();
                foreach (var zone in pickZones)
                    frontScores.AddRange(ScoreZone(draws, zone, periods));
            }

            // 3. 推荐号码：按评分降序取 Pick 个
            var frontPick = frontZone.Pick;
            var backPick = backZone?.Pick ?? 0;

            var recommendedFront = positional
                ? RecommendPositional(pickZones, frontScores)
                : frontScores.OrderByDescending(s => s.Score).Take(frontPick).Select(s => s.Number).OrderBy(n => n).ToArray();

            var recommendedBack = backScores.OrderByDescending(s => s.Score).Take(backPick).Select(s => s.Number).OrderBy(n => n).ToArray();

            // 4. 热号池 & 冷号池（前区）
            var hotNumbers = frontScores.OrderByDescending(s => s.HotScore).Take(10).Select(s => s.Number).ToArray();
            var coldNumbers = frontScores.OrderByDescending(s => s.CurrentMiss).Take(10).Select(s => s.Number).ToArray();

            // 5. AI 组合（3~5 注）
            var generatedBets = GenerateBets(t, pickZones, frontScores, backScores, positional);

            // 6. 下一期信息
            var (nextIssue, nextDrawDate) = GetNextIssueAndDate(t);

            // 7. 分析摘要
            var summary = BuildSummary(t, draws.Count, frontScores, backScores, positional, pickZones);

            return new LotteryAnalysisDto
            {
                Type = t,
                TypeName = LotteryTypes.GetName(t),
                Periods = draws.Count,
                NextIssue = nextIssue,
                NextDrawDate = nextDrawDate,
                FrontScores = frontScores,
                BackScores = backScores,
                RecommendedFront = recommendedFront,
                RecommendedBack = recommendedBack,
                HotNumbers = hotNumbers,
                ColdNumbers = coldNumbers,
                GeneratedBets = generatedBets,
                Summary = summary,
            };
        }

        /// <summary>对单个分区内每个号码进行 5 维评分</summary>
        private static List<NumberScoreDto> ScoreZone(List<LotteryDrawDto> draws, LotteryZoneDto zone, int totalPeriods)
        {
            var scores = new List<NumberScoreDto>();
            var recentWindow = Math.Min(10, draws.Count);

            // 预计算：每个号码的出现索引列表
            var hitMap = new Dictionary<int, List<int>>();
            foreach (var num in zone.Numbers)
            {
                var indices = new List<int>();
                for (int i = 0; i < draws.Count; i++)
                {
                    if (IsHit(draws[i], zone, num))
                        indices.Add(i);
                }
                hitMap[num] = indices;
            }

            // 区间均衡：统计近 N 期该分区每个号码出现的期望值 vs 实际值
            var avgFreq = (double)totalPeriods * zone.Pick / zone.Numbers.Length;

            // 连号分析：近 recentWindow 期中，相邻号码同出的频率
            var recentDraws = draws.TakeLast(recentWindow).ToList();

            foreach (var num in zone.Numbers)
            {
                var indices = hitMap[num];
                var count = indices.Count;

                // 当前遗漏
                int currentMiss = count == 0
                    ? totalPeriods
                    : totalPeriods - 1 - indices.Last();

                // 平均遗漏
                double avgMiss;
                int maxMiss;
                if (count == 0)
                {
                    avgMiss = totalPeriods;
                    maxMiss = totalPeriods;
                }
                else
                {
                    var misses = new List<int> { indices[0] };
                    for (int i = 1; i < indices.Count; i++)
                        misses.Add(indices[i] - indices[i - 1] - 1);
                    maxMiss = misses.Max();
                    avgMiss = misses.Average();
                }

                // ── 维度 1：热号分（出现频率 / 期望频率）──
                var expectedFreq = (double)totalPeriods * zone.Pick / zone.Numbers.Length;
                double hotScore = expectedFreq > 0
                    ? Math.Min(count / expectedFreq, 2.0) / 2.0 * 100
                    : 50;

                // ── 维度 2：冷号回补分（CurrentMiss / AvgMiss）──
                double coldScore = avgMiss > 0
                    ? Math.Min(currentMiss / avgMiss, 3.0) / 3.0 * 100
                    : 50;

                // ── 维度 3：遗漏极值分（CurrentMiss / MaxMiss）──
                double missScore = maxMiss > 0
                    ? (double)currentMiss / maxMiss * 100
                    : 50;

                // ── 维度 4：连号趋势分 ──
                // 池选型：近 recentWindow 期中，该号码与 ±1 号码同时出现的期数占比
                // 位置型：不适用，给 50 分
                double consecScore;
                if (zone.Positional)
                {
                    consecScore = 50;
                }
                else
                {
                    int consecCount = 0;
                    foreach (var draw in recentDraws)
                    {
                        var pool = zone.Source == "front" ? draw.Front : draw.Back;
                        if (pool.Contains(num) && (pool.Contains(num - 1) || pool.Contains(num + 1)))
                            consecCount++;
                    }
                    consecScore = recentWindow > 0
                        ? (double)consecCount / recentWindow * 100
                        : 50;
                }

                // ── 维度 5：区间均衡分 ──
                // 把号码池分成 3 个区间，统计各区间近期出现频率，偏冷的区间加分
                double zoneScore = 50;
                if (!zone.Positional)
                {
                    var nums = zone.Numbers;
                    int third = nums.Length / 3;
                    int zoneIdx = Array.IndexOf(nums, num) / Math.Max(third, 1);
                    zoneIdx = Math.Clamp(zoneIdx, 0, 2);

                    var zoneCounts = new int[3];
                    foreach (var draw in draws)
                    {
                        var pool = zone.Source == "front" ? draw.Front : draw.Back;
                        foreach (var n in pool)
                        {
                            int idx = Array.IndexOf(nums, n);
                            if (idx < 0) continue;
                            int z = idx / Math.Max(third, 1);
                            z = Math.Clamp(z, 0, 2);
                            zoneCounts[z]++;
                        }
                    }

                    var totalZoneHits = zoneCounts.Sum();
                    var expectedPerZone = totalZoneHits > 0 ? totalZoneHits / 3.0 : 1;
                    // 偏冷区间（实际 < 期望）→ 高分
                    var ratio = expectedPerZone > 0 ? zoneCounts[zoneIdx] / expectedPerZone : 1;
                    zoneScore = Math.Max(0, Math.Min(100, (2.0 - ratio) / 2.0 * 100));
                }

                // ── 综合评分 ──
                double score = W_HOT * hotScore
                    + W_COLD * coldScore
                    + W_MISS * missScore
                    + W_CONSEC * consecScore
                    + W_ZONE * zoneScore;

                scores.Add(new NumberScoreDto
                {
                    Number = num,
                    Score = Math.Round(score, 1),
                    HotScore = Math.Round(hotScore, 1),
                    ColdScore = Math.Round(coldScore, 1),
                    MissScore = Math.Round(missScore, 1),
                    ConsecutiveScore = Math.Round(consecScore, 1),
                    ZoneScore = Math.Round(zoneScore, 1),
                    CurrentMiss = currentMiss,
                    AvgMiss = Math.Round(avgMiss, 2),
                    MaxMiss = maxMiss,
                    Count = count,
                    ZoneLabel = zone.Label,
                });
            }

            return scores;
        }

        /// <summary>位置型彩种：每位选评分最高的号码</summary>
        private static int[] RecommendPositional(List<LotteryZoneDto> zones, List<NumberScoreDto> allScores)
        {
            var result = new int[zones.Count];
            int offset = 0;
            int zi = 0;
            foreach (var zone in zones)
            {
                var zoneScores = allScores.Skip(offset).Take(zone.Numbers.Length).ToList();
                result[zi] = zoneScores.OrderByDescending(s => s.Score).First().Number;
                offset += zone.Numbers.Length;
                zi++;
            }
            return result;
        }

        /// <summary>AI 组合：从高分号码池中按区间均衡生成 3~5 注</summary>
        private static List<LotteryBetItem> GenerateBets(string type, List<LotteryZoneDto> pickZones,
            List<NumberScoreDto> frontScores, List<NumberScoreDto> backScores, bool positional)
        {
            var bets = new List<LotteryBetItem>();
            var frontZone = pickZones.FirstOrDefault(z => z.Source == "front") ?? pickZones.First();
            var backZone = pickZones.FirstOrDefault(z => z.Source == "back");

            for (int betIdx = 0; betIdx < 5; betIdx++)
            {
                int[] front;
                int[] back;

                if (positional)
                {
                    // 位置型：每位从 Top 3 中按 betIdx 偏移取
                    front = new int[pickZones.Count];
                    int offset = 0;
                    for (int zi = 0; zi < pickZones.Count; zi++)
                    {
                        var zone = pickZones[zi];
                        var top = frontScores.Skip(offset).Take(zone.Numbers.Length)
                            .OrderByDescending(s => s.Score).Take(3).Select(s => s.Number).ToArray();
                        front[zi] = top[betIdx % top.Length];
                        offset += zone.Numbers.Length;
                    }
                    back = Array.Empty<int>();
                }
                else
                {
                    // 池选型：从 Top (pick+2) 中按 betIdx 偏移轮取
                    var topN = Math.Min(frontScores.Count, frontZone.Pick + 2 + betIdx);
                    var pool = frontScores.OrderByDescending(s => s.Score).Take(topN).Select(s => s.Number).ToList();

                    // 每次偏移起始位置，保证 5 注不完全一样
                    var selected = new List<int>();
                    int startOffset = betIdx;
                    for (int i = 0; i < frontZone.Pick; i++)
                    {
                        selected.Add(pool[(startOffset + i) % pool.Count]);
                    }
                    front = selected.Distinct().OrderBy(n => n).ToArray();

                    // 后区
                    if (backZone != null && backScores.Count > 0)
                    {
                        var backTop = backScores.OrderByDescending(s => s.Score)
                            .Take(Math.Min(backScores.Count, backZone.Pick + 1))
                            .Select(s => s.Number).ToList();
                        var backSelected = new List<int>();
                        for (int i = 0; i < backZone.Pick; i++)
                            backSelected.Add(backTop[(betIdx + i) % backTop.Count]);
                        back = backSelected.Distinct().OrderBy(n => n).ToArray();
                    }
                    else
                    {
                        back = Array.Empty<int>();
                    }
                }

                bets.Add(new LotteryBetItem { Front = front, Back = back });
            }

            return bets;
        }

        /// <summary>生成分析摘要文字</summary>
        private static string BuildSummary(string type, int periods, List<NumberScoreDto> frontScores,
            List<NumberScoreDto> backScores, bool positional, List<LotteryZoneDto> zones)
        {
            var typeName = LotteryTypes.GetName(type);
            var parts = new List<string>();
            parts.Add($"基于近 {periods} 期{typeName}开奖数据分析：");

            if (!positional)
            {
                // 前区遗漏预警
                var coldFront = frontScores.Where(s => s.CurrentMiss >= s.AvgMiss * 1.5 && s.CurrentMiss > 5)
                    .OrderByDescending(s => s.CurrentMiss).Take(3).ToList();
                if (coldFront.Count > 0)
                {
                    var coldText = string.Join("、", coldFront.Select(s =>
                        $"{s.Number:D2}（已遗漏 {s.CurrentMiss} 期，均值 {s.AvgMiss:F0}）"));
                    parts.Add($"前区严重遗漏号码：{coldText}。");
                }

                // 热号
                var hotFront = frontScores.OrderByDescending(s => s.Count).Take(3).ToList();
                if (hotFront.Count > 0)
                {
                    var hotText = string.Join("、", hotFront.Select(s => $"{s.Number:D2}（出现 {s.Count} 次）"));
                    parts.Add($"近期热号：{hotText}。");
                }

                // 后区
                if (backScores.Count > 0)
                {
                    var coldBack = backScores.OrderByDescending(s => s.CurrentMiss).Take(2).ToList();
                    if (coldBack.Count > 0 && coldBack.Any(s => s.CurrentMiss > 3))
                    {
                        var backText = string.Join("、", coldBack.Select(s =>
                            $"{s.Number:D2}（遗漏 {s.CurrentMiss} 期）"));
                        parts.Add($"后区关注：{backText}。");
                    }
                }
            }
            else
            {
                // 位置型：每位推荐
                int offset = 0;
                foreach (var zone in zones)
                {
                    var top = frontScores.Skip(offset).Take(zone.Numbers.Length)
                        .OrderByDescending(s => s.Score).First();
                    parts.Add($"{zone.Label}推荐：{top.Number}（评分 {top.Score:F0}）");
                    offset += zone.Numbers.Length;
                }
            }

            parts.Add("⚠️ 以上分析仅供参考，彩票本质为随机事件，请理性购彩。");
            return string.Join("\n", parts);
        }

        // ─── 工具方法 ───

        private static bool IsHit(LotteryDrawDto draw, LotteryZoneDto group, int num)
        {
            if (group.Positional)
                return group.PosIndex < draw.Front.Length && draw.Front[group.PosIndex] == num;
            return group.Source == "front"
                ? draw.Front.Contains(num)
                : draw.Back.Contains(num);
        }

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

        private static int[] ParseNumbers(string raw, bool positional)
            => raw.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.TryParse(s.Trim(), out var n) ? n : int.MinValue)
                .Where(n => positional ? n >= 0 : n > 0)
                .ToArray();

        private static LotteryDrawDto MapToDto(LotteryDrawEntity d)
        {
            var positional = LotteryTypes.IsPositional(d.LotteryType);
            return new LotteryDrawDto
            {
                Id = d.Id,
                IssueNumber = d.IssueNumber,
                DrawDate = d.DrawDate,
                Front = ParseNumbers(d.FrontNumbers, positional),
                Back = ParseNumbers(d.BackNumbers, positional),
            };
        }
    }
}

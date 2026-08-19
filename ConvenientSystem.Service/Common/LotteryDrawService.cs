using ConvenientSystem.Shared.Common;
using ConvenientSystem.Shared.Common.Exceptions;
using ConvenientSystem.Shared.Entity.Common;
using ConvenientSystem.Shared.Model.Common;
using Microsoft.AspNetCore.Http;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// 彩票开奖记录服务实现：管理多彩种（大乐透/双色球/排列五/福彩3D）开奖数据、计算号码走势统计。
    /// </summary>
    public class LotteryDrawService : ILotteryDrawService
    {
        private readonly IFreeSql _fsql;

        public LotteryDrawService(
            [FromKeyedServices("ConvenientSystemDb")] IFreeSql fsql)
        {
            _fsql = fsql;
        }

        public LotteryConfigDto GetConfig(string type)
        {
            var t = LotteryTypes.Normalize(type);
            return new LotteryConfigDto
            {
                Code = t,
                Name = LotteryTypes.GetName(t),
                PickZones = LotteryTypes.GetPickZones(t),
            };
        }

        public PagedResult<LotteryDrawDto> GetDraws(string type, int page, int size, string? sortField = null, string? sortOrder = null)
        {
            var t = LotteryTypes.Normalize(type);
            var query = _fsql.Select<LotteryDrawEntity>().Where(d => d.LotteryType == t);
            var total = query.Count();
            var sortedQuery = string.IsNullOrWhiteSpace(sortField) ? query.OrderByDescending(d => d.IssueNumber) : query.OrderByDynamic(sortField, sortOrder);
            var list = sortedQuery
                .Skip((page - 1) * size).Take(size)
                .ToList()
                .Select(MapToDto)
                .ToList();

            return new PagedResult<LotteryDrawDto> { Total = total, List = list };
        }

        public int ImportDraws(string type, List<LotteryDrawItem> draws)
        {
            if (draws == null || draws.Count == 0) return 0;

            var t = LotteryTypes.Normalize(type);
            var positional = LotteryTypes.IsPositional(t);

            var entities = draws.Select(d => new LotteryDrawEntity
            {
                LotteryType = t,
                IssueNumber = d.IssueNumber,
                DrawDate = DateTime.Parse(d.DrawDate),
                // 位置型按位存储（不排序、允许 0）；池选型升序补零
                FrontNumbers = FormatNumbers(d.Front, positional),
                BackNumbers = FormatNumbers(d.Back, positional),
            }).ToList();

            // 跳过同彩种已存在的期号
            var existingIssues = _fsql.Select<LotteryDrawEntity>()
                .Where(d => d.LotteryType == t && entities.Select(e => e.IssueNumber).Contains(d.IssueNumber))
                .ToList(d => d.IssueNumber)
                .ToHashSet();

            var newEntities = entities.Where(e => !existingIssues.Contains(e.IssueNumber)).ToList();
            if (newEntities.Count > 0)
                _fsql.Insert(newEntities).ExecuteAffrows();

            return newEntities.Count;
        }

        public bool DeleteDraw(int id)
        {
            return _fsql.Delete<LotteryDrawEntity>().Where(d => d.Id == id).ExecuteAffrows() > 0;
        }

        /// <summary>
        /// 历史号码匹配模式的展示上限：条件宽松时（如只选 1 个号码）可能命中上千期，
        /// 按期号降序截断后仅保留最近的部分，与走势图窗口上限保持同一量级。
        /// </summary>
        private const int MatchMaxPeriods = 500;

        public LotteryTrendDto GetTrend(string type, int periods, DateTime? startDate = null, DateTime? endDate = null,
            int[]? matchFront = null, int[]? matchBack = null, Dictionary<int, int[]>? matchPos = null)
        {
            var t = LotteryTypes.Normalize(type);
            var mFront = matchFront?.Distinct().ToArray() ?? Array.Empty<int>();
            var mBack = matchBack?.Distinct().ToArray() ?? Array.Empty<int>();
            // 数位条件（位置型彩种：键为数位序号，值为该位的候选数字）
            var mPos = matchPos ?? new Dictionary<int, int[]>();
            // 历史号码匹配模式：在全库范围内检索，统计期数与开奖日期区间均不参与
            var isMatch = mFront.Length > 0 || mBack.Length > 0 || mPos.Count > 0;

            var result = new LotteryTrendDto
            {
                Groups = LotteryTypes.GetTrendGroups(t),
                MatchMode = isMatch,
            };

            List<LotteryDrawDto> draws;
            if (isMatch)
            {
                draws = MatchDraws(t, mFront, mBack, mPos, out var matchTotal);
                result.MatchTotal = matchTotal;
            }
            else
            {
                var hasDateRange = startDate.HasValue || endDate.HasValue;

                var query = _fsql.Select<LotteryDrawEntity>()
                    .Where(d => d.LotteryType == t);

                if (hasDateRange)
                {
                    // 按开奖日期区间筛选（含端点，endDate 含当天全天）
                    if (startDate.HasValue)
                        query = query.Where(d => d.DrawDate >= startDate.Value.Date);
                    if (endDate.HasValue)
                        query = query.Where(d => d.DrawDate < endDate.Value.Date.AddDays(1));
                }
                else
                {
                    // 未指定日期：取最近 N 期
                    if (periods < 10) periods = 10;
                    if (periods > 500) periods = 500;
                }

                // 按期号正序排列，从旧到新；日期区间模式下限制最大 2000 期防止过大
                draws = query
                    .OrderByDescending(d => d.IssueNumber)
                    .Take(hasDateRange ? 2000 : periods)
                    .ToList()
                    .Select(MapToDto)
                    .Reverse()
                    .ToList();
            }

            result.TotalPeriods = draws.Count;
            result.Draws = draws;

            // 逐分区计算号码统计
            foreach (var group in result.Groups)
                group.Stats = CalculateStats(draws, group);

            // 逐分区计算展示窗口首期之前的历史遗漏（走势图遗漏种子，避免前端仅按可视窗口累加导致边界失真）；
            // 种子值仅在期号连续的窗口模式下成立，匹配模式只保留命中期、期与期不相邻且前端不展示遗漏，故跳过（同时省去一次万级查询）
            if (!isMatch && draws.Count > 0)
            {
                var oldestIssue = draws[0].IssueNumber;
                var olderDraws = _fsql.Select<LotteryDrawEntity>()
                    // 字符串期号比较用 CompareTo（FreeSql 表达式不支持 < 直接作用于 string）
                    .Where(d => d.LotteryType == t && d.IssueNumber.CompareTo(oldestIssue) < 0)
                    .OrderByDescending(d => d.IssueNumber)
                    // 防御上限：彩票历史总量有限，正常不会触及；极端情况下种子值截断在 10000 期
                    .Take(10000)
                    .ToList()
                    .Select(MapToDto)
                    .ToList();
                foreach (var group in result.Groups)
                    CalculateInitialMiss(olderDraws, group, group.Stats!);
            }

            return result;
        }

        /// <summary>
        /// 历史号码匹配：全库检索同时满足全部条件的期（条件之间是“且”，不是命中任意一个）。
        /// 号码集合条件要求该期包含所选的每一个号码（选 5 个就要 5 个全开出），
        /// 数位条件要求指定的每一个数位都对上，两者可同时生效。
        /// 结果按期号降序（新期在前）截断到展示上限。
        /// </summary>
        /// <param name="matchTotal">截断前的匹配总期数</param>
        private List<LotteryDrawDto> MatchDraws(string type, int[] front, int[] back,
            Dictionary<int, int[]> pos, out int matchTotal)
        {
            var all = _fsql.Select<LotteryDrawEntity>()
                .Where(d => d.LotteryType == type)
                .OrderByDescending(d => d.IssueNumber)
                // 防御上限：彩票历史总量有限，正常不会触及
                .Take(10000)
                .ToList()
                .Select(MapToDto)
                .ToList();

            var matched = new List<LotteryDrawDto>();
            foreach (var d in all)
            {
                // 所选号码必须全部开出：位置型彩种的 Front 是各位数字，此处不看位置只看是否含有
                if (!front.All(n => d.Front.Contains(n))) continue;
                if (!back.All(n => d.Back.Contains(n))) continue;
                // 数位条件：指定的每一位都要对上（该位开出的数字在该位候选内）
                if (pos.Any(kv => kv.Key >= d.Front.Length || !kv.Value.Contains(d.Front[kv.Key]))) continue;
                matched.Add(d);
            }
            matchTotal = matched.Count;

            // all 已按期号降序取出，此处仅截断：条件全命中后各期之间无优劣之分，按期号展示最近的部分
            return matched.Take(MatchMaxPeriods).ToList();
        }

        /// <summary>
        /// 计算各号码在展示窗口首期之前的历史遗漏：自窗口前一期向更早扫描，
        /// 命中即停、未命中递增，结果写入 Stats 的 InitialMiss（前端遗漏展示的种子值）。
        /// </summary>
        private static void CalculateInitialMiss(List<LotteryDrawDto> olderDraws, LotteryZoneDto group, List<NumberStatDto> stats)
        {
            var pending = group.Numbers.ToHashSet();
            foreach (var draw in olderDraws)
            {
                if (pending.Count == 0) break;
                foreach (var num in pending.ToList())
                {
                    if (IsHit(draw, group, num))
                        pending.Remove(num);
                    else
                        stats.First(s => s.Number == num).InitialMiss++;
                }
            }
            // 历史中从未出现的号码：种子遗漏 = 全部早期期数（上面循环已逐期累加，无需额外处理）
        }

        /// <summary>判断某号码在某期是否命中该分区</summary>
        private static bool IsHit(LotteryDrawDto draw, LotteryZoneDto group, int num)
        {
            if (group.Positional)
                return group.PosIndex < draw.Front.Length && draw.Front[group.PosIndex] == num;
            return group.Source == "front"
                ? draw.Front.Contains(num)
                : draw.Back.Contains(num);
        }

        /// <summary>计算分区内每个号码的统计</summary>
        private static List<NumberStatDto> CalculateStats(List<LotteryDrawDto> draws, LotteryZoneDto group)
        {
            var stats = new List<NumberStatDto>();
            var totalPeriods = draws.Count;

            foreach (var num in group.Numbers)
            {
                var stat = new NumberStatDto { Number = num };

                // 找出该号码出现的所有期索引
                var appearIndices = new List<int>();
                for (int i = 0; i < draws.Count; i++)
                {
                    if (IsHit(draws[i], group, num))
                        appearIndices.Add(i);
                }

                stat.Count = appearIndices.Count;

                if (appearIndices.Count == 0)
                {
                    // 从未出现：当前遗漏 = 总期数
                    stat.CurrentMiss = totalPeriods;
                    stat.AvgMiss = totalPeriods;
                    stat.MaxMiss = totalPeriods;
                    stat.MaxConsecutive = 0;
                }
                else
                {
                    // 当前遗漏 = 总期数 - 最后一次出现的索引 - 1
                    stat.CurrentMiss = totalPeriods - 1 - appearIndices.Last();

                    // 计算遗漏序列
                    var misses = new List<int>();
                    for (int i = 1; i < appearIndices.Count; i++)
                    {
                        misses.Add(appearIndices[i] - appearIndices[i - 1] - 1);
                    }
                    // 首次出现前的遗漏
                    misses.Insert(0, appearIndices[0]);

                    stat.MaxMiss = misses.Max();
                    stat.AvgMiss = misses.Count > 0 ? Math.Round(misses.Average(), 2) : 0;

                    // 计算最大连出
                    stat.MaxConsecutive = CalculateMaxConsecutive(draws, group, num);
                }

                stats.Add(stat);
            }

            return stats;
        }

        /// <summary>计算某号码的最大连出次数</summary>
        private static int CalculateMaxConsecutive(List<LotteryDrawDto> draws, LotteryZoneDto group, int num)
        {
            int maxConsec = 0;
            int current = 0;

            foreach (var draw in draws)
            {
                if (IsHit(draw, group, num))
                {
                    current++;
                    if (current > maxConsec) maxConsec = current;
                }
                else
                {
                    current = 0;
                }
            }

            return maxConsec;
        }

        /// <summary>查询指定开奖期的官网通告数据：开奖号码 + 全国中奖明细 + 销量/奖池</summary>
        public LotteryDrawNoticeDto GetDrawNotice(string type, string issue)
        {
            var t = LotteryTypes.Normalize(type);
            var draw = _fsql.Select<LotteryDrawEntity>()
                .Where(d => d.LotteryType == t && d.IssueNumber == issue).First()
                ?? throw new BizException($"未找到{LotteryTypes.GetName(t)}第 {issue} 期开奖记录", StatusCodes.Status404NotFound);

            var positional = LotteryTypes.IsPositional(t);
            return new LotteryDrawNoticeDto
            {
                IssueNumber = draw.IssueNumber,
                DrawDate = draw.DrawDate,
                Front = ParseNumbers(draw.FrontNumbers, positional),
                Back = ParseNumbers(draw.BackNumbers, positional),
                Grades = LotteryPrizeHelper.ParsePrizeDetail(draw.PrizeDetail),
                SalesAmount = draw.SalesAmount,
                PoolBalance = draw.PoolBalance,
                PrizeArea = draw.PrizeArea,
                NoticeUrl = draw.NoticeUrl,
            };
        }

        /// <summary>号码数组 → 逗号分隔字符串：位置型按位原样存储，池选型升序补零</summary>
        private static string FormatNumbers(int[] numbers, bool positional)
        {
            if (numbers == null || numbers.Length == 0) return string.Empty;
            IEnumerable<int> seq = positional ? numbers : numbers.OrderBy(n => n);
            return string.Join(",", seq.Select(n => positional ? n.ToString() : n.ToString("D2")));
        }

        /// <summary>逗号分隔字符串 → 号码数组：位置型允许 0，池选型过滤非法值</summary>
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

using ConvenientSystem.Shared.Common;
using ConvenientSystem.Shared.Common.Exceptions;
using ConvenientSystem.Shared.Common.Security;
using ConvenientSystem.Shared.Entity.Common;
using ConvenientSystem.Shared.Model.Common;
using Microsoft.AspNetCore.Http;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// 彩票选号记录服务实现：每注号码独立存储，按用户、按彩种隔离。
    /// </summary>
    public class LotteryService : ILotteryService
    {
        private readonly IFreeSql _fsql;
        private readonly ICurrentUser _currentUser;

        public LotteryService(
            [FromKeyedServices("ConvenientSystemDb")] IFreeSql fsql,
            ICurrentUser currentUser)
        {
            _fsql = fsql;
            _currentUser = currentUser;
        }

        private Guid RequireUserId()
            => _currentUser.UserId ?? throw new UnauthorizedAccessException("未登录");

        public PagedResult<LotteryBetDto> GetRecords(string type, string? date, int page, int size, string? sortField = null, string? sortOrder = null)
        {
            var userId = RequireUserId();
            var t = LotteryTypes.Normalize(type);
            var query = _fsql.Select<LotteryRecordEntity>()
                .Where(r => r.UserId == userId && r.LotteryType == t);

            if (!string.IsNullOrWhiteSpace(date) && date.Length >= 10)
            {
                var day = date[..10];
                var start = DateTime.Parse(day);
                var end = start.AddDays(1);
                // 按开奖日期筛选（保存时已默认存入下一期开奖日）
                query = query.Where(r => r.DrawDate >= start && r.DrawDate < end);
            }

            var total = query.Count();
            var sortedQuery = string.IsNullOrWhiteSpace(sortField) ? query.OrderByDescending(r => r.CreatedAt) : query.OrderByDynamic(sortField, sortOrder);
            var list = sortedQuery
                .Skip((page - 1) * size).Take(size)
                .ToList()
                .Select(MapToDto)
                .ToList();

            return new PagedResult<LotteryBetDto> { Total = total, List = list };
        }

        public List<LotteryBetDto> SaveBets(string type, List<LotteryBetItem> bets)
        {
            var userId = RequireUserId();
            if (bets == null || bets.Count == 0)
                return new List<LotteryBetDto>();

            var t = LotteryTypes.Normalize(type);
            var positional = LotteryTypes.IsPositional(t);

            // 保存默认归属下一期：期号 = 最新开奖期号 + 1，开奖日 = 最新开奖日之后的最近开奖日（当天未开奖则含当天）
            var (nextIssue, nextDrawDate) = GetNextIssueAndDate(t);

            var entities = bets.Select(b => new LotteryRecordEntity
            {
                UserId = userId,
                LotteryType = t,
                // 位置型按位存储（不排序、允许 0）；池选型升序补零
                FrontNumbers = FormatNumbers(b.Front, positional),
                BackNumbers = FormatNumbers(b.Back, positional),
                IssueNumber = nextIssue,
                DrawDate = nextDrawDate,
            }).ToList();

            var ids = _fsql.Insert(entities).ExecuteIdentity();
            // ExecuteIdentity 对批量插入返回最后一条 Id，需重新查询获取完整记录
            var saved = _fsql.Select<LotteryRecordEntity>()
                .Where(r => r.UserId == userId && r.LotteryType == t)
                .OrderByDescending(r => r.Id)
                .Take(entities.Count)
                .ToList()
                .Select(MapToDto)
                .ToList();

            return saved;
        }

        public bool DeleteRecord(int id)
        {
            var userId = RequireUserId();
            var affected = _fsql.Delete<LotteryRecordEntity>()
                .Where(r => r.Id == id && r.UserId == userId)
                .ExecuteAffrows();
            return affected > 0;
        }

        public int DeleteByDate(string type, string date)
        {
            var userId = RequireUserId();
            if (string.IsNullOrWhiteSpace(date) || date.Length < 10) return 0;

            var t = LotteryTypes.Normalize(type);
            var start = DateTime.Parse(date[..10]);
            var end = start.AddDays(1);
            // 与筛选口径一致：按开奖日期删除
            return _fsql.Delete<LotteryRecordEntity>()
                .Where(r => r.UserId == userId && r.LotteryType == t && r.DrawDate >= start && r.DrawDate < end)
                .ExecuteAffrows();
        }

        /// <summary>下一期期号与开奖日：期号为最新开奖期号 + 1（无开奖数据时为 null）；
        /// 开奖日为最新开奖日之后的最近开奖日，最新开奖早于今天时从今天起算（当天未开奖则含当天）</summary>
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

        public List<LotteryHomeResultDto> GetHomeResults()
        {
            var userId = RequireUserId();
            var results = new List<LotteryHomeResultDto>();

            foreach (var type in LotteryTypes.All)
            {
                var positional = LotteryTypes.IsPositional(type);
                var item = new LotteryHomeResultDto
                {
                    Type = type,
                    Name = LotteryTypes.GetName(type),
                    Positional = positional,
                };

                // 最新一期开奖（按期号倒序；无数据时仅返回彩种基本信息）
                var draw = _fsql.Select<LotteryDrawEntity>()
                    .Where(d => d.LotteryType == type)
                    .OrderByDescending(d => d.IssueNumber)
                    .First();
                if (draw == null)
                {
                    results.Add(item);
                    continue;
                }

                item.IssueNumber = draw.IssueNumber;
                item.DrawDate = draw.DrawDate;
                item.Front = ParseNumbers(draw.FrontNumbers, positional);
                item.Back = ParseNumbers(draw.BackNumbers, positional);

                // 本期本人选号记录逐注判奖（与验奖接口、开奖通知 Job 同一口径）：
                // 有期号的记录严格按期号归属，其次按记录自带开奖日，早期无这两个字段的记录才回退选号当天。
                // 不能只用 CreatedAt：提前几天选的号会落在开奖日之外，首页会漏掉这些注
                var day = draw.DrawDate.Date;
                var next = day.AddDays(1);
                var issue = draw.IssueNumber;
                var records = _fsql.Select<LotteryRecordEntity>()
                    .Where(r => r.UserId == userId && r.LotteryType == type
                        && (r.IssueNumber == issue
                            || ((r.IssueNumber == null || r.IssueNumber == "")
                                && ((r.DrawDate >= day && r.DrawDate < next)
                                    || (r.DrawDate == null && r.CreatedAt >= day && r.CreatedAt < next)))))
                    .ToList();

                // 当期官方奖级明细：判奖时用于确认双色球福运奖当期是否执行
                var grades = LotteryPrizeHelper.ParsePrizeDetail(draw.PrizeDetail);
                // 判奖规则：库内生效版本（自官网条文抓取），无则内置兜底规则
                var rules = LotteryRuleCache.Get(_fsql, type);

                foreach (var r in records)
                {
                    var front = ParseNumbers(r.FrontNumbers, positional);
                    var back = ParseNumbers(r.BackNumbers, positional);
                    var prize = LotteryPrizeHelper.CalcPrize(type, positional, front, back, item.Front, item.Back, grades, rules);
                    var isWin = prize != LotteryPrizeHelper.NoPrize;
                    if (isWin) item.WinCount++;
                    item.Bets.Add(new LotteryHomeBetResultDto
                    {
                        Pick = LotteryPrizeHelper.FormatResult(positional, front, back),
                        Prize = prize,
                        IsWin = isWin,
                    });
                }
                item.BetCount = records.Count;

                results.Add(item);
            }

            return results;
        }

        public LotteryVerifyDto VerifyBet(int id)
        {
            var userId = RequireUserId();
            var record = _fsql.Select<LotteryRecordEntity>()
                .Where(r => r.Id == id && r.UserId == userId).First()
                ?? throw new BizException("选号记录不存在或已删除", StatusCodes.Status404NotFound);

            var t = record.LotteryType;
            var positional = LotteryTypes.IsPositional(t);
            var front = ParseNumbers(record.FrontNumbers, positional);
            var back = ParseNumbers(record.BackNumbers, positional);

            var dto = new LotteryVerifyDto
            {
                RecordId = id,
                Pick = LotteryPrizeHelper.FormatResult(positional, front, back),
            };

            // 优先按记录自带的期号匹配（保存选号时已归属到目标期）；
            // 早期历史记录无期号/开奖日，才回退按选号当日匹配。
            // 不能直接用选号时间当开奖日：提前几天选的号会因此误报“未开奖”
            LotteryDrawEntity? draw = null;
            if (!string.IsNullOrWhiteSpace(record.IssueNumber))
                draw = _fsql.Select<LotteryDrawEntity>()
                    .Where(d => d.LotteryType == t && d.IssueNumber == record.IssueNumber)
                    .First();
            if (draw == null)
            {
                var day = (record.DrawDate ?? record.CreatedAt).Date;
                draw = _fsql.Select<LotteryDrawEntity>()
                    .Where(d => d.LotteryType == t && d.DrawDate >= day && d.DrawDate < day.AddDays(1))
                    .OrderByDescending(d => d.IssueNumber)
                    .First();
            }
            if (draw == null)
            {
                dto.Prize = "未开奖";
                return dto;
            }

            dto.HasDraw = true;
            dto.IssueNumber = draw.IssueNumber;
            dto.DrawDate = draw.DrawDate;
            dto.DrawFront = ParseNumbers(draw.FrontNumbers, positional);
            dto.DrawBack = ParseNumbers(draw.BackNumbers, positional);
            dto.SalesAmount = draw.SalesAmount;
            dto.PoolBalance = draw.PoolBalance;
            dto.PrizeArea = draw.PrizeArea;
            dto.NoticeUrl = draw.NoticeUrl;
            dto.Grades = LotteryPrizeHelper.ParsePrizeDetail(draw.PrizeDetail);
            var rules = LotteryRuleCache.Get(_fsql, t);

            var hit = LotteryPrizeHelper.CalcHit(t, positional, front, back, dto.DrawFront, dto.DrawBack, dto.Grades, rules);
            dto.Hit = hit;
            dto.Prize = hit.Prize;
            dto.IsWin = hit.IsWin;
            if (dto.IsWin)
            {
                var m = MatchMoney(dto.Prize, t, dto.Grades, rules);
                dto.Money = m.Money;
                dto.Tax = m.Tax;
                dto.MoneyAfterTax = m.AfterTax;
                dto.GradeCount = m.GradeCount;
                dto.MatchedGrade = m.MatchedGrade;
            }
            return dto;
        }

        public LotteryIssueVerifyDto VerifyIssue(string type, DateTime? date)
        {
            var userId = RequireUserId();
            var t = LotteryTypes.Normalize(type);
            var positional = LotteryTypes.IsPositional(t);
            var dto = new LotteryIssueVerifyDto();

            // 指定开奖日则取当天那一期（同日多期取期号最大者），否则取该彩种最新一期
            var q = _fsql.Select<LotteryDrawEntity>().Where(d => d.LotteryType == t);
            if (date.HasValue)
            {
                var d0 = date.Value.Date;
                var d1 = d0.AddDays(1);
                q = q.Where(d => d.DrawDate >= d0 && d.DrawDate < d1);
            }
            var draw = q.OrderByDescending(d => d.IssueNumber).First();
            if (draw == null) return dto; // HasDraw=false：该日无开奖或历史未采集

            dto.HasDraw = true;
            dto.IssueNumber = draw.IssueNumber;
            dto.DrawDate = draw.DrawDate;
            dto.DrawFront = ParseNumbers(draw.FrontNumbers, positional);
            dto.DrawBack = ParseNumbers(draw.BackNumbers, positional);
            dto.SalesAmount = draw.SalesAmount;
            dto.PoolBalance = draw.PoolBalance;
            dto.PrizeArea = draw.PrizeArea;
            dto.NoticeUrl = draw.NoticeUrl;
            dto.Grades = LotteryPrizeHelper.ParsePrizeDetail(draw.PrizeDetail);
            var rules = LotteryRuleCache.Get(_fsql, t);

            // 归属该期的本人选号（与 GetHomeResults、VerifyBet 同一三级口径：
            // 期号优先 → 记录自带开奖日 → 早期记录回退选号当天）
            var day = draw.DrawDate.Date;
            var next = day.AddDays(1);
            var issue = draw.IssueNumber;
            var records = _fsql.Select<LotteryRecordEntity>()
                .Where(r => r.UserId == userId && r.LotteryType == t
                    && (r.IssueNumber == issue
                        || ((r.IssueNumber == null || r.IssueNumber == "")
                            && ((r.DrawDate >= day && r.DrawDate < next)
                                || (r.DrawDate == null && r.CreatedAt >= day && r.CreatedAt < next)))))
                .OrderBy(r => r.CreatedAt)
                .ToList();

            foreach (var r in records)
            {
                var front = ParseNumbers(r.FrontNumbers, positional);
                var back = ParseNumbers(r.BackNumbers, positional);
                var hit = LotteryPrizeHelper.CalcHit(t, positional, front, back, dto.DrawFront, dto.DrawBack, dto.Grades, rules);
                var bet = new LotteryIssueBetDto
                {
                    RecordId = r.Id,
                    Front = front,
                    Back = back,
                    Pick = LotteryPrizeHelper.FormatResult(positional, front, back),
                    CreatedAt = r.CreatedAt,
                    Prize = hit.Prize,
                    IsWin = hit.IsWin,
                    Hit = hit,
                };
                if (hit.IsWin)
                {
                    dto.WinCount++;
                    var m = MatchMoney(hit.Prize, t, dto.Grades, rules);
                    bet.Money = m.Money;
                    bet.Tax = m.Tax;
                    bet.MoneyAfterTax = m.AfterTax;
                    bet.GradeCount = m.GradeCount;
                    bet.MatchedGrade = m.MatchedGrade;
                    // 合计只累加官方奖金可得的注，避免把缺数据的注当成 0 元拉低总额
                    if (m.Money.HasValue)
                    {
                        dto.MoneyKnown = true;
                        dto.TotalMoney += m.Money.Value;
                        dto.TotalTax += m.Tax ?? 0m;
                        dto.TotalMoneyAfterTax += m.AfterTax ?? 0m;
                    }
                }
                dto.Bets.Add(bet);
            }
            dto.BetCount = records.Count;
            return dto;
        }

        /// <summary>
        /// 按奖级取本注奖金与个税：验单注与验整期共用，避免两处口径漂移。
        /// 奖金优先取官方当期明细（浮动奖级只有官方数据准），官方缺该奖级时回落规则表的单注固定奖金；
        /// 两者都没有时金额全为 null（不以 0 元冒充），但注数与奖级名仍尽量回传。
        /// </summary>
        private static (decimal? Money, decimal? Tax, decimal? AfterTax, long? GradeCount, string? MatchedGrade)
            MatchMoney(string prize, string type, List<LotteryPrizeGradeDto> grades, LotteryRuleDto rules)
        {
            var row = LotteryPrizeHelper.MatchGrade(prize, type, grades, rules);
            var rule = LotteryPrizeHelper.FindRule(rules, prize);
            var gradeName = row?.Grade ?? rule?.Grade;
            if ((row?.Money ?? rule?.FixedMoney) is not decimal money)
                return (null, null, null, row?.Count, gradeName);
            var tax = LotteryPrizeHelper.CalcTax(money);
            return (money, tax, money - tax, row?.Count, gradeName);
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

        private static LotteryBetDto MapToDto(LotteryRecordEntity r)
        {
            var positional = LotteryTypes.IsPositional(r.LotteryType);
            return new LotteryBetDto
            {
                Id = r.Id,
                Front = ParseNumbers(r.FrontNumbers, positional),
                Back = ParseNumbers(r.BackNumbers, positional),
                IssueNumber = r.IssueNumber,
                DrawDate = r.DrawDate,
                CreatedAt = r.CreatedAt,
            };
        }
    }
}

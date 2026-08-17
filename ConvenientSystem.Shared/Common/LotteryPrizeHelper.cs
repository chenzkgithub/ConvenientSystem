using ConvenientSystem.Shared.Model.Common;
using System.Text.Json;

namespace ConvenientSystem.Shared.Common
{
    /// <summary>
    /// 彩票中奖计算公共助手：开奖号码格式化与逐注奖级判定。
    /// 供开奖结果邮件通知 Job 与首页中奖结果接口共用，保证口径一致。
    /// </summary>
    public static class LotteryPrizeHelper
    {
        /// <summary>未中奖固定文案（各处按此值判断是否中奖）</summary>
        public const string NoPrize = "未中奖";

        /// <summary>
        /// 双色球福运奖奖级名（3 个红球中 5 元）。
        /// 仅在“执行特别规定期间”（奖池达 15 亿元及以上）设立，不是每期都有，
        /// 因此必须按当期官方奖级明细判定是否执行，不能无条件当中奖。
        /// </summary>
        public const string FuyunGrade = "福运奖";

        /// <summary>格式化号码文本：位置型空格分隔，池选型补零、前后区用 + 分隔</summary>
        public static string FormatResult(bool positional, int[] front, int[] back)
        {
            var f = string.Join(" ", front.Select(n => positional ? n.ToString() : n.ToString("D2")));
            if (back.Length == 0) return f;
            return $"{f} + {string.Join(" ", back.Select(n => n.ToString("D2")))}";
        }

        /// <summary>逗号分隔号码字符串 → 整型数组（过滤非法值）</summary>
        public static int[] ParseNumbers(string? raw)
            => (raw ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.TryParse(s.Trim(), out var n) ? n : -1)
                .Where(n => n >= 0)
                .ToArray();

        /// <summary>官方中奖明细 JSON（LotteryDraw.PrizeDetail）→ 奖级列表；历史期未采集或解析失败时返回空</summary>
        public static List<LotteryPrizeGradeDto> ParsePrizeDetail(string? prizeDetail)
        {
            if (string.IsNullOrWhiteSpace(prizeDetail)) return new List<LotteryPrizeGradeDto>();
            try
            {
                var rows = JsonSerializer.Deserialize<List<PrizeDetailRow>>(prizeDetail,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return rows?.Select(r => new LotteryPrizeGradeDto
                {
                    Grade = r.Grade ?? string.Empty,
                    Count = r.Count,
                    Money = r.Money,
                }).ToList() ?? new List<LotteryPrizeGradeDto>();
            }
            catch
            {
                return new List<LotteryPrizeGradeDto>();
            }
        }

        /// <summary>中奖明细 JSON 内部行结构</summary>
        private sealed class PrizeDetailRow
        {
            public string? Grade { get; set; }
            public long? Count { get; set; }
            public decimal? Money { get; set; }
        }

        /// <summary>偶然所得个税起征点：单注奖金超过该额度才征税（含税起征额本身不征）</summary>
        public const decimal TaxThreshold = 10000m;

        /// <summary>偶然所得个税税率：超过起征点按全额征收</summary>
        public const decimal TaxRate = 0.20m;

        /// <summary>
        /// 单注奖金应缴个人所得税（偶然所得）：不超过 1 万元免征，
        /// 超过 1 万元按中奖全额（而非超出部分）征收 20%。
        /// </summary>
        public static decimal CalcTax(decimal money)
            => money > TaxThreshold ? Math.Round(money * TaxRate, 2) : 0m;

        /// <summary>
        /// 由官方中奖明细取本注所中奖级行（含单注奖金与全国注数）。
        /// 本系统奖级名与官方奖级名不完全一致（如“直选中奖”对应福彩3D 的“单选”），需先映射：
        /// 传入 rules 时按规则表的 SystemGrade → Grade 映射，否则用内置映射。
        /// 验奖接口与开奖邮件共用此方法，保证奖金口径一致。
        /// </summary>
        public static LotteryPrizeGradeDto? MatchGrade(string prize, string type, List<LotteryPrizeGradeDto> grades,
            LotteryRuleDto? rules = null)
        {
            if (grades.Count == 0) return null;
            var target = FindRule(rules, prize)?.Grade ?? prize switch
            {
                "直选中奖" => type == LotteryTypes.FC3D ? "单选" : "一等奖",
                "组三中奖" => "组选3",
                "组六中奖" => "组选6",
                "组选中奖" => null, // 排列五无官方组选奖级
                _ => prize,
            };
            if (target == null) return null;
            var row = grades.FirstOrDefault(g => SameGrade(g.Grade, target));
            // 单奖级彩种（排列五）未命中时取唯一奖级
            if (row == null && grades.Count == 1) row = grades[0];
            return row;
        }

        /// <summary>按本系统奖级名取规则行（未传规则或未命中时返回 null）</summary>
        public static LotteryGradeRuleDto? FindRule(LotteryRuleDto? rules, string prize)
            => rules?.Grades.FirstOrDefault(g => g.SystemGrade == prize || SameGrade(g.Grade, prize));

        /// <summary>
        /// 奖级的单注固定奖金（来自官网“单注奖金固定为…元”条文）。
        /// 仅当官方当期明细拿不到奖金时作回落；浮动奖级与分档固定奖（如大乐透三~七等奖按奖池 8 亿分两档）返回 null。
        /// </summary>
        public static decimal? FixedMoney(LotteryRuleDto? rules, string prize)
            => FindRule(rules, prize)?.FixedMoney;

        /// <summary>
        /// 奖级名等价比较：忽略“第”前缀。
        /// 福彩官网明细入库时拼的是“第一等奖”，体彩用官网原文“一等奖”，而判奖返回的一律不带“第”，
        /// 直接相等比较会让双色球每一注都取不到官方奖金（前端显示成“官方未提供奖金数据”）。
        /// 在此处兼容而不改写入格式：历史期的明细回填只补空值，改写入端也覆盖不到已入库的旧数据。
        /// </summary>
        private static bool SameGrade(string? gradeInDetail, string target)
            => TrimGradePrefix(gradeInDetail) == TrimGradePrefix(target);

        /// <summary>去掉奖级名的“第”前缀（“单选”“组选3”等非等奖名不受影响）</summary>
        private static string TrimGradePrefix(string? grade)
            => (grade ?? string.Empty).Trim().TrimStart('第');

        /// <summary>
        /// 计算一注的中奖结果：
        /// - 大乐透/双色球：前区与后区命中数按现行奖级规则判定
        /// - 排列五：按位全中=直选中奖；数字集合一致=组选
        /// - 福彩3D：按位全中=直选；数字集合一致=组三/组六
        /// grades 为当期官方奖级明细，仅用于判定双色球福运奖当期是否执行，缺省时视为未执行。
        /// rules 为规则表生效版本，缺省时用内置兜底规则（二者判奖结果等价）。
        /// </summary>
        public static string CalcPrize(string type, bool positional, int[] front, int[] back,
            int[] drawFront, int[] drawBack, List<LotteryPrizeGradeDto>? grades = null, LotteryRuleDto? rules = null)
            => CalcHit(type, positional, front, back, drawFront, drawBack, grades, rules).Prize;

        /// <summary>
        /// 计算一注的中奖明细：在奖级之外给出命中号码分布与文字说明，
        /// 供验奖弹窗与开奖邮件展示“哪些号码对了”。奖级判定与 CalcPrize 同一口径。
        /// </summary>
        public static LotteryHitResultDto CalcHit(string type, bool positional, int[] front, int[] back,
            int[] drawFront, int[] drawBack, List<LotteryPrizeGradeDto>? grades = null, LotteryRuleDto? rules = null)
        {
            var r = new LotteryHitResultDto();

            if (positional)
            {
                // 位置型按位比对：命中与否取决于同一位上的数字是否相同
                r.PositionHits = front.Select((n, i) => i < drawFront.Length && n == drawFront[i]).ToArray();
                var hit = r.PositionHits.Count(b => b);
                r.FrontHitCount = hit;
                var sameSet = front.Length == drawFront.Length
                    && front.OrderBy(n => n).SequenceEqual(drawFront.OrderBy(n => n));

                if (front.Length == drawFront.Length && front.SequenceEqual(drawFront))
                {
                    r.Prize = "直选中奖";
                    r.HitSummary = $"{front.Length} 位数字全中且顺序一致";
                }
                else if (sameSet)
                {
                    r.Prize = type == LotteryTypes.FC3D
                        ? (front.Distinct().Count() == 2 ? "组三中奖" : "组六中奖")
                        : "组选中奖";
                    r.HitSummary = $"数字全中但顺序不一致（按位命中 {hit} 位）";
                }
                else
                {
                    r.Prize = NoPrize;
                    r.HitSummary = hit > 0 ? $"仅按位命中 {hit} 位" : "无命中";
                }

                r.IsWin = r.Prize != NoPrize;
                return r;
            }

            r.FrontHits = front.Where(n => drawFront.Contains(n)).ToArray();
            r.BackHits = back.Where(n => drawBack.Contains(n)).ToArray();
            var frontHit = r.FrontHits.Length;
            var backHit = r.BackHits.Length;
            r.FrontHitCount = frontHit;
            r.BackHitCount = backHit;

            var zone = type == LotteryTypes.SSQ ? ("红球", "蓝球") : ("前区", "后区");
            r.HitSummary = $"{zone.Item1}命中 {frontHit} 个，{zone.Item2}命中 {backHit} 个";

            r.Prize = MatchPrizeByRules(rules ?? LotteryRuleDefaults.Get(type), frontHit, backHit, grades);
            r.IsWin = r.Prize != NoPrize;
            return r;
        }

        /// <summary>
        /// 按规则表判定池选型彩种（大乐透/双色球）的奖级。
        /// 官方条文的“任意 N 个…相同”是“至少”语义，因此逐条件比 frontHit >= Front && backHit >= Back，
        /// 并按奖级从高到低（Order 升序）取首个满足者——对应官方“只兜付所中得最高奖级，不能兼中兼得”。
        /// </summary>
        public static string MatchPrizeByRules(LotteryRuleDto rules, int frontHit, int backHit,
            List<LotteryPrizeGradeDto>? grades = null)
        {
            foreach (var g in rules.Grades.OrderBy(g => g.Order))
            {
                if (g.Match != LotteryMatchKind.Hit || g.Conds.Count == 0) continue;
                if (!g.Conds.Any(c => frontHit >= c.Front && backHit >= c.Back)) continue;
                // 附条件奖级（双色球福运奖）非常设，只有当期官方明细里出现该奖级时才算中奖，
                // 否则未执行期间的 3+0 会被误报为中奖且取不到奖金
                if (g.Conditional && !HasGrade(grades, g.Grade)) continue;
                return g.SystemGrade;
            }
            return NoPrize;
        }

        /// <summary>
        /// 当期官方奖级明细里是否有指定奖级（附条件奖级是否执行的依据）。
        /// 福彩接口每期固定返回福运奖槽位，未执行时注数与奖金为空、入库时已被过滤，
        /// 所以明细里有这一行就意味着当期确实在执行特别规定。
        /// </summary>
        private static bool HasGrade(List<LotteryPrizeGradeDto>? grades, string grade)
            => grades != null && grades.Any(g => SameGrade(g.Grade, grade));
    }
}

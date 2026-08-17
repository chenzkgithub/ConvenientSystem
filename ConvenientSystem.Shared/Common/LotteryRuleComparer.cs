using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Shared.Common
{
    /// <summary>
    /// 两套玩法规则的判奖等价性比较：抓到新条文时用来判断"是否真的改变了判奖结果"。
    /// 池选型按前后区命中数枚举全部组合逐一比对，位置型比对奖级构成与固定奖金，
    /// 只有条文变动确实改变判奖时才需要人工审核，纯排版改动不打扰用户。
    /// </summary>
    public static class LotteryRuleComparer
    {
        /// <summary>两套规则的判奖结果是否完全一致</summary>
        public static bool Equivalent(string type, LotteryRuleDto a, LotteryRuleDto b)
            => Diff(type, a, b).Count == 0;

        /// <summary>
        /// 两套规则的差异清单（每项一行中文说明，空列表表示判奖完全一致）。
        /// 池选型：逐「前区命中+后区命中」组合比对奖级，附条件奖级分"当期执行/未执行"两种情形各比一遍；
        /// 位置型：比对奖级名、判定方式与单注固定奖金。
        /// </summary>
        public static List<string> Diff(string type, LotteryRuleDto a, LotteryRuleDto b)
        {
            type = LotteryTypes.Normalize(type);
            var diff = new List<string>();

            if (LotteryTypes.IsPositional(type))
            {
                var names = a.Grades.Select(g => g.Grade).Union(b.Grades.Select(g => g.Grade));
                foreach (var name in names)
                {
                    var ga = a.Grades.FirstOrDefault(g => g.Grade == name);
                    var gb = b.Grades.FirstOrDefault(g => g.Grade == name);
                    if (ga == null) { diff.Add($"新增奖级：{name}"); continue; }
                    if (gb == null) { diff.Add($"取消奖级：{name}"); continue; }
                    if (ga.SystemGrade != gb.SystemGrade || ga.Match != gb.Match)
                        diff.Add($"{name}：判定方式 {ga.Match} → {gb.Match}");
                    if (ga.FixedMoney != gb.FixedMoney)
                        diff.Add($"{name}：单注固定奖金 {Money(ga.FixedMoney)} → {Money(gb.FixedMoney)}");
                }
                return diff;
            }

            var (frontPick, backPick) = GetPicks(type);
            // 附条件奖级（如福运奖）在"当期执行"与"未执行"下判奖不同，两种情形都要比
            var conditional = a.Grades.Concat(b.Grades).Where(g => g.Conditional)
                .Select(g => new LotteryPrizeGradeDto { Grade = g.Grade }).ToList();
            var cases = conditional.Count > 0
                ? new[] { (Tag: "（当期执行附条件奖级）", Detail: conditional), (Tag: string.Empty, Detail: new List<LotteryPrizeGradeDto>()) }
                : [(Tag: string.Empty, Detail: new List<LotteryPrizeGradeDto>())];

            foreach (var (tag, detail) in cases)
                for (var f = 0; f <= frontPick; f++)
                    for (var k = 0; k <= backPick; k++)
                    {
                        var pa = LotteryPrizeHelper.MatchPrizeByRules(a, f, k, detail);
                        var pb = LotteryPrizeHelper.MatchPrizeByRules(b, f, k, detail);
                        if (pa != pb) diff.Add($"{f}+{k}{tag}：{pa} → {pb}");
                    }

            // 固定奖金变化不改变奖级，但直接影响奖金展示，同样要提示
            foreach (var ga in a.Grades)
            {
                var gb = b.Grades.FirstOrDefault(g => g.Grade == ga.Grade);
                if (gb != null && ga.FixedMoney != gb.FixedMoney)
                    diff.Add($"{ga.Grade}：单注固定奖金 {Money(ga.FixedMoney)} → {Money(gb.FixedMoney)}");
            }
            return diff;
        }

        private static string Money(decimal? money) => money.HasValue ? $"{money.Value:0.##} 元" : "无固定值";

        /// <summary>彩种前后区选号个数（命中数枚举上限）</summary>
        private static (int Front, int Back) GetPicks(string type)
        {
            var zones = LotteryTypes.GetPickZones(type);
            return (zones.Where(z => z.Source == "front").Sum(z => z.Pick),
                    zones.Where(z => z.Source == "back").Sum(z => z.Pick));
        }
    }
}

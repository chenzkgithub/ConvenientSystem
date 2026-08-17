using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Shared.Common
{
    /// <summary>
    /// 内置兜底玩法规则：与 LotteryPrizeHelper 原有硬编码判奖表完全等价（已逐组合验算）。
    /// 仅在库内该彩种暂无生效版本、或规则 JSON 解析失败时使用，保证抓取失败也不影响判奖。
    /// 条文文字取自官网现行规则，奖金一律以"单注固定奖金"条文为准；
    /// 大乐透三~七等奖固定奖金按奖池是否达 8 亿元分两档，故不填 FixedMoney，只保留原文。
    /// </summary>
    public static class LotteryRuleDefaults
    {
        /// <summary>取指定彩种的内置兜底规则（IsDefault=true）</summary>
        public static LotteryRuleDto Get(string type) => new()
        {
            LotteryType = LotteryTypes.Normalize(type),
            IsDefault = true,
            Grades = LotteryTypes.Normalize(type) switch
            {
                LotteryTypes.SSQ => Ssq(),
                LotteryTypes.PL5 => Pl5(),
                LotteryTypes.FC3D => Fc3d(),
                _ => Dlt(),
            },
        };

        /// <summary>大乐透：前区 5 + 后区 2，七个奖级</summary>
        private static List<LotteryGradeRuleDto> Dlt() =>
        [
            Hit(1, "一等奖", [(5, 2)], null, null,
                "投注号码与当期开奖号码全部相同（顺序不限，下同），即中奖"),
            Hit(2, "二等奖", [(5, 1)], null, null,
                "投注号码与当期开奖号码中的5个前区号码及任意1个后区号码相同，即中奖"),
            Hit(3, "三等奖", [(5, 0), (4, 2)], null, "5000元（奖池低于8亿元）/ 6666元（奖池8亿元及以上）",
                "投注号码与当期开奖号码中的5个前区号码相同，或者任意4个前区号码及2个后区号码相同，即中奖"),
            Hit(4, "四等奖", [(4, 1)], null, "300元（奖池低于8亿元）/ 380元（奖池8亿元及以上）",
                "投注号码与当期开奖号码中的任意4个前区号码及任意1个后区号码相同，即中奖"),
            Hit(5, "五等奖", [(4, 0), (3, 2)], null, "150元（奖池低于8亿元）/ 200元（奖池8亿元及以上）",
                "投注号码与当期开奖号码中的任意4个前区号码相同，或者任意3个前区号码及2个后区号码相同，即中奖"),
            Hit(6, "六等奖", [(3, 1), (2, 2)], null, "15元（奖池低于8亿元）/ 18元（奖池8亿元及以上）",
                "投注号码与当期开奖号码中的任意3个前区号码及任意1个后区号码相同，或者任意2个前区号码及2个后区号码相同，即中奖"),
            Hit(7, "七等奖", [(3, 0), (2, 1), (1, 2), (0, 2)], null, "5元（奖池低于8亿元）/ 7元（奖池8亿元及以上）",
                "投注号码与当期开奖号码中的任意3个前区号码相同，或者任意2个前区号码及任意1个后区号码相同，"
                + "或者任意1个前区号码及2个后区号码相同，或者2个后区号码相同，即中奖"),
        ];

        /// <summary>双色球：红球 6 + 蓝球 1，六个奖级 + 福运奖（附条件，须排在六等奖之后）</summary>
        private static List<LotteryGradeRuleDto> Ssq() =>
        [
            Hit(1, "一等奖", [(6, 1)], null, null,
                "投注号码与当期开奖号码全部相同，即中奖"),
            Hit(2, "二等奖", [(6, 0)], null, null,
                "投注号码与当期开奖号码中的6个红色球号码相同，即中奖"),
            Hit(3, "三等奖", [(5, 1)], 3000m, "单注奖金固定为3000元",
                "投注号码与当期开奖号码中的任意5个红色球号码和1个蓝色球号码相同，即中奖"),
            Hit(4, "四等奖", [(5, 0), (4, 1)], 200m, "单注奖金固定为200元",
                "投注号码与当期开奖号码中的任意5个红色球号码相同，或与任意4个红色球号码和1个蓝色球号码相同，即中奖"),
            Hit(5, "五等奖", [(4, 0), (3, 1)], 10m, "单注奖金固定为10元",
                "投注号码与当期开奖号码中的任意4个红色球号码相同，或与任意3个红色球号码和1个蓝色球号码相同，即中奖"),
            Hit(6, "六等奖", [(0, 1)], 5m, "单注奖金固定为5元",
                "投注号码与当期开奖号码中的1个蓝色球号码相同，即中奖"),
            Hit(7, LotteryPrizeHelper.FuyunGrade, [(3, 0)], 5m, "单注奖金固定为5元",
                "在执行特别规定期间，投注号码与当期开奖号码中的任意3个红色球号码相同，即中奖",
                conditional: true),
        ];

        /// <summary>排列五：5 位按位全同，单一奖级</summary>
        private static List<LotteryGradeRuleDto> Pl5() =>
        [
            Pos(1, "一等奖", "直选中奖", LotteryMatchKind.Exact, 100000m, "单注固定奖金100000元",
                "所选号码与中奖号码全部相同且顺序一致"),
        ];

        /// <summary>福彩3D：本系统支持单选与组选3/组选6（官网另有和数、包选等玩法，不在本系统投注范围内）</summary>
        private static List<LotteryGradeRuleDto> Fc3d() =>
        [
            Pos(1, "单选", "直选中奖", LotteryMatchKind.Exact, 1040m, "单注奖金固定为1040元",
                "投注号码与当期开奖号码按位全部相同（百位+十位+个位），即中奖"),
            Pos(2, "组选3", "组三中奖", LotteryMatchKind.Set3, 346m, "单注奖金固定为346元",
                "当期开奖号码的三位数中任意两位数字相同，且投注号码与当期开奖号码相同（顺序不限），即中奖"),
            Pos(3, "组选6", "组六中奖", LotteryMatchKind.Set6, 173m, "单注奖金固定为173元",
                "当期开奖号码的三位数各不相同，且投注号码与当期开奖号码相同（顺序不限），即中奖"),
        ];

        /// <summary>池选型奖级（按命中个数判定，SystemGrade 与官方奖级名一致）</summary>
        private static LotteryGradeRuleDto Hit(int order, string grade, (int Front, int Back)[] conds,
            decimal? fixedMoney, string? moneyText, string conditionText, bool conditional = false) => new()
            {
                Grade = grade,
                SystemGrade = grade,
                Order = order,
                Match = LotteryMatchKind.Hit,
                Conds = conds.Select(c => new LotteryHitCondDto { Front = c.Front, Back = c.Back }).ToList(),
                FixedMoney = fixedMoney,
                MoneyText = moneyText,
                ConditionText = conditionText,
                Conditional = conditional,
            };

        /// <summary>位置型奖级（按位全同/同号集合判定，SystemGrade 为本系统自有奖级名）</summary>
        private static LotteryGradeRuleDto Pos(int order, string grade, string systemGrade, string match,
            decimal? fixedMoney, string? moneyText, string conditionText) => new()
            {
                Grade = grade,
                SystemGrade = systemGrade,
                Order = order,
                Match = match,
                FixedMoney = fixedMoney,
                MoneyText = moneyText,
                ConditionText = conditionText,
            };
    }
}

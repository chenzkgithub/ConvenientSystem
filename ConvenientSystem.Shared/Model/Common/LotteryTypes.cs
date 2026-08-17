namespace ConvenientSystem.Shared.Model.Common
{
    /// <summary>
    /// 彩种定义：大乐透 / 双色球 / 排列五 / 福彩3D 四个彩种的分区规则。
    /// - 池选型（DLT/SSQ）：从前区/后区号码池中选不重复号码，升序存储；
    /// - 位置型（PL5/FC3D）：每个位置各选 1 个 0-9 数字，顺序有意义、数字可重复、允许 0。
    /// </summary>
    public static class LotteryTypes
    {
        /// <summary>大乐透：前区 5 码（01-35）+ 后区 2 码（01-12）</summary>
        public const string DLT = "DLT";
        /// <summary>双色球：红球 6 码（01-33）+ 蓝球 1 码（01-16）</summary>
        public const string SSQ = "SSQ";
        /// <summary>排列五：万/千/百/十/个 5 位，每位 0-9</summary>
        public const string PL5 = "PL5";
        /// <summary>福彩3D：百/十/个 3 位，每位 0-9</summary>
        public const string FC3D = "FC3D";

        /// <summary>全部支持的彩种代码</summary>
        public static readonly string[] All = [DLT, SSQ, PL5, FC3D];

        public static bool IsValid(string? type) => type != null && All.Contains(type);

        /// <summary>非法/未知彩种一律按大乐透处理</summary>
        public static string Normalize(string? type) => IsValid(type) ? type! : DLT;

        /// <summary>位置型彩种：号码按位存储，不排序、允许 0</summary>
        public static bool IsPositional(string type) => type is PL5 or FC3D;

        public static string GetName(string type) => type switch
        {
            SSQ => "双色球",
            PL5 => "排列五",
            FC3D => "福彩3D",
            _ => "大乐透",
        };

        /// <summary>开奖星期：大乐透周一/三/六，双色球周二/四/日，排列五与福彩3D 每天开奖</summary>
        public static DayOfWeek[] GetDrawDays(string type) => Normalize(type) switch
        {
            DLT => [DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Saturday],
            SSQ => [DayOfWeek.Tuesday, DayOfWeek.Thursday, DayOfWeek.Sunday],
            _ => [DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
                  DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday],
        };

        /// <summary>自指定日期（含当天）起的最近一个开奖日</summary>
        public static DateTime NextDrawDate(string type, DateTime from)
        {
            var days = GetDrawDays(type);
            var d = from.Date;
            while (!days.Contains(d.DayOfWeek)) d = d.AddDays(1);
            return d;
        }

        /// <summary>选号分区：每个分区对应一注中的一个选号区域</summary>
        public static List<LotteryZoneDto> GetPickZones(string type) => Normalize(type) switch
        {
            SSQ =>
            [
                Zone("front", "红球区", Range(1, 33), "front", pick: 6),
                Zone("back", "蓝球区", Range(1, 16), "back", pick: 1),
            ],
            PL5 => PositionalZones(["万位", "千位", "百位", "十位", "个位"]),
            FC3D => PositionalZones(["百位", "十位", "个位"]),
            _ =>
            [
                Zone("front", "前区", Range(1, 35), "front", pick: 5),
                Zone("back", "后区", Range(1, 12), "back", pick: 2),
            ],
        };

        /// <summary>走势图分区：比选号分区更细的分组（用于分区着色与逐列统计）</summary>
        public static List<LotteryZoneDto> GetTrendGroups(string type) => Normalize(type) switch
        {
            SSQ =>
            [
                Zone("z1", "红一区", Range(1, 11), "front", pickZoneKey: "front"),
                Zone("z2", "红二区", Range(12, 22), "front", pickZoneKey: "front"),
                Zone("z3", "红三区", Range(23, 33), "front", pickZoneKey: "front"),
                Zone("zb", "蓝球", Range(1, 16), "back", pickZoneKey: "back"),
            ],
            PL5 => PositionalZones(["万位", "千位", "百位", "十位", "个位"]),
            // 福彩3D 在按位分区之外再加组选号码分布区：开奖三位数字按集合命中（重复数字只算一次），
            // 用于观察组选形态。该区不在 GetPickZones 中，故走势图上不可点选，仅作展示与统计。
            FC3D =>
            [
                .. PositionalZones(["百位", "十位", "个位"]),
                Zone("zg", "组选号码分布", Range(0, 9), "front"),
            ],
            _ =>
            [
                Zone("z1", "一区", Range(1, 12), "front", pickZoneKey: "front"),
                Zone("z2", "二区", Range(13, 24), "front", pickZoneKey: "front"),
                Zone("z3", "三区", Range(25, 35), "front", pickZoneKey: "front"),
                Zone("zb", "后区", Range(1, 12), "back", pickZoneKey: "back"),
            ],
        };

        /// <summary>位置型彩种的分区：每个位置一个分区，号码 0-9，各选 1 个</summary>
        private static List<LotteryZoneDto> PositionalZones(string[] labels)
            => labels.Select((label, i) => new LotteryZoneDto
            {
                Key = $"p{i}",
                Label = label,
                Numbers = Range(0, 9),
                Source = "front",
                Positional = true,
                PosIndex = i,
                Pick = 1,
                PickZoneKey = $"p{i}",
            }).ToList();

        private static LotteryZoneDto Zone(string key, string label, int[] numbers, string source,
            int pick = 0, string? pickZoneKey = null) => new()
        {
            Key = key,
            Label = label,
            Numbers = numbers,
            Source = source,
            Pick = pick,
            PickZoneKey = pickZoneKey ?? key,
        };

        private static int[] Range(int from, int to)
            => Enumerable.Range(from, to - from + 1).ToArray();
    }
}

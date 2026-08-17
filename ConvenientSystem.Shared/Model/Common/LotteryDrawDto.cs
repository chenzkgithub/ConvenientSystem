namespace ConvenientSystem.Shared.Model.Common
{
    /// <summary>
    /// 开奖记录 DTO
    /// </summary>
    public class LotteryDrawDto
    {
        /// <summary>记录 Id</summary>
        public int Id { get; set; }

        /// <summary>期号</summary>
        public string IssueNumber { get; set; } = string.Empty;

        /// <summary>开奖日期</summary>
        public DateTime DrawDate { get; set; }

        /// <summary>前区号码（已排序）</summary>
        public int[] Front { get; set; } = Array.Empty<int>();

        /// <summary>后区号码（已排序）</summary>
        public int[] Back { get; set; } = Array.Empty<int>();
    }

    /// <summary>
    /// 号码统计数据（单个号码）
    /// </summary>
    public class NumberStatDto
    {
        /// <summary>号码</summary>
        public int Number { get; set; }

        /// <summary>出现次数</summary>
        public int Count { get; set; }

        /// <summary>当前遗漏（距上次出现的期数）</summary>
        public int CurrentMiss { get; set; }

        /// <summary>展示窗口首期之前的历史遗漏（距窗口前最近一次出现的期数，取自库内全部早期历史，走势图遗漏种子）</summary>
        public int InitialMiss { get; set; }

        /// <summary>平均遗漏</summary>
        public double AvgMiss { get; set; }

        /// <summary>最大遗漏</summary>
        public int MaxMiss { get; set; }

        /// <summary>最大连出</summary>
        public int MaxConsecutive { get; set; }
    }

    /// <summary>
    /// 彩种分区定义：既用于选号页的选号区域，也用于走势图的列分组。
    /// - 池选型分区（DLT/SSQ）：从 Numbers 号码池中选 Pick 个不重复号码；
    /// - 位置型分区（PL5/FC3D）：Positional=true，对应开奖号码 Front[PosIndex] 位置，各选 1 个。
    /// </summary>
    public class LotteryZoneDto
    {
        /// <summary>分区唯一键（如 front/back/p0/p1/z1）</summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>分区名称（如 前区/蓝球/万位/一区）</summary>
        public string Label { get; set; } = string.Empty;

        /// <summary>分区包含的号码列（升序）</summary>
        public int[] Numbers { get; set; } = Array.Empty<int>();

        /// <summary>号码归属：front=前区数组，back=后区数组</summary>
        public string Source { get; set; } = "front";

        /// <summary>是否位置型：true 时命中判定为 Front[PosIndex] == 号码</summary>
        public bool Positional { get; set; }

        /// <summary>位置型分区对应 Front 数组的下标</summary>
        public int PosIndex { get; set; }

        /// <summary>需选号码数（走势子分区为 0）</summary>
        public int Pick { get; set; }

        /// <summary>归属的选号分区键（走势子分区指向所属选号分区）</summary>
        public string PickZoneKey { get; set; } = string.Empty;

        /// <summary>逐号码统计（仅走势接口返回）</summary>
        public List<NumberStatDto>? Stats { get; set; }
    }

    /// <summary>
    /// 彩种配置（选号页渲染用）
    /// </summary>
    public class LotteryConfigDto
    {
        /// <summary>彩种代码（DLT/SSQ/PL5/FC3D）</summary>
        public string Code { get; set; } = string.Empty;

        /// <summary>彩种名称</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>选号分区列表</summary>
        public List<LotteryZoneDto> PickZones { get; set; } = new();
    }

    /// <summary>
    /// 走势图分析结果
    /// </summary>
    public class LotteryTrendDto
    {
        /// <summary>统计期数</summary>
        public int TotalPeriods { get; set; }

        /// <summary>
        /// 是否历史号码匹配模式。该模式下 Draws 只保留满足全部条件的期并按期号降序，
        /// 相邻行不再是相邻期，故遗漏类统计与连线、纵向连号均不成立，前端据此关闭这些展示。
        /// </summary>
        public bool MatchMode { get; set; }

        /// <summary>匹配到的总期数（超出展示上限被截断时大于 Draws.Count，供前端提示）</summary>
        public int MatchTotal { get; set; }

        /// <summary>开奖记录列表（按期号正序：从旧到新；匹配模式下改为按期号降序）</summary>
        public List<LotteryDrawDto> Draws { get; set; } = new();

        /// <summary>走势图分区（含逐号码统计，顺序即列顺序）</summary>
        public List<LotteryZoneDto> Groups { get; set; } = new();
    }

    /// <summary>
    /// 首页彩票中奖结果（单彩种）：最新一期开奖号码 + 当前用户开奖当日选号的逐注中奖结果
    /// </summary>
    public class LotteryHomeResultDto
    {
        /// <summary>彩种代码（DLT/SSQ/PL5/FC3D）</summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>彩种名称（大乐透/双色球/排列五/福彩3D）</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>最新一期期号（无开奖数据时为空）</summary>
        public string IssueNumber { get; set; } = string.Empty;

        /// <summary>开奖日期（无开奖数据时为 null）</summary>
        public DateTime? DrawDate { get; set; }

        /// <summary>是否位置型彩种（影响号码展示格式）</summary>
        public bool Positional { get; set; }

        /// <summary>开奖前区号码</summary>
        public int[] Front { get; set; } = Array.Empty<int>();

        /// <summary>开奖后区号码（位置型为空）</summary>
        public int[] Back { get; set; } = Array.Empty<int>();

        /// <summary>本人开奖当日的选号注数（0=未参与）</summary>
        public int BetCount { get; set; }

        /// <summary>本人中奖注数</summary>
        public int WinCount { get; set; }

        /// <summary>本人逐注中奖明细</summary>
        public List<LotteryHomeBetResultDto> Bets { get; set; } = new();
    }

    /// <summary>
    /// 首页彩票单注中奖明细
    /// </summary>
    public class LotteryHomeBetResultDto
    {
        /// <summary>选号文本（如 "01 05 12 23 35 + 02 11"）</summary>
        public string Pick { get; set; } = string.Empty;

        /// <summary>中奖结果文案（一等奖…七等奖/福运奖/直选中奖/组选中奖/未中奖）</summary>
        public string Prize { get; set; } = string.Empty;

        /// <summary>是否中奖</summary>
        public bool IsWin { get; set; }
    }

    /// <summary>
    /// 批量导入开奖记录请求
    /// </summary>
    public class LotteryDrawImportRequest
    {
        /// <summary>彩种代码（默认 DLT）</summary>
        public string Type { get; set; } = LotteryTypes.DLT;

        /// <summary>开奖记录列表</summary>
        public List<LotteryDrawItem> Draws { get; set; } = new();
    }

    /// <summary>
    /// 单条开奖记录
    /// </summary>
    public class LotteryDrawItem
    {
        /// <summary>期号</summary>
        public string IssueNumber { get; set; } = string.Empty;

        /// <summary>开奖日期（yyyy-MM-dd）</summary>
        public string DrawDate { get; set; } = string.Empty;

        /// <summary>前区号码</summary>
        public int[] Front { get; set; } = Array.Empty<int>();

        /// <summary>后区号码</summary>
        public int[] Back { get; set; } = Array.Empty<int>();
    }

    /// <summary>
    /// 指定开奖期的官网通告数据（走势图双击查看用）：开奖号码 + 全国中奖明细 + 销量/奖池
    /// </summary>
    public class LotteryDrawNoticeDto
    {
        /// <summary>期号</summary>
        public string IssueNumber { get; set; } = string.Empty;

        /// <summary>开奖日期</summary>
        public DateTime DrawDate { get; set; }

        /// <summary>开奖前区号码</summary>
        public int[] Front { get; set; } = Array.Empty<int>();

        /// <summary>开奖后区号码</summary>
        public int[] Back { get; set; } = Array.Empty<int>();

        /// <summary>官方全国中奖明细（空=历史期未采集）</summary>
        public List<LotteryPrizeGradeDto> Grades { get; set; } = new();

        /// <summary>当期销量（元）</summary>
        public decimal? SalesAmount { get; set; }

        /// <summary>当期奖池滚存（元）</summary>
        public decimal? PoolBalance { get; set; }

        /// <summary>一等奖中奖地区文本（福彩双色球官网通告口径；无则 null）</summary>
        public string? PrizeArea { get; set; }

        /// <summary>官方开奖通告 PDF 链接（体彩大乐透/排列五；无则 null）</summary>
        public string? NoticeUrl { get; set; }
    }

    /// <summary>
    /// 官方中奖明细单行（官网通告口径：奖级/全国注数/单注奖金）
    /// </summary>
    public class LotteryPrizeGradeDto
    {
        /// <summary>奖级名称（一等奖/二等奖/单选/组选3 等）</summary>
        public string Grade { get; set; } = string.Empty;

        /// <summary>全国中奖注数（null=官方未公布）</summary>
        public long? Count { get; set; }

        /// <summary>单注奖金（元）</summary>
        public decimal? Money { get; set; }
    }

    /// <summary>
    /// 逐注中奖明细：奖级 + 命中号码分布（由 LotteryPrizeHelper.CalcHit 产出）
    /// </summary>
    public class LotteryHitResultDto
    {
        /// <summary>中奖结果（未中奖/一等奖…/直选中奖…）</summary>
        public string Prize { get; set; } = string.Empty;

        /// <summary>是否中奖</summary>
        public bool IsWin { get; set; }

        /// <summary>命中的前区/红球号码（池选型；位置型不用此字段）</summary>
        public int[] FrontHits { get; set; } = Array.Empty<int>();

        /// <summary>命中的后区/蓝球号码（池选型）</summary>
        public int[] BackHits { get; set; } = Array.Empty<int>();

        /// <summary>位置型按位是否命中（与本注号码同下标；池选型为空）</summary>
        public bool[] PositionHits { get; set; } = Array.Empty<bool>();

        /// <summary>前区/红球命中个数（位置型为按位命中位数）</summary>
        public int FrontHitCount { get; set; }

        /// <summary>后区/蓝球命中个数</summary>
        public int BackHitCount { get; set; }

        /// <summary>命中情况文字说明（如“前区命中 3 个，后区命中 1 个”）</summary>
        public string HitSummary { get; set; } = string.Empty;
    }

    /// <summary>
    /// 选号记录中奖验证结果：本注奖级 + 命中明细 + 对应开奖期的官网通告数据
    /// </summary>
    public class LotteryVerifyDto
    {
        /// <summary>选号记录 Id</summary>
        public int RecordId { get; set; }

        /// <summary>本注号码文本</summary>
        public string Pick { get; set; } = string.Empty;

        /// <summary>选号当日是否已有开奖（false 时其余开奖字段为空）</summary>
        public bool HasDraw { get; set; }

        /// <summary>对应开奖期号</summary>
        public string IssueNumber { get; set; } = string.Empty;

        /// <summary>开奖日期</summary>
        public DateTime? DrawDate { get; set; }

        /// <summary>开奖前区号码</summary>
        public int[] DrawFront { get; set; } = Array.Empty<int>();

        /// <summary>开奖后区号码</summary>
        public int[] DrawBack { get; set; } = Array.Empty<int>();

        /// <summary>中奖结果（未开奖/未中奖/一等奖…/直选中奖…）</summary>
        public string Prize { get; set; } = string.Empty;

        /// <summary>是否中奖</summary>
        public bool IsWin { get; set; }

        /// <summary>本注奖金（元；税前，null=无官方数据或未中奖）</summary>
        public decimal? Money { get; set; }

        /// <summary>本注应缴个人所得税（元；单注不超 1 万为 0）</summary>
        public decimal? Tax { get; set; }

        /// <summary>本注税后实得奖金（元）</summary>
        public decimal? MoneyAfterTax { get; set; }

        /// <summary>本注所中奖级的全国中奖注数（null=官方明细中未找到对应奖级）</summary>
        public long? GradeCount { get; set; }

        /// <summary>本注对应的官方奖级名（用于在全国中奖明细中定位本注那一行）</summary>
        public string? MatchedGrade { get; set; }

        /// <summary>本注命中明细（未开奖时为 null）</summary>
        public LotteryHitResultDto? Hit { get; set; }

        /// <summary>该期官方全国中奖明细（空=历史期未采集）</summary>
        public List<LotteryPrizeGradeDto> Grades { get; set; } = new();

        /// <summary>当期销量（元）</summary>
        public decimal? SalesAmount { get; set; }

        /// <summary>当期奖池滚存（元）</summary>
        public decimal? PoolBalance { get; set; }

        /// <summary>一等奖中奖地区文本（福彩双色球官网通告口径；无则 null）</summary>
        public string? PrizeArea { get; set; }

        /// <summary>官方开奖通告 PDF 链接（体彩大乐透/排列五；无则 null）</summary>
        public string? NoticeUrl { get; set; }
    }

    /// <summary>
    /// 整期批量验奖中的单注结果：开奖号码与官网通告放在外层，此处不逐注重复
    /// </summary>
    public class LotteryIssueBetDto
    {
        /// <summary>选号记录 Id</summary>
        public int RecordId { get; set; }

        /// <summary>本注前区/红球号码（逐球高亮命中需要原始数组）</summary>
        public int[] Front { get; set; } = Array.Empty<int>();

        /// <summary>本注后区/蓝球号码</summary>
        public int[] Back { get; set; } = Array.Empty<int>();

        /// <summary>本注号码文本</summary>
        public string Pick { get; set; } = string.Empty;

        /// <summary>选号时间</summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>中奖结果（未中奖/一等奖…/直选中奖…）</summary>
        public string Prize { get; set; } = string.Empty;

        /// <summary>是否中奖</summary>
        public bool IsWin { get; set; }

        /// <summary>本注命中明细</summary>
        public LotteryHitResultDto? Hit { get; set; }

        /// <summary>本注奖金（元；税前，null=无官方数据或未中奖）</summary>
        public decimal? Money { get; set; }

        /// <summary>本注应缴个人所得税（元；单注不超 1 万为 0）</summary>
        public decimal? Tax { get; set; }

        /// <summary>本注税后实得奖金（元）</summary>
        public decimal? MoneyAfterTax { get; set; }

        /// <summary>本注所中奖级的全国中奖注数</summary>
        public long? GradeCount { get; set; }

        /// <summary>本注对应的官方奖级名</summary>
        public string? MatchedGrade { get; set; }
    }

    /// <summary>
    /// 整期批量验奖结果：一份开奖号码与官网通告 + 逐注结果 + 合计奖金
    /// </summary>
    public class LotteryIssueVerifyDto
    {
        /// <summary>目标期是否已开奖（false 时其余字段为空）</summary>
        public bool HasDraw { get; set; }

        /// <summary>开奖期号</summary>
        public string IssueNumber { get; set; } = string.Empty;

        /// <summary>开奖日期</summary>
        public DateTime? DrawDate { get; set; }

        /// <summary>开奖前区号码</summary>
        public int[] DrawFront { get; set; } = Array.Empty<int>();

        /// <summary>开奖后区号码</summary>
        public int[] DrawBack { get; set; } = Array.Empty<int>();

        /// <summary>本期逐注验奖结果（按选号时间升序）</summary>
        public List<LotteryIssueBetDto> Bets { get; set; } = new();

        /// <summary>本期参与验奖的总注数</summary>
        public int BetCount { get; set; }

        /// <summary>中奖注数</summary>
        public int WinCount { get; set; }

        /// <summary>合计奖金（元；税前，仅累加官方奖金可得的注）</summary>
        public decimal TotalMoney { get; set; }

        /// <summary>合计应缴个人所得税（元；逐注分别计征后求和）</summary>
        public decimal TotalTax { get; set; }

        /// <summary>合计税后实得奖金（元）</summary>
        public decimal TotalMoneyAfterTax { get; set; }

        /// <summary>是否至少有一注取到了官方奖金（false 时合计金额无意义，前端不展示）</summary>
        public bool MoneyKnown { get; set; }

        /// <summary>该期官方全国中奖明细（空=历史期未采集）</summary>
        public List<LotteryPrizeGradeDto> Grades { get; set; } = new();

        /// <summary>当期销量（元）</summary>
        public decimal? SalesAmount { get; set; }

        /// <summary>当期奖池滚存（元）</summary>
        public decimal? PoolBalance { get; set; }

        /// <summary>一等奖中奖地区文本（福彩双色球官网通告口径）</summary>
        public string? PrizeArea { get; set; }

        /// <summary>官方开奖通告 PDF 链接（体彩大乐透/排列五）</summary>
        public string? NoticeUrl { get; set; }
    }
}

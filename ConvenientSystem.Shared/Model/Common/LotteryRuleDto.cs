namespace ConvenientSystem.Shared.Model.Common
{
    /// <summary>
    /// 奖级命中条件：前区/后区各需命中的最少号码个数。
    /// 官方条文表达的是"至少"语义（如"任意 4 个前区号码及任意 1 个后区号码相同"），
    /// 因此判定为 frontHit &gt;= Front &amp;&amp; backHit &gt;= Back，并按奖级从高到低取首个满足者。
    /// </summary>
    public class LotteryHitCondDto
    {
        /// <summary>前区（红球）需命中个数</summary>
        public int Front { get; set; }

        /// <summary>后区（蓝球）需命中个数</summary>
        public int Back { get; set; }
    }

    /// <summary>奖级判定方式</summary>
    public static class LotteryMatchKind
    {
        /// <summary>池选型按命中个数判定（DLT/SSQ）</summary>
        public const string Hit = "hit";
        /// <summary>位置型按位全同（PL5 一等奖 / FC3D 单选）</summary>
        public const string Exact = "exact";
        /// <summary>位置型同号集合且开奖号码含两位相同（FC3D 组选3）</summary>
        public const string Set3 = "set3";
        /// <summary>位置型同号集合且开奖号码三位各不同（FC3D 组选6）</summary>
        public const string Set6 = "set6";
    }

    /// <summary>
    /// 单个奖级的结构化规则（由官网条文解析而来，存 LotteryRule.GradeJson，判奖直接读它）
    /// </summary>
    public class LotteryGradeRuleDto
    {
        /// <summary>官方奖级名（如 一等奖/福运奖/单选/组选3）</summary>
        public string Grade { get; set; } = string.Empty;

        /// <summary>本系统奖级名（判奖结果与官方明细匹配用的键；池选型与 Grade 相同）</summary>
        public string SystemGrade { get; set; } = string.Empty;

        /// <summary>奖级顺序（从 1 开始，越小奖级越高；判奖按此升序取首个满足者）</summary>
        public int Order { get; set; }

        /// <summary>判定方式（见 <see cref="LotteryMatchKind"/>）</summary>
        public string Match { get; set; } = LotteryMatchKind.Hit;

        /// <summary>命中条件（多个之间为"或"关系；Match 非 hit 时为空）</summary>
        public List<LotteryHitCondDto> Conds { get; set; } = new();

        /// <summary>单注固定奖金（元）；非固定奖级或条文含多档金额时为 null</summary>
        public decimal? FixedMoney { get; set; }

        /// <summary>奖金条文原文（固定奖金分档时保留原文，不做数字取舍）</summary>
        public string? MoneyText { get; set; }

        /// <summary>中奖条件条文原文</summary>
        public string? ConditionText { get; set; }

        /// <summary>是否附条件奖级（如双色球福运奖仅在执行特别规定期间设立，需当期官方明细确有该奖级才判中）</summary>
        public bool Conditional { get; set; }
    }

    /// <summary>
    /// 一个彩种的一套完整玩法规则（生效版本或内置兜底版本）
    /// </summary>
    public class LotteryRuleDto
    {
        /// <summary>彩种代码</summary>
        public string LotteryType { get; set; } = string.Empty;

        /// <summary>奖级规则（按 Order 升序）</summary>
        public List<LotteryGradeRuleDto> Grades { get; set; } = new();

        /// <summary>是否来自内置兜底规则（true 表示库内暂无生效版本）</summary>
        public bool IsDefault { get; set; }
    }

    /// <summary>
    /// 规则版本（页面展示用：条文全文 + 奖级对照表数据 + 版本元信息）
    /// </summary>
    public class LotteryRuleVersionDto
    {
        /// <summary>记录 Id（内置兜底版本为 0）</summary>
        public int Id { get; set; }

        /// <summary>版本号（内置兜底版本为 0）</summary>
        public int Version { get; set; }

        /// <summary>版本状态：1=生效中 2=待审核 3=已被新版替代 4=已驳回</summary>
        public byte Status { get; set; }

        /// <summary>状态文本</summary>
        public string StatusText { get; set; } = string.Empty;

        /// <summary>条文抓取来源页面地址</summary>
        public string? SourceUrl { get; set; }

        /// <summary>官网玩法规则条文全文</summary>
        public string? RuleText { get; set; }

        /// <summary>奖级规则（按 Order 升序，前端据此自绘奖级对照表）</summary>
        public List<LotteryGradeRuleDto> Grades { get; set; } = new();

        /// <summary>最近一次抓到的时间</summary>
        public DateTime CrawledAt { get; set; }

        /// <summary>切为生效的时间</summary>
        public DateTime? EffectiveAt { get; set; }

        /// <summary>审核人账号</summary>
        public string? ReviewedBy { get; set; }

        /// <summary>备注</summary>
        public string? Remark { get; set; }
    }

    /// <summary>
    /// 玩法规则弹窗数据：当前生效版本 + 待审版本（有则给出，供人工比对确认）
    /// </summary>
    public class LotteryRuleViewDto
    {
        /// <summary>彩种代码</summary>
        public string LotteryType { get; set; } = string.Empty;

        /// <summary>彩种名称</summary>
        public string TypeName { get; set; } = string.Empty;

        /// <summary>是否位置型彩种（PL5/FC3D 按位判奖，对照表按位展示）</summary>
        public bool Positional { get; set; }

        /// <summary>前区号码总个数（对照表每行画的前区圆点数；池选型=选号个数，位置型=位数）</summary>
        public int FrontTotal { get; set; }

        /// <summary>后区号码总个数（对照表每行画的后区圆点数；位置型为 0）</summary>
        public int BackTotal { get; set; }

        /// <summary>前区名称（前区/红球）</summary>
        public string FrontLabel { get; set; } = string.Empty;

        /// <summary>后区名称（后区/蓝球；位置型为空）</summary>
        public string BackLabel { get; set; } = string.Empty;

        /// <summary>当前判奖依据的版本（库内无生效版本时为内置兜底规则）</summary>
        public LotteryRuleVersionDto? Current { get; set; }

        /// <summary>是否正在用内置兜底规则判奖</summary>
        public bool UsingDefault { get; set; }

        /// <summary>待审版本（官网条文有变动时给出，需人工确认后才生效）</summary>
        public LotteryRuleVersionDto? Pending { get; set; }
    }

    /// <summary>规则版本审核入参</summary>
    public class LotteryRuleReviewDto
    {
        /// <summary>待审版本记录 Id</summary>
        public int Id { get; set; }

        /// <summary>true=启用该版本，false=驳回</summary>
        public bool Approve { get; set; }

        /// <summary>备注（驳回原因等）</summary>
        public string? Remark { get; set; }
    }
}

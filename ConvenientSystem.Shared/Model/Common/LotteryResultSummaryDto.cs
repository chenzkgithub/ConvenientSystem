namespace ConvenientSystem.Shared.Model.Common
{
    /// <summary>
    /// 开奖结果每日汇总（用于邮件、群机器人卡片详情页等）。
    /// </summary>
    public class LotteryResultSummaryDto
    {
        /// <summary>汇总日期</summary>
        public DateTime Date { get; set; }

        /// <summary>标题</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>副标题/说明</summary>
        public string Subtitle { get; set; } = string.Empty;

        /// <summary>当天无开奖时回退为最新一期</summary>
        public bool IsLatestFallback { get; set; }

        /// <summary>各彩种开奖汇总</summary>
        public List<LotterySummaryDrawDto> Draws { get; set; } = new();
    }

    /// <summary>
    /// 单个彩种的开奖汇总。
    /// </summary>
    public class LotterySummaryDrawDto
    {
        /// <summary>彩种代码</summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>彩种名称</summary>
        public string TypeName { get; set; } = string.Empty;

        /// <summary>主题色</summary>
        public string Color { get; set; } = string.Empty;

        /// <summary>是否位置型（排列五/福彩3D）</summary>
        public bool Positional { get; set; }

        /// <summary>期号</summary>
        public string IssueNumber { get; set; } = string.Empty;

        /// <summary>开奖日期</summary>
        public DateTime DrawDate { get; set; }

        /// <summary>开奖前区/红球号码</summary>
        public int[] Front { get; set; } = Array.Empty<int>();

        /// <summary>开奖后区/蓝球号码</summary>
        public int[] Back { get; set; } = Array.Empty<int>();

        /// <summary>官方奖级明细</summary>
        public List<LotteryPrizeGradeDto> Grades { get; set; } = new();

        /// <summary>当期销量（元）</summary>
        public decimal? SalesAmount { get; set; }

        /// <summary>奖池滚存（元）</summary>
        public decimal? PoolBalance { get; set; }

        /// <summary>一等奖中奖地区</summary>
        public string? PrizeArea { get; set; }

        /// <summary>官网通告 PDF 链接</summary>
        public string? NoticeUrl { get; set; }

        /// <summary>全部用户在本期的选号及中奖结果</summary>
        public List<LotterySummaryRecordDto> Records { get; set; } = new();
    }

    /// <summary>
    /// 汇总页中单个用户的选号及中奖结果。
    /// </summary>
    public class LotterySummaryRecordDto
    {
        /// <summary>用户显示名</summary>
        public string UserName { get; set; } = string.Empty;

        /// <summary>前区/红球选号</summary>
        public int[] Front { get; set; } = Array.Empty<int>();

        /// <summary>后区/蓝球选号</summary>
        public int[] Back { get; set; } = Array.Empty<int>();

        /// <summary>选号时间</summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>该记录归属的开奖日期</summary>
        public DateTime? DrawDate { get; set; }

        /// <summary>命中明细</summary>
        public LotteryHitResultDto Hit { get; set; } = new();

        /// <summary>中奖结果文字</summary>
        public string Prize { get; set; } = string.Empty;

        /// <summary>单注奖金（税前，null 表示未知）</summary>
        public decimal? Money { get; set; }

        /// <summary>个税（null 表示未知或无税）</summary>
        public decimal? Tax { get; set; }

        /// <summary>税后实得（null 表示未知）</summary>
        public decimal? Net { get; set; }
    }
}

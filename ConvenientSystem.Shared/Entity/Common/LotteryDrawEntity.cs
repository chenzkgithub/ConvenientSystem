using FreeSql.DataAnnotations;

namespace ConvenientSystem.Shared.Entity.Common
{
    /// <summary>
    /// 彩票开奖记录表（见 db/init.sql dbo.LotteryDraw，多彩种共用）
    /// </summary>
    [Table(Name = "LotteryDraw")]
    public class LotteryDrawEntity
    {
        [Column(IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>彩种代码（DLT/SSQ/PL5/FC3D）</summary>
        public string LotteryType { get; set; } = "DLT";

        /// <summary>期号（如 "2026087"）</summary>
        public string IssueNumber { get; set; } = string.Empty;

        /// <summary>开奖日期</summary>
        public DateTime DrawDate { get; set; }

        /// <summary>前区号码（逗号分隔；池选型已排序如 "01,05,12,23,35"，位置型按位存储如 "3,5,0,9,1"）</summary>
        public string FrontNumbers { get; set; } = string.Empty;

        /// <summary>后区号码（逗号分隔，已排序，如 "02,11"）</summary>
        public string BackNumbers { get; set; } = string.Empty;

        /// <summary>官方中奖明细 JSON：[{"grade":"一等奖","count":2,"money":9662603}]，历史期未采集时为 null</summary>
        public string? PrizeDetail { get; set; }

        /// <summary>当期销量（元）</summary>
        [Column(Precision = 18, Scale = 2)]
        public decimal? SalesAmount { get; set; }

        /// <summary>奖池滚存（元；固定奖彩种为空）</summary>
        [Column(Precision = 18, Scale = 2)]
        public decimal? PoolBalance { get; set; }

        /// <summary>一等奖中奖地区文本（福彩双色球官网通告口径，多省份时较长；无则 null）</summary>
        [Column(StringLength = 500)]
        public string? PrizeArea { get; set; }

        /// <summary>官方开奖通告 PDF 链接（体彩大乐透/排列五；无则 null）</summary>
        [Column(StringLength = 500)]
        public string? NoticeUrl { get; set; }

        /// <summary>创建时间</summary>
        [Column(CanInsert = false, CanUpdate = false)]
        public DateTime CreatedAt { get; set; }
    }
}

using FreeSql.DataAnnotations;

namespace ConvenientSystem.Shared.Entity.Common
{
    /// <summary>
    /// 彩票玩法规则表（见 db/init.sql dbo.LotteryRule）：每日自官网抓取条文并按版本留存，
    /// 只有 Status=1 的版本参与判奖，条文变动时新版先以 Status=2 待审入库
    /// </summary>
    [Table(Name = "LotteryRule")]
    public class LotteryRuleEntity
    {
        [Column(IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>彩种代码（DLT/SSQ/PL5/FC3D）</summary>
        [Column(StringLength = 10)]
        public string LotteryType { get; set; } = "DLT";

        /// <summary>同彩种内递增的规则版本号</summary>
        public int Version { get; set; }

        /// <summary>版本状态（见 <see cref="LotteryRuleStatus"/>）：1=生效中 2=待审核 3=已被新版替代 4=已驳回</summary>
        public byte Status { get; set; }

        /// <summary>条文抓取来源页面地址</summary>
        [Column(StringLength = 500)]
        public string? SourceUrl { get; set; }

        /// <summary>官网玩法规则条文全文（纯文本，供页面展示与版本比对）</summary>
        [Column(StringLength = -1)]
        public string? RuleText { get; set; }

        /// <summary>结构化奖级规则 JSON（List&lt;LotteryGradeRuleDto&gt;，判奖直接读此列）</summary>
        [Column(StringLength = -1)]
        public string? GradeJson { get; set; }

        /// <summary>本版本解析出的奖级数（为 0 说明解析失败，不会入库）</summary>
        public int GradeCount { get; set; }

        /// <summary>条文+奖级 JSON 的 SHA256（比对官网规则是否变动）</summary>
        [Column(StringLength = 64)]
        public string? ContentHash { get; set; }

        /// <summary>本版本最近一次抓到的时间</summary>
        public DateTime CrawledAt { get; set; }

        /// <summary>切为生效的时间（未生效过为 null）</summary>
        public DateTime? EffectiveAt { get; set; }

        /// <summary>审核人账号（首次自动生效时为系统）</summary>
        [Column(StringLength = 50)]
        public string? ReviewedBy { get; set; }

        /// <summary>备注（首次自动生效/驳回原因等）</summary>
        [Column(StringLength = 500)]
        public string? Remark { get; set; }

        /// <summary>创建时间</summary>
        [Column(CanInsert = false, CanUpdate = false)]
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>规则版本状态</summary>
    public static class LotteryRuleStatus
    {
        /// <summary>生效中：同彩种最多一行，判奖只读这一行</summary>
        public const byte Active = 1;
        /// <summary>待审核：官网条文有变动，等人工确认后才生效</summary>
        public const byte Pending = 2;
        /// <summary>已被新版替代</summary>
        public const byte Replaced = 3;
        /// <summary>已驳回：人工确认官网变动不采纳</summary>
        public const byte Rejected = 4;
    }
}

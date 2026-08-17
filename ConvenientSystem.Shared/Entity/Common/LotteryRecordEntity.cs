using FreeSql.DataAnnotations;

namespace ConvenientSystem.Shared.Entity.Common
{
    /// <summary>
    /// 彩票选号记录表（见 db/init.sql dbo.LotteryRecord，多彩种共用）
    /// </summary>
    [Table(Name = "dbo.LotteryRecord")]
    public class LotteryRecordEntity
    {
        [Column(IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>所属用户 Id（SysUser.Id，GUID）</summary>
        public Guid UserId { get; set; }

        /// <summary>彩种代码（DLT/SSQ/PL5/FC3D）</summary>
        public string LotteryType { get; set; } = "DLT";

        /// <summary>前区号码（逗号分隔；池选型已排序，位置型按位存储）</summary>
        public string FrontNumbers { get; set; } = string.Empty;

        /// <summary>后区号码（逗号分隔，已排序，如 "02,11"）</summary>
        public string BackNumbers { get; set; } = string.Empty;

        /// <summary>所属期号（保存时默认取下一期；历史记录为 null）</summary>
        [Column(StringLength = 20)]
        public string? IssueNumber { get; set; }

        /// <summary>开奖日期（保存时默认取下一期开奖日；历史记录为 null）</summary>
        public DateTime? DrawDate { get; set; }

        /// <summary>选号时间</summary>
        [Column(CanInsert = false, CanUpdate = false)]
        public DateTime CreatedAt { get; set; }
    }
}

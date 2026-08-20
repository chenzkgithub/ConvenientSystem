using FreeSql.DataAnnotations;

namespace ConvenientSystem.Shared.Entity.Sms
{
    /// <summary>
    /// 短信配额表（见 db/init.sql dbo.SmsQuota）
    /// </summary>
    [Table(Name = "SmsQuota")]
    public class SmsQuotaEntity
    {
        [Column(IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>配额类型：Daily / Monthly</summary>
        public string QuotaType { get; set; } = string.Empty;

        /// <summary>上限条数</summary>
        public int MaxCount { get; set; } = 100;

        /// <summary>更新时间</summary>
        public DateTime UpdateTime { get; set; } = DateTime.Now;
    }
}

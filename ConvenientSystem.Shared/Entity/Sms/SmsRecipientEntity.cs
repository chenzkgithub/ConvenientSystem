using FreeSql.DataAnnotations;

namespace ConvenientSystem.Shared.Entity.Sms
{
    /// <summary>
    /// 短信收件人表（见 db/init.sql dbo.SmsRecipient）
    /// </summary>
    [Table(Name = "dbo.SmsRecipient")]
    public class SmsRecipientEntity
    {
        [Column(IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>关联任务 ID</summary>
        public int TaskId { get; set; }

        /// <summary>手机号</summary>
        public string Phone { get; set; } = string.Empty;

        /// <summary>姓名（用于模板变量替换）</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 发送状态：0=待发送 1=成功 2=失败
        /// </summary>
        public byte Status { get; set; }

        /// <summary>失败原因</summary>
        public string? ErrorMessage { get; set; }

        /// <summary>实际发送时间</summary>
        public DateTime? SentTime { get; set; }
    }
}

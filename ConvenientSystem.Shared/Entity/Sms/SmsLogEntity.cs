using FreeSql.DataAnnotations;

namespace ConvenientSystem.Shared.Entity.Sms
{
    /// <summary>
    /// 短信发送日志表（见 db/init.sql dbo.SmsLog）
    /// </summary>
    [Table(Name = "SmsLog")]
    public class SmsLogEntity
    {
        [Column(IsPrimary = true, IsIdentity = true)]
        public long Id { get; set; }

        /// <summary>关联任务 ID</summary>
        public int TaskId { get; set; }

        /// <summary>关联收件人 ID</summary>
        public int RecipientId { get; set; }

        /// <summary>手机号</summary>
        public string Phone { get; set; } = string.Empty;

        /// <summary>实际发送内容（变量已替换）</summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>阿里云 RequestId</summary>
        public string? ProviderMsgId { get; set; }

        /// <summary>发送状态：0=失败 1=成功</summary>
        public byte Status { get; set; }

        /// <summary>错误信息</summary>
        public string? ErrorMessage { get; set; }

        /// <summary>耗时毫秒</summary>
        public int CostMs { get; set; }

        /// <summary>发送时间</summary>
        [Column(CanInsert = false, CanUpdate = false)]
        public DateTime CreateTime { get; set; }
    }
}

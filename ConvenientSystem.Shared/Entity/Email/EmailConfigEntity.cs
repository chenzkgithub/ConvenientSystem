using FreeSql.DataAnnotations;

namespace ConvenientSystem.Shared.Entity.Email
{
    /// <summary>
    /// 邮件 SMTP 配置表（列表化，支持多条配置）
    /// </summary>
    [Table(Name = "dbo.EmailConfig")]
    public class EmailConfigEntity
    {
        [Column(IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>SMTP 服务器地址</summary>
        public string SmtpServer { get; set; } = string.Empty;

        /// <summary>SMTP 端口</summary>
        public int SmtpPort { get; set; } = 587;

        /// <summary>发件人邮箱</summary>
        public string Account { get; set; } = string.Empty;

        /// <summary>授权码（AES 加密存储）</summary>
        public string Password { get; set; } = string.Empty;

        /// <summary>发件人显示名</summary>
        public string FromName { get; set; } = "系统通知";

        /// <summary>是否启用 SSL</summary>
        public bool EnableSsl { get; set; } = true;

        /// <summary>配置名称</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>是否默认配置</summary>
        public bool IsDefault { get; set; } = false;

        /// <summary>是否启用</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>创建时间</summary>
        [Column(CanInsert = false, CanUpdate = false)]
        public DateTime CreateTime { get; set; }

        /// <summary>更新时间</summary>
        public DateTime UpdateTime { get; set; } = DateTime.Now;
    }
}

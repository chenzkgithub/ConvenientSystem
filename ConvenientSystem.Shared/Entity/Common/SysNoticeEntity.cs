using FreeSql.DataAnnotations;

namespace ConvenientSystem.Shared.Entity.Common
{
    /// <summary>
    /// 系统通知表（见 db/init.sql dbo.SysNotice）。
    /// 管理员发布站内通知，发布时可按需联动邮件/短信/群机器人推送。
    /// </summary>
    [Table(Name = "SysNotice")]
    public class SysNoticeEntity
    {
        [Column(IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>通知标题</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>通知内容（NVARCHAR(MAX)）</summary>
        [Column(StringLength = -1)]
        public string Content { get; set; } = string.Empty;

        /// <summary>通知级别：1=普通 2=重要 3=紧急</summary>
        public byte Level { get; set; } = 1;

        /// <summary>发布时是否联动邮件推送给已填邮箱的用户</summary>
        public bool SendEmail { get; set; }

        /// <summary>发布时是否联动短信推送给已填手机号的用户</summary>
        public bool SendSms { get; set; }

        /// <summary>发布时是否联动群机器人广播</summary>
        public bool SendWebhook { get; set; }

        /// <summary>是否启用（停用后用户端不再展示）</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>有效期截止时间（NULL=永久有效；过期后用户端不再展示）</summary>
        public DateTime? ExpireTime { get; set; }

        /// <summary>发布人用户 Id（关联 SysUser.Id）</summary>
        public Guid? CreatedById { get; set; }

        [Column(CanInsert = false, CanUpdate = false)]
        public DateTime CreateTime { get; set; }

        public DateTime UpdateTime { get; set; }
    }
}

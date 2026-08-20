using FreeSql.DataAnnotations;

namespace ConvenientSystem.Shared.Entity.Email
{
    /// <summary>
    /// 邮件发送日志表
    /// </summary>
    [Table(Name = "EmailLog")]
    public class EmailLogEntity
    {
        [Column(IsPrimary = true, IsIdentity = true)]
        public long Id { get; set; }

        /// <summary>关联任务 ID</summary>
        public int TaskId { get; set; }

        /// <summary>任务名称</summary>
        public string TaskName { get; set; } = string.Empty;

        /// <summary>实际收件人</summary>
        public string Recipients { get; set; } = string.Empty;

        /// <summary>邮件主题</summary>
        public string Subject { get; set; } = string.Empty;

        /// <summary>实际发送内容（变量已替换；HTML 正文可能很大，用 NVARCHAR(MAX)）</summary>
        [Column(StringLength = -1)]
        public string Content { get; set; } = string.Empty;

        /// <summary>0=失败 1=成功</summary>
        public byte Status { get; set; }

        /// <summary>错误信息</summary>
        public string? ErrorMessage { get; set; }

        /// <summary>耗时毫秒</summary>
        public int CostMs { get; set; }

        /// <summary>创建人用户 Id（SysUser.Id，GUID；关联 SysUser 展示账号与姓名；系统自动发送时为 null）</summary>
        public Guid? CreatedById { get; set; }

        /// <summary>创建时间</summary>
        public DateTime CreateTime { get; set; } = DateTime.Now;
    }
}

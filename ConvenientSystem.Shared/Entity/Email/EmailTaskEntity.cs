using FreeSql.DataAnnotations;

namespace ConvenientSystem.Shared.Entity.Email
{
    /// <summary>
    /// 邮件定时任务表
    /// </summary>
    [Table(Name = "EmailTask")]
    public class EmailTaskEntity
    {
        [Column(IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>任务名称</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>邮件主题</summary>
        public string Subject { get; set; } = string.Empty;

        /// <summary>邮件内容（支持 {日期} {时间} 变量）</summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>收件人（多个用分号分隔）</summary>
        public string Recipients { get; set; } = string.Empty;

        /// <summary>调度类型：once / daily / weekly / cron</summary>
        public string ScheduleType { get; set; } = "once";

        /// <summary>单次发送时间</summary>
        public DateTime? SendTime { get; set; }

        /// <summary>Cron 表达式（自定义周期）</summary>
        public string? CronExpression { get; set; }

        /// <summary>每周几（如 "1,3,5"，0=周日）</summary>
        public string? WeekDays { get; set; }

        /// <summary>每天/每周的发送时间（如 "09:00"）</summary>
        public string? DailyTime { get; set; }

        /// <summary>Hangfire Job ID</summary>
        public string? HangfireJobId { get; set; }

        /// <summary>是否启用</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>状态：0=正常 1=暂停</summary>
        public byte Status { get; set; } = 0;

        /// <summary>上次发送时间</summary>
        public DateTime? LastSendTime { get; set; }

        /// <summary>创建时间</summary>
        public DateTime CreateTime { get; set; } = DateTime.Now;

        /// <summary>创建人用户 Id（SysUser.Id，GUID；用于数据权限过滤，列表关联 SysUser 展示账号与姓名）</summary>
        public Guid? CreatedById { get; set; }

        /// <summary>更新时间</summary>
        public DateTime UpdateTime { get; set; } = DateTime.Now;
    }
}

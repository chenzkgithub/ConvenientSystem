using FreeSql.DataAnnotations;

namespace ConvenientSystem.Shared.Entity.Sms
{
    /// <summary>
    /// 短信任务表（见 db/init.sql dbo.SmsTask）
    /// </summary>
    [Table(Name = "dbo.SmsTask")]
    public class SmsTaskEntity
    {
        [Column(IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>任务名称</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>关联模板 ID</summary>
        public int TemplateId { get; set; }

        /// <summary>计划发送时间</summary>
        public DateTime SendTime { get; set; }

        /// <summary>Hangfire Job ID（用于取消任务）</summary>
        public string? HangfireJobId { get; set; }

        /// <summary>
        /// 任务状态：0=待执行 1=执行中 2=已完成 3=已取消 4=失败
        /// </summary>
        public byte Status { get; set; }

        /// <summary>总收件人数</summary>
        public int TotalCount { get; set; }

        /// <summary>成功数</summary>
        public int SuccessCount { get; set; }

        /// <summary>失败数</summary>
        public int FailCount { get; set; }

        /// <summary>创建人用户 Id（SysUser.Id，GUID；用于数据权限过滤，列表关联 SysUser 展示账号与姓名）</summary>
        public Guid? CreatedById { get; set; }

        /// <summary>创建时间</summary>
        [Column(CanInsert = false, CanUpdate = false)]
        public DateTime CreateTime { get; set; }

        /// <summary>更新时间</summary>
        public DateTime UpdateTime { get; set; } = DateTime.Now;
    }
}

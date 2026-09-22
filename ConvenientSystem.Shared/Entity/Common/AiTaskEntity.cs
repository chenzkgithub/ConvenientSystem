using ConvenientSystem.Shared.Common;
using FreeSql.DataAnnotations;

namespace ConvenientSystem.Shared.Entity.Common
{
    /// <summary>AI 助手创建的任务/待办</summary>
    [Table(Name = "AiTask")]
    public class AiTaskEntity
    {
        [Column(IsIdentity = true, IsPrimary = true)]
        public long Id { get; set; }

        /// <summary>创建用户 Id</summary>
        [Column(StringLength = 64)]
        public string UserId { get; set; } = string.Empty;

        /// <summary>任务标题</summary>
        [Column(StringLength = 200)]
        public string Title { get; set; } = string.Empty;

        /// <summary>任务描述</summary>
        [Column(StringLength = -1)]
        public string? Description { get; set; }

        /// <summary>截止时间</summary>
        public DateTime? DueDate { get; set; }

        /// <summary>状态：0 待办 / 1 完成</summary>
        public int Status { get; set; }

        /// <summary>创建时间</summary>
        public DateTime CreateTime { get; set; } = TimeHelper.Now;
    }
}

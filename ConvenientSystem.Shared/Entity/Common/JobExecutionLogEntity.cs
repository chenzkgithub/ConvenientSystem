using FreeSql.DataAnnotations;

namespace ConvenientSystem.Shared.Entity.Common
{
    /// <summary>
    /// 定时任务执行日志表（见 db/init.sql dbo.JobExecutionLog）：由各 Job 基类
    /// ExecuteWithLog 在任务执行前后自动写入/更新，供「定时任务管理 → 日志」弹窗查看。
    /// </summary>
    [Table(Name = "JobExecutionLog")]
    public class JobExecutionLogEntity
    {
        [Column(IsPrimary = true, IsIdentity = true)]
        public long Id { get; set; }

        /// <summary>对应 RecurringJobId（如"网站监控定时巡检"）</summary>
        public string JobName { get; set; } = string.Empty;

        /// <summary>执行状态：Succeeded / Failed</summary>
        public string State { get; set; } = string.Empty;

        /// <summary>方法名</summary>
        public string? MethodName { get; set; }

        /// <summary>参数 JSON</summary>
        public string? Arguments { get; set; }

        /// <summary>开始时间</summary>
        public DateTime StartedAt { get; set; }

        /// <summary>结束时间</summary>
        public DateTime? FinishedAt { get; set; }

        /// <summary>耗时毫秒</summary>
        public long? DurationMs { get; set; }

        /// <summary>异常信息</summary>
        public string? Error { get; set; }

        /// <summary>创建时间</summary>
        [Column(CanInsert = false, CanUpdate = false)]
        public DateTime CreatedAt { get; set; }
    }
}

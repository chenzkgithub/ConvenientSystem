using FreeSql.DataAnnotations;

namespace ConvenientSystem.Shared.Entity.Common
{
    /// <summary>
    /// 主机资源监控探测日志表（本地配置库 ConvenientSystem，见 db/init.sql；保留 30 天）
    /// </summary>
    [Table(Name = "dbo.HostMonitorLog")]
    public class HostMonitorLogEntity
    {
        [Column(IsPrimary = true, IsIdentity = true)]
        public long Id { get; set; }

        /// <summary>关联监控目标 Id</summary>
        public int TargetId { get; set; }

        /// <summary>探测结果：1=正常 2=异常</summary>
        public byte Status { get; set; }

        /// <summary>探测值（磁盘已用%/内存使用率%/CPU 使用率%；SVC 为运行中服务数）</summary>
        [Column(Precision = 10, Scale = 2)]
        public decimal? Value { get; set; }

        /// <summary>异常原因</summary>
        public string? ErrorMsg { get; set; }

        /// <summary>整机概览指标快照 JSON（CPU/内存/磁盘/网络/IO，仅 HOST）</summary>
        public string? MetricsJson { get; set; }

        /// <summary>探测时间</summary>
        public DateTime CheckAt { get; set; } = DateTime.Now;
    }
}

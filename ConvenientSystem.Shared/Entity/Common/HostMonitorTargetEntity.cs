using FreeSql.DataAnnotations;

namespace ConvenientSystem.Shared.Entity.Common
{
    /// <summary>
    /// 主机资源监控目标表（本地配置库 ConvenientSystem，见 db/init.sql）。
    /// 支持磁盘/内存/CPU 使用率阈值告警与 Windows 服务运行状态检查。
    /// </summary>
    [Table(Name = "HostMonitorTarget")]
    public class HostMonitorTargetEntity
    {
        [Column(IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>监控目标名称</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>指标类型（DISK=磁盘 MEM=内存 CPU=CPU SVC=服务 HOST=整机概览）</summary>
        public string MetricType { get; set; } = "DISK";

        /// <summary>目标电脑 IP/主机名（NULL/空=本机；远程采集需目标开启 WinRM）</summary>
        public string? HostAddress { get; set; }

        /// <summary>远程采集账号（远程目标必填，如 .\Administrator）</summary>
        public string? AuthAccount { get; set; }

        /// <summary>远程采集密码（仅远程目标使用）</summary>
        public string? AuthPassword { get; set; }

        /// <summary>整机概览最近指标快照 JSON（CPU/内存/磁盘/开机时长，仅 HOST）</summary>
        public string? MetricsJson { get; set; }

        /// <summary>磁盘盘符（如 C；NULL=所有固定磁盘，仅 DISK 有效）</summary>
        public string? DriveLetter { get; set; }

        /// <summary>Windows 服务名列表（逗号分隔，仅 SVC 有效）</summary>
        public string? ServiceNames { get; set; }

        /// <summary>告警阈值百分比（磁盘已用%/内存使用率%/CPU 使用率%，超过即异常）</summary>
        [Column(Precision = 5, Scale = 2)]
        public decimal? ThresholdPercent { get; set; }

        /// <summary>单次探测超时（秒）</summary>
        public int TimeoutSeconds { get; set; } = 30;

        /// <summary>探测间隔（分钟）</summary>
        public int IntervalMinutes { get; set; } = 10;

        /// <summary>是否启用监控</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>状态变化时是否邮件告警</summary>
        public bool NotifyEmail { get; set; } = true;

        /// <summary>最近探测结果：NULL=未探测 1=正常 2=异常</summary>
        public byte? LastStatus { get; set; }

        /// <summary>最近探测值（磁盘已用%/内存使用率%/CPU 使用率%；SVC 为运行中服务数）</summary>
        [Column(Precision = 10, Scale = 2)]
        public decimal? LastValue { get; set; }

        /// <summary>最近异常原因</summary>
        public string? LastErrorMsg { get; set; }

        /// <summary>最近探测时间</summary>
        public DateTime? LastCheckAt { get; set; }

        /// <summary>CPU 计数器快照（内部使用：上次采样的 ProcessorTime 与时间戳，JSON 格式）</summary>
        public string? SnapshotJson { get; set; }

        /// <summary>备注</summary>
        public string? Remark { get; set; }

        /// <summary>创建时间</summary>
        public DateTime CreateTime { get; set; } = DateTime.Now;
    }
}

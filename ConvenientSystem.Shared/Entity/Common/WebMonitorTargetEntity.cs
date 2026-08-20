using FreeSql.DataAnnotations;

namespace ConvenientSystem.Shared.Entity.Common
{
    /// <summary>
    /// 网站/API 监控目标表（本地配置库 ConvenientSystem，见 db/init.sql）
    /// </summary>
    [Table(Name = "WebMonitorTarget")]
    public class WebMonitorTargetEntity
    {
        [Column(IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>监控目标名称</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>被监控地址（http/https）</summary>
        public string Url { get; set; } = string.Empty;

        /// <summary>请求方式（GET/POST/HEAD）</summary>
        public string Method { get; set; } = "GET";

        /// <summary>期望 HTTP 状态码</summary>
        public int ExpectStatus { get; set; } = 200;

        /// <summary>期望关键字（响应体包含才算正常；NULL 不校验）</summary>
        public string? ExpectKeyword { get; set; }

        /// <summary>单次探测超时（秒）</summary>
        public int TimeoutSeconds { get; set; } = 10;

        /// <summary>探测间隔（分钟）</summary>
        public int IntervalMinutes { get; set; } = 10;

        /// <summary>是否启用监控</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>状态变化时是否邮件告警</summary>
        public bool NotifyEmail { get; set; } = true;

        /// <summary>最近探测结果：NULL=未探测 1=正常 2=异常</summary>
        public byte? LastStatus { get; set; }

        /// <summary>最近探测耗时（毫秒）</summary>
        public int? LastLatencyMs { get; set; }

        /// <summary>最近异常原因</summary>
        public string? LastErrorMsg { get; set; }

        /// <summary>最近探测时间</summary>
        public DateTime? LastCheckAt { get; set; }

        /// <summary>备注</summary>
        public string? Remark { get; set; }

        /// <summary>创建时间</summary>
        public DateTime CreateTime { get; set; } = DateTime.Now;
    }
}

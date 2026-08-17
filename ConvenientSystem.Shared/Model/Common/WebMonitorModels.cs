namespace ConvenientSystem.Shared.Model.Common
{
    /// <summary>监控目标列表项 DTO</summary>
    public class WebMonitorTargetDto
    {
        public int Id { get; set; }
        /// <summary>监控目标名称</summary>
        public string Name { get; set; } = string.Empty;
        /// <summary>被监控地址</summary>
        public string Url { get; set; } = string.Empty;
        /// <summary>请求方式（GET/POST/HEAD）</summary>
        public string Method { get; set; } = "GET";
        /// <summary>期望 HTTP 状态码</summary>
        public int ExpectStatus { get; set; } = 200;
        /// <summary>期望关键字（NULL 不校验）</summary>
        public string? ExpectKeyword { get; set; }
        /// <summary>单次探测超时（秒）</summary>
        public int TimeoutSeconds { get; set; } = 10;
        /// <summary>探测间隔（分钟）</summary>
        public int IntervalMinutes { get; set; } = 10;
        /// <summary>是否启用监控</summary>
        public bool Enabled { get; set; }
        /// <summary>状态变化时是否邮件告警</summary>
        public bool NotifyEmail { get; set; }
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
    }

    /// <summary>监控目标新增/编辑 DTO（Id 为空表示新增）</summary>
    public class WebMonitorTargetSaveDto
    {
        public int? Id { get; set; }
        /// <summary>监控目标名称</summary>
        public string Name { get; set; } = string.Empty;
        /// <summary>被监控地址（http/https）</summary>
        public string Url { get; set; } = string.Empty;
        /// <summary>请求方式（GET/POST/HEAD）</summary>
        public string Method { get; set; } = "GET";
        /// <summary>期望 HTTP 状态码</summary>
        public int ExpectStatus { get; set; } = 200;
        /// <summary>期望关键字（响应体包含才算正常；空不校验）</summary>
        public string? ExpectKeyword { get; set; }
        /// <summary>单次探测超时（秒）</summary>
        public int TimeoutSeconds { get; set; } = 10;
        /// <summary>探测间隔（分钟）</summary>
        public int IntervalMinutes { get; set; } = 10;
        /// <summary>是否启用监控</summary>
        public bool Enabled { get; set; } = true;
        /// <summary>状态变化时是否邮件告警</summary>
        public bool NotifyEmail { get; set; } = true;
        /// <summary>备注</summary>
        public string? Remark { get; set; }
    }

    /// <summary>探测日志 DTO</summary>
    public class WebMonitorLogDto
    {
        public long Id { get; set; }
        /// <summary>探测结果：1=正常 2=异常</summary>
        public byte Status { get; set; }
        /// <summary>实际 HTTP 状态码（网络层失败为 NULL）</summary>
        public int? HttpStatusCode { get; set; }
        /// <summary>探测耗时（毫秒）</summary>
        public int? LatencyMs { get; set; }
        /// <summary>异常原因</summary>
        public string? ErrorMsg { get; set; }
        /// <summary>探测时间</summary>
        public DateTime CheckAt { get; set; }
    }
}

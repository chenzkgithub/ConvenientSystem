using FreeSql.DataAnnotations;

namespace ConvenientSystem.Shared.Entity.Common
{
    /// <summary>
    /// 网站/API 监控探测日志表（本地配置库 ConvenientSystem，见 db/init.sql；保留 30 天）
    /// </summary>
    [Table(Name = "dbo.WebMonitorLog")]
    public class WebMonitorLogEntity
    {
        [Column(IsPrimary = true, IsIdentity = true)]
        public long Id { get; set; }

        /// <summary>关联监控目标 Id</summary>
        public int TargetId { get; set; }

        /// <summary>探测结果：1=正常 2=异常</summary>
        public byte Status { get; set; }

        /// <summary>实际 HTTP 状态码（网络层失败为 NULL）</summary>
        public int? HttpStatusCode { get; set; }

        /// <summary>探测耗时（毫秒）</summary>
        public int? LatencyMs { get; set; }

        /// <summary>异常原因</summary>
        public string? ErrorMsg { get; set; }

        /// <summary>探测时间</summary>
        public DateTime CheckAt { get; set; } = DateTime.Now;
    }
}

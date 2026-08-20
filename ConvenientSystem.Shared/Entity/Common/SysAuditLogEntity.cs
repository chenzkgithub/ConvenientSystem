using FreeSql.DataAnnotations;

namespace ConvenientSystem.Shared.Entity.Common
{
    /// <summary>
    /// 操作审计日志表（见 db/init.sql dbo.SysAuditLog）：仅记录写操作（POST/PUT/DELETE）。
    /// </summary>
    [Table(Name = "SysAuditLog")]
    public class SysAuditLogEntity
    {
        [Column(IsPrimary = true, IsIdentity = true)]
        public long Id { get; set; }

        /// <summary>操作人 SysUser.Id（GUID；匿名/未登录为 null）</summary>
        public Guid? UserId { get; set; }

        /// <summary>操作人账号</summary>
        public string Account { get; set; } = string.Empty;

        /// <summary>操作描述（控制器/方法名）</summary>
        public string Action { get; set; } = string.Empty;

        /// <summary>所属模块（Area）</summary>
        public string Module { get; set; } = string.Empty;

        /// <summary>请求路径</summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>HTTP 方法</summary>
        public string Method { get; set; } = string.Empty;

        /// <summary>客户端 IP</summary>
        public string Ip { get; set; } = string.Empty;

        /// <summary>请求体摘要（截断 2000）</summary>
        public string? ParamSummary { get; set; }

        /// <summary>是否成功（状态码 &lt; 400）</summary>
        public bool Success { get; set; }

        /// <summary>HTTP 状态码</summary>
        public int StatusCode { get; set; }

        /// <summary>耗时毫秒</summary>
        public int CostMs { get; set; }

        /// <summary>发生时间</summary>
        [Column(CanInsert = false, CanUpdate = false)]
        public DateTime CreateTime { get; set; }
    }
}

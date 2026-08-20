using FreeSql.DataAnnotations;

namespace ConvenientSystem.Shared.Entity.Common
{
    /// <summary>
    /// 系统错误日志表（见 db/init.sql dbo.SysErrorLog）：由全局异常过滤器 BizExceptionFilter
    /// 在捕获未处理异常时写入，记录异常类型、消息、堆栈、请求路径与操作人。
    /// </summary>
    [Table(Name = "SysErrorLog")]
    public class SysErrorLogEntity
    {
        [Column(IsPrimary = true, IsIdentity = true)]
        public long Id { get; set; }

        /// <summary>操作人 SysUser.Id（GUID；未登录为 null）</summary>
        public Guid? UserId { get; set; }

        /// <summary>操作人账号（未登录为空）</summary>
        public string Account { get; set; } = string.Empty;

        /// <summary>请求路径</summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>HTTP 方法</summary>
        public string Method { get; set; } = string.Empty;

        /// <summary>返回给客户端的 HTTP 状态码</summary>
        public int StatusCode { get; set; }

        /// <summary>异常类型全名</summary>
        public string ExceptionType { get; set; } = string.Empty;

        /// <summary>异常消息（截断 2000）</summary>
        public string ErrorMessage { get; set; } = string.Empty;

        /// <summary>完整堆栈跟踪（截断 8000）</summary>
        public string? StackTrace { get; set; }

        /// <summary>客户端 IP</summary>
        public string Ip { get; set; } = string.Empty;

        /// <summary>发生时间</summary>
        [Column(CanInsert = false, CanUpdate = false)]
        public DateTime CreateTime { get; set; }
    }
}

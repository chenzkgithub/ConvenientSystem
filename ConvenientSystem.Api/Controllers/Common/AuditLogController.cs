using ConvenientSystem.Api.Auth;
using ConvenientSystem.Service.Common;
using ConvenientSystem.Shared.Model.Common;
using Microsoft.AspNetCore.Mvc;

namespace ConvenientSystem.Api.Controllers.Common
{
    /// <summary>
    /// 操作审计日志接口。
    /// </summary>
    [Area("Common")]
    [PermissionAuthorize("audit-log")]
    public class AuditLogController : BaseController
    {
        private readonly IAuditLogService _auditLogService;

        public AuditLogController(IAuditLogService auditLogService)
        {
            _auditLogService = auditLogService;
        }

        /// <summary>分页查询审计日志</summary>
        [HttpGet]
        public ActionResult<PagedResult<AuditLogDto>> List(
            [FromQuery] string? account,
            [FromQuery] string? module,
            [FromQuery] bool? success,
            [FromQuery] DateTime? startTime,
            [FromQuery] DateTime? endTime,
            [FromQuery] int page = 1,
            [FromQuery] int size = 20)
            => Ok(_auditLogService.GetList(account, module, success, startTime, endTime, page, size));

        /// <summary>按日审计操作趋势（默认近 7 天）</summary>
        [HttpGet]
        public ActionResult<SendTrendDto> Trend([FromQuery] int days = 7)
            => Ok(_auditLogService.GetTrend(days));

        /// <summary>按日登录活跃趋势（默认近 7 天，首页数据看板用）</summary>
        [HttpGet]
        public ActionResult<SendTrendDto> LoginTrend([FromQuery] int days = 7)
            => Ok(_auditLogService.GetLoginTrend(days));
    }
}

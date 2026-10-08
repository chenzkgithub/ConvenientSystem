using ConvenientSystem.Api.Auth;
using ConvenientSystem.Shared.Common.Security;
using ConvenientSystem.Shared.Model.Common;
using ConvenientSystem.Service.Common;
using Microsoft.AspNetCore.Mvc;

namespace ConvenientSystem.Api.Controllers.Common
{
    /// <summary>
    /// 手机端仪表盘数据接口（工作台趋势图）。与 PC 端审计日志权限解耦：
    /// PC 的 /Common/AuditLog/* 受管理码 audit-log 保护，手机端用户有无仪表盘数据
    /// 不应由 PC 端审计权限决定，故改走本控制器，仅要求手机端 app-dashboard 权限点
    /// （手机端令牌只含 app-* 码，PC 端 token 无该码自然 403）。
    /// </summary>
    [Area("Common")]
    [PermissionAuthorize(AppPerm.Dashboard)]
    public class MobileDashboardController : BaseController
    {
        private readonly IAuditLogService _auditLogService;

        public MobileDashboardController(IAuditLogService auditLogService)
        {
            _auditLogService = auditLogService;
        }

        /// <summary>按日审计操作趋势（默认近 7 天）</summary>
        [HttpGet]
        public ActionResult<SendTrendDto> Trend([FromQuery] int days = 7)
            => Ok(_auditLogService.GetTrend(days));

        /// <summary>按日登录活跃趋势（默认近 7 天）</summary>
        [HttpGet]
        public ActionResult<SendTrendDto> LoginTrend([FromQuery] int days = 7)
            => Ok(_auditLogService.GetLoginTrend(days));
    }
}

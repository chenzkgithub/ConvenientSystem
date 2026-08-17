using ConvenientSystem.Api.Auth;
using ConvenientSystem.Service.Common;
using Microsoft.AspNetCore.Mvc;

namespace ConvenientSystem.Api.Controllers.Common
{
    /// <summary>
    /// 系统运行状态大盘接口
    /// </summary>
    [Area("Common")]
    [PermissionAuthorize("system-dashboard")]
    public class SystemDashboardController : BaseController
    {
        private readonly ISystemDashboardService _dashboardService;

        public SystemDashboardController(ISystemDashboardService dashboardService)
        {
            _dashboardService = dashboardService;
        }

        /// <summary>获取系统运行状态</summary>
        [HttpGet]
        public IActionResult GetDashboard() => Ok(_dashboardService.GetDashboard());
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ConvenientSystem.Api.Controllers
{
    /// <summary>
    /// 匿名健康检查端点（固定路由 /api/health）：
    /// 部署流程（DeployService）在容器/服务重启后 curl 本地址验证新版本是否存活，
    /// 必须匿名（探测不带 JWT）且不能挂在 Area 路由下（与部署侧健康检查 URL 硬约定对齐）。
    /// </summary>
    [Route("api/health")]
    [AllowAnonymous]
    public class HealthController : ControllerBase
    {
        /// <summary>存活探测：返回 200 即视为服务已启动。</summary>
        [HttpGet]
        public IActionResult Get()
            => Ok(new { status = "ok", time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") });
    }
}

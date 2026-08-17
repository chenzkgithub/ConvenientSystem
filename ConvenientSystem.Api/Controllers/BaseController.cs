using System.Security.Claims;
using ConvenientSystem.Shared.Common.Security;
using Microsoft.AspNetCore.Mvc;

namespace ConvenientSystem.Api.Controllers
{
    /// <summary>
    /// 控制器基类：统一约定 area 路由模板 api/[area]/[controller]/[action]。
    /// 各控制器只需标注 [Area("模块名")] 并继承本类，路由段由方法名自动生成，
    /// 无需在 [HttpGet]/[HttpPost] 上重复书写路径。
    /// 同时暴露从 JWT 读取的当前用户信息（用户 Id）。
    /// </summary>
    [ApiController]
    [Route("api/[area]/[controller]/[action]")]
    public abstract class BaseController : ControllerBase
    {
        /// <summary>当前登录用户 Id（GUID）；未登录或无 userId claim 时为 null。</summary>
        protected Guid? CurrentUserId
        {
            get
            {
                var raw = User.FindFirst(JwtHelper.UserIdClaim)?.Value
                          ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                return Guid.TryParse(raw, out var id) && id != Guid.Empty ? id : null;
            }
        }

    }
}

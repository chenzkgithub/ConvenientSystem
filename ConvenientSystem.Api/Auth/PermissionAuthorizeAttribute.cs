using ConvenientSystem.Shared.Common.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ConvenientSystem.Api.Auth
{
    /// <summary>
    /// 接口级鉴权：标注在控制器或 Action 上，要求当前用户拥有指定菜单权限码（菜单 Name）。
    /// 未登录返回 401；已登录但无权返回 403。
    /// 权限码来源于 JWT 的 menuCodes claim（登录时按用户角色的可见菜单生成）。
    /// 所有角色（包括管理员）均按配置的菜单权限校验，不做特殊放行。
    /// Action 或控制器上标注 [AllowAnonymous] 时跳过鉴权，允许外部匿名访问。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
    public sealed class PermissionAuthorizeAttribute : Attribute, IAuthorizationFilter
    {
        private readonly string _menuCode;

        public PermissionAuthorizeAttribute(string menuCode)
        {
            _menuCode = menuCode;
        }

        public void OnAuthorization(AuthorizationFilterContext context)
        {
            // [AllowAnonymous] 优先：Action 或 Controller 上标注时跳过鉴权
            if (context.ActionDescriptor.EndpointMetadata.OfType<AllowAnonymousAttribute>().Any())
                return;

            var user = context.HttpContext.User;
            if (user?.Identity?.IsAuthenticated != true)
            {
                context.Result = new UnauthorizedResult();
                return;
            }

            var codes = user.FindFirst(JwtHelper.MenuCodesClaim)?.Value ?? string.Empty;
            var granted = codes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (!granted.Contains(_menuCode, StringComparer.OrdinalIgnoreCase))
            {
                context.Result = new ObjectResult(new { message = "无权访问该功能" })
                {
                    StatusCode = StatusCodes.Status403Forbidden
                };
            }
        }
    }
}

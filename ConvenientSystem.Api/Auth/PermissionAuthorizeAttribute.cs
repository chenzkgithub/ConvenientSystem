using ConvenientSystem.Shared.Common.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ConvenientSystem.Api.Auth
{
    /// <summary>
    /// 接口级鉴权：标注在控制器或 Action 上，要求当前用户拥有指定权限码（菜单 Name 或视图权限点 Name）。
    /// 支持传入多个码，用户拥有其中任意一个即放行（OR 语义）。
    /// 标注在类上控制页面访问权限；标注在方法上控制按钮/操作级权限。
    /// 类级与方法级同时存在时，类级先执行，若已返回 403 则方法级短路跳过。
    /// 未登录返回 401；已登录但无权返回 403。
    /// 权限码来源于 JWT 的 menuCodes claim（含菜单码与视图权限码）。
    /// 所有角色（包括管理员）均按配置的权限校验，不做特殊放行。
    /// Action 或控制器上标注 [AllowAnonymous] 时跳过鉴权，允许外部匿名访问。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
    public sealed class PermissionAuthorizeAttribute : Attribute, IAuthorizationFilter
    {
        private readonly string[] _menuCodes;

        /// <param name="menuCodes">权限码，用户拥有其中任意一个即放行。</param>
        public PermissionAuthorizeAttribute(params string[] menuCodes)
        {
            _menuCodes = menuCodes;
        }

        public void OnAuthorization(AuthorizationFilterContext context)
        {
            // 若前一个过滤器（如类级 PermissionAuthorize）已返回失败结果，短路跳过
            if (context.Result != null) return;

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
            if (!_menuCodes.Any(c => granted.Contains(c, StringComparer.OrdinalIgnoreCase)))
            {
                context.Result = new ObjectResult(new { message = "无权访问该功能" })
                {
                    StatusCode = StatusCodes.Status403Forbidden
                };
            }
        }
    }
}

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
    /// 权限码来源于 JWT 的 menuCodes claim（含菜单码与视图权限码；手机端 app 令牌为 app-* 码）。
    /// 所有角色（包括管理员）均按配置的权限校验，不做特殊放行。
    /// Action 或控制器上标注 [AllowAnonymous] 时跳过鉴权，允许外部匿名访问。
    /// 平台相关属性（两端权限独立）：
    /// - Platform：仅当请求来自该平台（JWT platform claim，app=手机端）时才校验权限码，其他平台跳过码校验
    ///   （仍要求已登录）。用于“手机端专属码 + PC 端不限制”的共用接口（如聊天/通知）。
    /// - AppCode：请求来自手机端时改用该码校验，Web 端仍用构造函数传入的码。用于两端共用但权限码不同的接口
    ///   （如彩票：web 用菜单码 lottery，app 用 app-lottery）。
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

        /// <summary>平台限定（可选）：仅当请求来自该平台（app=手机端）时才校验权限码；其他平台跳过码校验（仍要求已登录）。</summary>
        public string? Platform { get; set; }

        /// <summary>手机端权限码（可选）：请求来自手机端（platform=app）时改用该码校验；Web 端仍用构造函数传入的码。</summary>
        public string? AppCode { get; set; }

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

            // 请求平台：JWT platform claim（升级前的旧令牌缺省按 web）
            var requestPlatform = user.FindFirst(JwtHelper.PlatformClaim)?.Value ?? ClientPlatform.Web;
            var isApp = string.Equals(requestPlatform, ClientPlatform.App, StringComparison.OrdinalIgnoreCase);

            // Platform 限定：非目标平台跳过码校验（登录态仍由认证体系保证）
            if (!string.IsNullOrEmpty(Platform)
                && !string.Equals(requestPlatform, Platform, StringComparison.OrdinalIgnoreCase))
                return;

            // 码选择：手机端请求优先用 AppCode（未配置则与 Web 端共用构造码）
            var requiredCodes = isApp && !string.IsNullOrEmpty(AppCode) ? new[] { AppCode } : _menuCodes;

            var codes = user.FindFirst(JwtHelper.MenuCodesClaim)?.Value ?? string.Empty;
            var granted = codes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (!requiredCodes.Any(c => granted.Contains(c, StringComparer.OrdinalIgnoreCase)))
            {
                context.Result = new ObjectResult(new { message = "无权访问该功能" })
                {
                    StatusCode = StatusCodes.Status403Forbidden
                };
            }
        }
    }
}

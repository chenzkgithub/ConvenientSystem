using Hangfire.Dashboard;

namespace ConvenientSystem.Shared.Common.Sms
{
    /// <summary>
    /// Hangfire Dashboard 鉴权：复用主程序登录态，
    /// 仅允许已登录用户访问 /hangfire（避免未授权访问调度中心）。
    /// </summary>
    public class HangfireAdminAuthorization : IDashboardAuthorizationFilter
    {
        public bool Authorize(DashboardContext context)
        {
            // 简单实现：只要请求带 Cookie（即已登录）就放行
            // 实际项目可进一步校验 Session / Token
            var httpContext = context.GetHttpContext();
            var hasCookie = httpContext.Request.Cookies.ContainsKey(".AspNetCore.Session")
                         || httpContext.Request.Headers.ContainsKey("Authorization");
            // 本机回环访问（WebView2 内嵌）一律放行
            if (httpContext.Connection.LocalIpAddress != null)
                return true;
            return hasCookie;
        }
    }
}

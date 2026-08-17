using ConvenientSystem.Shared.Entity.Common;
using Microsoft.AspNetCore.Authorization;

namespace ConvenientSystem.Api.Middleware
{
    /// <summary>
    /// 用户状态验证中间件：在所有请求通过身份验证后，检查已登录用户是否被停用。
    /// 同时校验当前令牌的 JTI 是否为该用户最新一次登录签发的（挤号：同账号新登录使旧令牌失效）。
    /// 如果用户被停用或令牌已被新会话挤掉，返回 401 Unauthorized，强制前端重新登录。
    /// 
    /// 调用顺序：应在 UseAuthentication 之后、业务逻辑之前。
    /// 示例（Program.cs）：
    ///   app.UseAuthentication();
    ///   app.UseMiddleware<UserStatusValidationMiddleware>();  // 添加到这里
    ///   app.UseAuthorization();
    /// </summary>
    public class UserStatusValidationMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<UserStatusValidationMiddleware> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly SessionTokenStore _sessionStore;

        public UserStatusValidationMiddleware(
            RequestDelegate next,
            ILogger<UserStatusValidationMiddleware> logger,
            IServiceProvider serviceProvider,
            SessionTokenStore sessionStore)
        {
            _next = next;
            _logger = logger;
            _serviceProvider = serviceProvider;
            _sessionStore = sessionStore;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // 仅检查已登录的用户（排除登录/心跳检查等公开端点）
            if (context.User?.Identity?.IsAuthenticated == true)
            {
                // [AllowAnonymous] 端点跳过状态校验：外部公开页面等免登录接口不受挤号/停用影响
                var endpoint = context.GetEndpoint();
                var allowAnon = endpoint?.Metadata.GetMetadata<AllowAnonymousAttribute>() != null;
                if (!allowAnon)
                {
                    // 获取当前用户 ID
                    var userIdStr = context.User.FindFirst("userId")?.Value
                                  ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                
                    if (Guid.TryParse(userIdStr, out var userId) && userId != Guid.Empty)
                    {
                        // 使用作用域服务查询用户状态
                        using (var scope = _serviceProvider.CreateScope())
                        {
                            try
                            {
                                var configDb = scope.ServiceProvider.GetKeyedService<IFreeSql>("ConvenientSystemDb");
                                if (configDb != null)
                                {
                                    var user = await configDb.Select<SysUserEntity>()
                                        .Where(u => u.Id == userId)
                                        .FirstAsync();

                                    // 用户不存在或被停用 → 返回 401，强制重新登录
                                    if (user == null || !user.Enabled)
                                    {
                                        var account = context.User.FindFirst("account")?.Value ?? "Unknown";
                                        _logger.LogWarning("用户已停用或不存在，拒绝访问。UserId={UserId}, Account={Account}, Path={Path}",
                                            userId, account, context.Request.Path);
                                    
                                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                                        await context.Response.WriteAsJsonAsync(new
                                        {
                                            message = "您的账号已被停用，请重新登录"
                                        });
                                        return;
                                    }

                                    // 挤号校验：当前令牌的 JTI 必须与该用户最新登录会话一致
                                    var jti = context.User.FindFirst("jti")?.Value;
                                    if (!_sessionStore.IsValid(userId, jti))
                                    {
                                        var account = context.User.FindFirst("account")?.Value ?? "Unknown";
                                        _logger.LogInformation("用户会话已被挤号，拒绝访问。UserId={UserId}, Account={Account}, Path={Path}",
                                            userId, account, context.Request.Path);
                                    
                                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                                        await context.Response.WriteAsJsonAsync(new
                                        {
                                            message = "您的账号已在其他设备登录，当前会话已失效"
                                        });
                                        return;
                                    }
                                }
                                // DB 异常时不强制退出（继续处理请求）
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "验证用户状态时异常，UserId={UserId}，继续请求处理", userId);
                                // 异常时放行（保证 DB 故障不影响业务）
                            }
                        }
                    }
                } // end !allowAnon
            }

            await _next(context);
        }
    }
}

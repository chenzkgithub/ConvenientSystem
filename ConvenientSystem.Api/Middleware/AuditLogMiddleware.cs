using System.Diagnostics;
using System.Security.Claims;
using System.Text;
using ConvenientSystem.Shared.Common.Audit;
using ConvenientSystem.Shared.Entity.Common;

namespace ConvenientSystem.Api.Middleware
{
    /// <summary>
    /// 操作审计中间件：仅拦截以 /api 开头的写操作（POST/PUT/DELETE），
    /// 记录操作人、路径、请求体摘要、状态码与耗时，异步入队落库。
    /// 从 JWT claims 取操作人（阶段三接入认证后自然生效，未登录时留空）。
    /// 放在 UseAuthentication/UseAuthorization 之后，以便读取到用户身份。
    /// </summary>
    public class AuditLogMiddleware
    {
        private const int MaxParamLength = 2000;
        private readonly RequestDelegate _next;
        private readonly AuditLogQueue _queue;

        public AuditLogMiddleware(RequestDelegate next, AuditLogQueue queue)
        {
            _next = next;
            _queue = queue;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (!ShouldAudit(context.Request))
            {
                await _next(context);
                return;
            }

            var paramSummary = await ReadBodySummaryAsync(context.Request);
            var sw = Stopwatch.StartNew();
            try
            {
                await _next(context);
            }
            finally
            {
                sw.Stop();
                TryEnqueue(context, paramSummary, sw.ElapsedMilliseconds);
            }
        }

        /// <summary>仅审计 /api 下的写操作。</summary>
        private static bool ShouldAudit(HttpRequest request)
        {
            if (!request.Path.HasValue || !request.Path.Value!.StartsWith("/api", StringComparison.OrdinalIgnoreCase))
                return false;
            var m = request.Method;
            return HttpMethods.IsPost(m) || HttpMethods.IsPut(m) || HttpMethods.IsDelete(m);
        }

        /// <summary>读取请求体摘要（截断），需在读取前开启缓冲以便后续管道再次读取。</summary>
        private static async Task<string?> ReadBodySummaryAsync(HttpRequest request)
        {
            try
            {
                request.EnableBuffering();
                if (request.Body.CanSeek) request.Body.Position = 0;
                using var reader = new StreamReader(request.Body, Encoding.UTF8, false, 1024, leaveOpen: true);
                var body = await reader.ReadToEndAsync();
                if (request.Body.CanSeek) request.Body.Position = 0;
                if (string.IsNullOrEmpty(body)) return null;
                return body.Length > MaxParamLength ? body.Substring(0, MaxParamLength) : body;
            }
            catch
            {
                return null;
            }
        }

        private void TryEnqueue(HttpContext context, string? paramSummary, long costMs)
        {
            try
            {
                var user = context.User;
                Guid? userId = null;
                var account = string.Empty;
                if (user?.Identity?.IsAuthenticated == true)
                {
                    var idStr = user.FindFirst("userId")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                    if (Guid.TryParse(idStr, out var uid) && uid != Guid.Empty) userId = uid;
                    account = user.FindFirst("account")?.Value ?? user.Identity.Name ?? string.Empty;
                }

                var path = context.Request.Path.HasValue ? context.Request.Path.Value! : string.Empty;
                var status = context.Response.StatusCode;
                _queue.Enqueue(new SysAuditLogEntity
                {
                    UserId = userId,
                    Account = account,
                    Action = ResolveAction(context),
                    Module = ResolveModule(path),
                    Path = path,
                    Method = context.Request.Method,
                    Ip = context.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
                    ParamSummary = paramSummary,
                    Success = status < 400,
                    StatusCode = status,
                    CostMs = (int)Math.Min(costMs, int.MaxValue)
                });
            }
            catch
            {
                // 审计失败绝不影响主流程
            }
        }

        /// <summary>操作描述：取 controller/action 路由段（约定 api/{area}/{controller}/{action}）。</summary>
        private static string ResolveAction(HttpContext context)
        {
            var rv = context.GetRouteData()?.Values;
            if (rv != null && rv.Count > 0)
            {
                var controller = rv.TryGetValue("controller", out var c) ? c?.ToString() : null;
                var action = rv.TryGetValue("action", out var a) ? a?.ToString() : null;
                if (!string.IsNullOrEmpty(controller) || !string.IsNullOrEmpty(action))
                    return $"{controller}/{action}".Trim('/');
            }
            return context.Request.Path.Value ?? string.Empty;
        }

        /// <summary>所属模块：取 api/{area} 的 area 段。</summary>
        private static string ResolveModule(string path)
        {
            // path 形如 /api/Sms/SmsTask/Create
            var segs = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            return segs.Length >= 2 ? segs[1] : string.Empty;
        }
    }
}

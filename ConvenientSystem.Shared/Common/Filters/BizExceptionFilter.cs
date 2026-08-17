using System.Security.Claims;
using ConvenientSystem.Shared.Common.Exceptions;
using ConvenientSystem.Shared.Entity.Common;
using FreeSql;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ConvenientSystem.Shared.Common.Filters
{
    /// <summary>
    /// 全局异常过滤器：把 Service 层抛出的异常统一转换为 { message } 响应体，
    /// 与前端 request.ts 中 handle() 读取 body.message 的约定保持一致。
    /// 未处理异常（非 BizException / 非客户端中断）同时写入 SysErrorLog 表。
    /// </summary>
    public sealed class BizExceptionFilter : IExceptionFilter
    {
        /// <summary>客户端主动中断请求时的状态码（沿用重构前 SQL 查询工具的约定）</summary>
        private const int ClientClosedRequest = 499;

        private const int MaxErrorMessageLength = 2000;
        private const int MaxStackTraceLength = 8000;

        private readonly ILogger<BizExceptionFilter> _logger;
        private readonly IFreeSql _configDb;

        public BizExceptionFilter(
            ILogger<BizExceptionFilter> logger,
            [FromKeyedServices("ConvenientSystemDb")] IFreeSql configDb)
        {
            _logger = logger;
            _configDb = configDb;
        }

        public void OnException(ExceptionContext context)
        {
            switch (context.Exception)
            {
                // 业务异常：状态码与消息均由 Service 层指定，属预期结果，只记 Debug
                case BizException biz:
                    _logger.LogDebug("业务异常（{StatusCode}）：{Message}", biz.StatusCode, biz.Message);
                    context.Result = Build(biz.Message, biz.StatusCode, biz.Extras);
                    break;

                // 客户端中断（前端 abort 请求）：不算服务端故障
                case OperationCanceledException:
                    _logger.LogInformation("请求已被客户端取消：{Path}", context.HttpContext.Request.Path);
                    context.Result = Build("操作已取消", ClientClosedRequest);
                    break;

                default:
                    _logger.LogError(context.Exception, "未处理异常：{Path}", context.HttpContext.Request.Path);
                    TryWriteErrorLog(context, StatusCodes.Status500InternalServerError);
                    context.Result = Build("服务端异常：" + context.Exception.Message, StatusCodes.Status500InternalServerError);
                    break;
            }
            context.ExceptionHandled = true;
        }

        /// <summary>
        /// 将未处理异常写入 SysErrorLog 表（失败不影响主流程）。
        /// </summary>
        private void TryWriteErrorLog(ExceptionContext context, int statusCode)
        {
            try
            {
                var ctx = context.HttpContext;
                var user = ctx.User;
                var account = string.Empty;
                Guid? userId = null;
                if (user?.Identity?.IsAuthenticated == true)
                {
                    account = user.FindFirst("account")?.Value ?? user.Identity.Name ?? string.Empty;
                    var idStr = user.FindFirst("userId")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                    if (Guid.TryParse(idStr, out var uid) && uid != Guid.Empty) userId = uid;
                }

                var ex = context.Exception;
                var msg = ex.Message ?? string.Empty;
                if (msg.Length > MaxErrorMessageLength) msg = msg[..MaxErrorMessageLength];
                var stack = ex.StackTrace;
                if (!string.IsNullOrEmpty(stack) && stack.Length > MaxStackTraceLength)
                    stack = stack[..MaxStackTraceLength];

                _configDb.Insert(new SysErrorLogEntity
                {
                    UserId = userId,
                    Account = account,
                    Path = ctx.Request.Path.Value ?? string.Empty,
                    Method = ctx.Request.Method,
                    StatusCode = statusCode,
                    ExceptionType = ex.GetType().FullName ?? ex.GetType().Name,
                    ErrorMessage = msg,
                    StackTrace = stack,
                    Ip = ctx.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
                }).ExecuteAffrows();
            }
            catch (Exception logEx)
            {
                _logger.LogWarning(logEx, "写入 SysErrorLog 失败，不影响主流程");
            }
        }

        private static ObjectResult Build(string message, int statusCode, IReadOnlyDictionary<string, object?>? extras = null)
        {
            if (extras == null || extras.Count == 0)
                return new(new { message }) { StatusCode = statusCode };
            // 附加字段与 message 平级输出（message 不允许被附加字段覆盖）
            var body = new Dictionary<string, object?>(extras) { ["message"] = message };
            return new(body) { StatusCode = statusCode };
        }
    }
}

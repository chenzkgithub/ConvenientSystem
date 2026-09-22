using System.Text;

namespace ConvenientSystem;

/// <summary>
/// 宿主标识注入中间件：桌面端宿主在 index.html 的 head 中注入 cs-host meta，
/// 前端 hostContext 据此判定运行环境（替代不可靠的 UA/webview 探测）。
/// 仅拦截 GET / 与 GET /index.html 的 HTML 响应，其余请求零开销透传。
/// </summary>
internal sealed class HostKindInjectionMiddleware
{
    private const string MetaTag = "<meta name=\"cs-host\" content=\"desktop\">";
    private readonly RequestDelegate _next;

    public HostKindInjectionMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        var isIndex = HttpMethods.IsGet(context.Request.Method)
            && (path == "/" || string.Equals(path, "/index.html", StringComparison.OrdinalIgnoreCase));
        if (!isIndex)
        {
            await _next(context);
            return;
        }

        var originalBody = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        try
        {
            await _next(context);

            var contentType = context.Response.Headers.ContentType.ToString();
            var isPlainHtml = context.Response.StatusCode == StatusCodes.Status200OK
                && contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrEmpty(context.Response.Headers.ContentEncoding);
            if (isPlainHtml)
            {
                buffer.Seek(0, SeekOrigin.Begin);
                var html = await new StreamReader(buffer, Encoding.UTF8).ReadToEndAsync();
                var markerIndex = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
                if (markerIndex >= 0 && !html.Contains("cs-host"))
                {
                    html = html.Insert(markerIndex, MetaTag + "\n  ");
                    var bytes = Encoding.UTF8.GetBytes(html);
                    context.Response.ContentLength = bytes.Length;
                    context.Response.Body = originalBody;
                    await context.Response.Body.WriteAsync(bytes, context.RequestAborted);
                    return;
                }
            }

            // 非 HTML / 无需注入：把缓冲内容原样写回真实响应流
            buffer.Seek(0, SeekOrigin.Begin);
            context.Response.Body = originalBody;
            await buffer.CopyToAsync(originalBody, context.RequestAborted);
        }
        finally
        {
            context.Response.Body = originalBody;
        }
    }
}

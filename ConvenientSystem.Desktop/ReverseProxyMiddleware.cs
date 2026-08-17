using System.Text;

namespace ConvenientSystem;

/// <summary>
/// 反向代理中间件：将本机 API 请求转发到远程服务器，用于"后端部署服务器 + 本地 exe 走代理"模式。
/// 仅当 AppSettings:RemoteServerUrl 非空时生效（格式：IP:端口，如 127.0.0.1:51943）；
/// 为空时直接放行，行为与未添加本中间件一致。
/// </summary>
internal sealed class ReverseProxyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly HttpClient _httpClient;
    private readonly string _remoteBaseUrl;

    // 逐跳（hop-by-hop）头：由当前连接产生，不应转发给上游。
    private static readonly HashSet<string> SkipRequestHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection", "Keep-Alive", "Transfer-Encoding", "TE", "Trailer",
        "Upgrade", "Proxy-Authorization", "Proxy-Authenticate",
        "Host",   // 必须重写为目标主机
        "Origin", // 跨域头：代理场景下不应暴露给上游
    };

    private static readonly HashSet<string> SkipResponseHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Transfer-Encoding", "Connection", "Keep-Alive",
        "Server", "Date",  // 由本机 Kestrel 自动填充
    };

    public ReverseProxyMiddleware(
        RequestDelegate next,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _next = next;
        // 从配置项 RemoteServerUrl（格式：IP:端口）拼接远程服务地址
        var url = (configuration["AppSettings:RemoteServerUrl"] ?? string.Empty).Trim();
        _remoteBaseUrl = !string.IsNullOrEmpty(url) ? $"http://{url.TrimEnd('/')}" : string.Empty;
        _httpClient = httpClientFactory.CreateClient("ReverseProxy");
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // 未配置远程地址 → 本地模式，直接走本机控制器。
        if (string.IsNullOrEmpty(_remoteBaseUrl))
        {
            await _next(context);
            return;
        }

        var targetUri = _remoteBaseUrl + context.Request.Path + context.Request.QueryString;

        try
        {
            using var request = new HttpRequestMessage(
                new HttpMethod(context.Request.Method), targetUri);

            // 转发请求头（跳过逐跳头与 Host/Origin）
            foreach (var header in context.Request.Headers)
            {
                if (SkipRequestHeaders.Contains(header.Key)) continue;
                request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }

            // 转发请求体（POST/PUT/PATCH 等）
            if (context.Request.ContentLength is > 0)
            {
                request.Content = new StreamContent(context.Request.Body);
                if (context.Request.ContentType is { Length: > 0 } ct)
                    request.Content.Headers.ContentType =
                        System.Net.Http.Headers.MediaTypeHeaderValue.Parse(ct);
            }

            using var response = await _httpClient.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);

            context.Response.StatusCode = (int)response.StatusCode;

            // 转发响应头
            foreach (var header in response.Headers)
            {
                if (SkipResponseHeaders.Contains(header.Key)) continue;
                context.Response.Headers.Append(header.Key, header.Value.ToArray());
            }
            foreach (var header in response.Content.Headers)
            {
                if (SkipResponseHeaders.Contains(header.Key)) continue;
                context.Response.Headers.Append(header.Key, header.Value.ToArray());
            }

            // 流式转发响应体（避免整块加载到内存）
            context.Response.ContentLength = response.Content.Headers.ContentLength;
            await using var responseStream = await response.Content.ReadAsStreamAsync(context.RequestAborted);
            await responseStream.CopyToAsync(context.Response.Body, context.RequestAborted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var logger = context.RequestServices
                .GetRequiredService<ILogger<ReverseProxyMiddleware>>();
            logger.LogError(ex, "反向代理转发失败：{TargetUri}", targetUri);
            context.Response.StatusCode = 502;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    message = $"远程服务器连接失败：{ex.Message}"
                }), context.RequestAborted);
        }
    }
}

using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using Microsoft.Extensions.Configuration;

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

    // 配置了内网考勤数据库连接串时，考勤路径由本地控制器处理，不转发到云。
    private readonly bool _hasLocalAttendance;

    public ReverseProxyMiddleware(
        RequestDelegate next,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        IWritableOptions<AppSettings> appSettings)
    {
        _next = next;
        // 从配置项 RemoteServerUrl（格式：IP:端口）拼接远程服务地址
        var url = appSettings.Value.RemoteServerUrl.Trim();
        _remoteBaseUrl = !string.IsNullOrEmpty(url) ? $"http://{url.TrimEnd('/')}" : string.Empty;
        _httpClient = httpClientFactory.CreateClient("ReverseProxy");
        // 当配置了 YhSystemDb 时，考勤请求走本地控制器
        _hasLocalAttendance = !string.IsNullOrWhiteSpace(configuration.GetConnectionString("YhSystemDb"));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // 未配置远程地址 → 本地模式，直接走本机控制器。
        if (string.IsNullOrEmpty(_remoteBaseUrl))
        {
            await _next(context);
            return;
        }

        // WebSocket 请求（SignalR 实时通道）：双工流转发到远程服务器
        if (context.WebSockets.IsWebSocketRequest &&
            context.Request.Path.StartsWithSegments("/hubs", StringComparison.OrdinalIgnoreCase))
        {
            await ProxyWebSocketAsync(context);
            return;
        }

        // 接口分离：新前缀 /api/local/* 一律视为本地接口（对应桌面端各控制器的 api/local 路由）
        if (context.Request.Path.StartsWithSegments("/api/local", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // 考勤路径走本地控制器（当配置了内网数据库时）
        if (_hasLocalAttendance &&
            context.Request.Path.StartsWithSegments("/api/YunHan/Attendance", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // 本机监控路径始终由本地控制器处理，不转发到云
        if (context.Request.Path.StartsWithSegments("/api/Common/Monitor", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // 本地构建与发布控制器不走反向代理
        // Git 代码管理工作台操作本机仓库（git 命令在本机执行），同样必须走本地控制器
        // UiState：前端 UI 状态持久化（模板/卡片配置存 exe 目录 ui-state.json，不能转发到云）
        // Pipeline：流水线引擎在本机执行构建/部署（依赖本机服务与 SSH 凭据），不能转发到云
        // WebUpdate：页面内「发现新版本」提示条调用的本地热更新接口（替换本机 wwwroot），不能转发到云
        // ApiSpec：API 文档生成器扫描用户本机 C# 源码，云端容器读不到本机路径，必须走本地控制器
        // ConfigEditor：配置文件热编辑操作本机/服务器本地文件（exe 编辑安装目录、服务器编辑服务器目录），不能转发到云
        // CodeScan：代码扫描读取本机文件系统，转发到云后路径不存在，必须走本地控制器
        // Hosts：读写本机 hosts 文件（操作的是用户电脑，云端执行无意义），不能转发到云
        // AsyncTask：统一异步任务查询本机 center（ApiSpec 扫描/生成）；Apifox 云端任务轮询走 ApifoxConfig 路径转发到云
        if (context.Request.Path.StartsWithSegments("/api/Common/Build", StringComparison.OrdinalIgnoreCase)
            || context.Request.Path.StartsWithSegments("/api/Common/UniversalBuild", StringComparison.OrdinalIgnoreCase)
            || context.Request.Path.StartsWithSegments("/api/Common/UiState", StringComparison.OrdinalIgnoreCase)
            || context.Request.Path.StartsWithSegments("/api/Common/Pipeline", StringComparison.OrdinalIgnoreCase)
            || context.Request.Path.StartsWithSegments("/api/Common/WebUpdate", StringComparison.OrdinalIgnoreCase)
            || context.Request.Path.StartsWithSegments("/api/Common/Git", StringComparison.OrdinalIgnoreCase)
            || context.Request.Path.StartsWithSegments("/api/Common/ApiSpec", StringComparison.OrdinalIgnoreCase)
            || context.Request.Path.StartsWithSegments("/api/Common/ConfigEditor", StringComparison.OrdinalIgnoreCase)
            || context.Request.Path.StartsWithSegments("/api/Common/CodeScan", StringComparison.OrdinalIgnoreCase)
            || context.Request.Path.StartsWithSegments("/api/Common/Hosts", StringComparison.OrdinalIgnoreCase)
            || context.Request.Path.StartsWithSegments("/api/Common/AsyncTask", StringComparison.OrdinalIgnoreCase))
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

    /// <summary>
    /// WebSocket 双工转发：接受客户端连接，同时用 ClientWebSocket 连远程，双向透传帧。
    /// SignalR 聊天 Hub 依赖此通道实现毫秒级实时推送；不转发则前端降级为 REST 轮询。
    /// </summary>
    private async Task ProxyWebSocketAsync(HttpContext context)
    {
        var logger = context.RequestServices.GetRequiredService<ILogger<ReverseProxyMiddleware>>();

        // 1. 接受客户端 WebSocket 连接
        using var clientWs = await context.WebSockets.AcceptWebSocketAsync();

        // 2. 构建远程 WebSocket URL（http→ws，https→wss）
        var wsScheme = _remoteBaseUrl.StartsWith("https", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws";
        var httpPath = _remoteBaseUrl.Replace("https://", "").Replace("http://", "");
        var remoteWsUrl = $"{wsScheme}://{httpPath}{context.Request.Path}{context.Request.QueryString}";

        // 3. 连接远程 WebSocket（转发必要的请求头）
        using var remoteWs = new ClientWebSocket();
        foreach (var header in context.Request.Headers)
        {
            if (SkipRequestHeaders.Contains(header.Key)) continue;
            try { remoteWs.Options.SetRequestHeader(header.Key, header.Value.ToString()); }
            catch { /* 部分头不允许设置（如 Content-Length），忽略 */ }
        }

        try
        {
            await remoteWs.ConnectAsync(new Uri(remoteWsUrl), context.RequestAborted);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "WebSocket 连接远程失败：{Url}", remoteWsUrl);
            await clientWs.CloseAsync(WebSocketCloseStatus.InternalServerError,
                "Remote connection failed", CancellationToken.None);
            return;
        }

        // 4. 双向转发（任一方关闭或取消时结束）
        var cts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        try
        {
            var clientToRemote = ForwardAsync(clientWs, remoteWs, cts.Token);
            var remoteToClient = ForwardAsync(remoteWs, clientWs, cts.Token);
            await Task.WhenAny(clientToRemote, remoteToClient);
        }
        finally
        {
            await cts.CancelAsync();
            // 确保双方都关闭
            if (clientWs.State == WebSocketState.Open || clientWs.State == WebSocketState.CloseReceived)
            {
                try { await clientWs.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); }
                catch { /* 可能已断开 */ }
            }
            if (remoteWs.State == WebSocketState.Open || remoteWs.State == WebSocketState.CloseReceived)
            {
                try { await remoteWs.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); }
                catch { /* 可能已断开 */ }
            }
        }
    }

    /// <summary>从 source 读帧并转发到 destination，直到 source 关闭或取消。</summary>
    private static async Task ForwardAsync(WebSocket source, WebSocket destination, CancellationToken ct)
    {
        var buffer = new byte[4096];
        while (!ct.IsCancellationRequested && source.State == WebSocketState.Open)
        {
            var result = await source.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close) break;

            if (destination.State == WebSocketState.Open)
            {
                await destination.SendAsync(
                    new ArraySegment<byte>(buffer, 0, result.Count),
                    result.MessageType, result.EndOfMessage, ct);
            }
        }
    }
}

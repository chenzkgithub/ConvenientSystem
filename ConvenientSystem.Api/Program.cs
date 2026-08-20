using ConvenientSystem.Api.Middleware;
using ConvenientSystem.Shared.Common;
using ConvenientSystem.Shared.Common.Email;
using ConvenientSystem.Shared.Common.Sms;
using Hangfire;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using PdfiumViewer.Standard;

namespace ConvenientSystem.Api;

/// <summary>
/// API 接口服务入口：启动 Kestrel，只提供 REST 接口与 Hangfire 面板（不承载静态前端）。
/// 前端静态资源由桌面客户端（ConvenientSystem.Desktop）的 wwwroot 提供，接口请求经其反向代理转发到本服务；
/// 浏览器单独访问前端时同样指向桌面客户端端口。绑定 0.0.0.0 以便局域网其他设备访问。
/// 服务登记全部集中在 <see cref="ServicesExtent"/>。
/// </summary>
internal static class Program
{
    /// <summary>配置文件中的默认端口（AppSettings:ServicePort 未配置时的回退值）。</summary>
    private const int DefaultPort = 51943;

    private static void Main(string[] args)
    {
        // Linux 下启用 System.Drawing（依赖 libgdiplus）；Windows 无此开关不受影响
        AppContext.SetSwitch("System.Drawing.EnableUnixSupport", true);

        // PdfiumViewer.Standard 的 P/Invoke 声明为 [DllImport("pdfium.dll")]，
        // Linux 上需将 pdfium.dll 映射到 libpdfium.so
        if (OperatingSystem.IsLinux())
        {
            NativeLibrary.SetDllImportResolver(
                typeof(PdfDocument).Assembly,
                (libraryName, _, _) =>
                {
                    if (libraryName == "pdfium.dll")
                    {
                        var candidates = new[] { "/app/api/libpdfium.so", "libpdfium.so", "pdfium" };
                        foreach (var p in candidates)
                            if (NativeLibrary.TryLoad(p, out var h))
                                return h;
                    }
                    return IntPtr.Zero;
                });
        }

        var app = BuildWebApp(args);

        // 从配置文件读取端口，未配置则用默认值
        int configuredPort = app.Configuration.GetValue<int?>("AppSettings:ServicePort") ?? DefaultPort;

        app.Urls.Clear();
        int port = IsPortAvailable(configuredPort) ? configuredPort : 0;
        app.Urls.Add($"http://0.0.0.0:{port}");
        try
        {
            app.Start();
        }
        catch
        {
            (app as IDisposable)?.Dispose();
            app = BuildWebApp(args);
            app.Urls.Clear();
            app.Urls.Add("http://0.0.0.0:0");
            app.Start();
        }

        var baseUrl = ResolveBaseUrl(app);
        // 代理地址：桌面客户端 RemoteServerUrl 指向本服务的地址
        var proxyUrl = baseUrl.Replace("0.0.0.0", "127.0.0.1");

        Console.WriteLine("========================================");
        Console.WriteLine("  ConvenientSystem API 接口服务");
        Console.WriteLine($"  服务地址：{baseUrl}");
        Console.WriteLine($"  代理地址：{proxyUrl}");
        Console.WriteLine("  关闭此窗口即停止服务");
        Console.WriteLine("========================================");

        app.WaitForShutdown();
        app.StopAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// 构建并配置 ASP.NET Core 应用（纯接口服务：控制器 + Hangfire，无静态文件中间件）。
    /// </summary>
    private static WebApplication BuildWebApp(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // 全部依赖注入登记（数据库、定时任务、短信/邮件、业务服务）。
        ServicesExtent.ConfigureServices(builder.Services, builder.Configuration);

        var app = builder.Build();

        // JWT 认证/授权：读取 Bearer Token 并填充 User，供审计中间件与接口鉴权特性使用。
        app.UseAuthentication();
        app.UseAuthorization();

        // 用户状态验证：检查已登录用户是否被停用，若是则返回 401 强制重新登录。
        // 必须在 UseAuthentication 之后，以便能读取 JWT 中的用户 ID。
        app.UseMiddleware<UserStatusValidationMiddleware>();

        // 操作审计：仅拦 /api 下的写操作（POST/PUT/DELETE），异步入队落库。
        // 置于 UseAuthentication/UseAuthorization 之后，以读取 JWT 用户身份。
        app.UseMiddleware<AuditLogMiddleware>();

        // 控制器路由：api/{area}/{controller}/{action}（见 Controllers/BaseController.cs）。
        app.MapControllers();

        // Hangfire Dashboard 响应编码修正：确保 Content-Type 含 charset=utf-8，避免中文任务名乱码
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/hangfire"))
            {
                context.Response.OnStarting(() =>
                {
                    var ct = context.Response.ContentType;
                    if (ct != null && ct.StartsWith("text/html") && !ct.Contains("charset"))
                    {
                        context.Response.ContentType = ct + "; charset=utf-8";
                    }
                    return Task.CompletedTask;
                });
            }
            await next();
        });

        // Hangfire Dashboard
        app.UseHangfireDashboard("/hangfire", new DashboardOptions
        {
            Authorization = new[] { new HangfireAdminAuthorization() },
            DashboardTitle = "任务调度中心"
        });

        RunStartupCompensators(app);

        return app;
    }

    /// <summary>
    /// 启动补偿：恢复待执行的短信任务与所有启用的邮件定时任务。
    /// 任一补偿失败只记警告，不阻断服务启动。
    /// </summary>
    private static void RunStartupCompensators(WebApplication app)
    {
        Compensate<SmsStartupCompensator>(app, c => c.Compensate(), "短信启动补偿失败");
        Compensate<EmailStartupCompensator>(app, c => c.Compensate(), "邮件启动补偿失败");
        Compensate<LotteryStartupCompensator>(app, c => c.Compensate(), "大乐透启动补偿失败");
        Compensate<WebMonitorStartupCompensator>(app, c => c.Compensate(), "网站监控启动补偿失败");
        Compensate<HostMonitorStartupCompensator>(app, c => c.Compensate(), "主机监控启动补偿失败");
    }

    private static void Compensate<T>(WebApplication app, Action<T> action, string failMessage) where T : notnull
    {
        using var scope = app.Services.CreateScope();
        var compensator = scope.ServiceProvider.GetRequiredService<T>();
        try
        {
            action(compensator);
        }
        catch (Exception ex)
        {
            var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
            loggerFactory.CreateLogger("ConvenientSystem.Api.Program").LogWarning(ex, failMessage);
        }
    }

    /// <summary>
    /// 读取 Kestrel 实际绑定的地址。
    /// </summary>
    private static string ResolveBaseUrl(WebApplication app)
    {
        var server = app.Services.GetRequiredService<IServer>();
        var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses;
        return addresses?.FirstOrDefault() ?? $"http://0.0.0.0:{DefaultPort}";
    }

    /// <summary>检测指定端口当前是否可绑定（空闲）。</summary>
    private static bool IsPortAvailable(int port)
    {
        try
        {
            var listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch
        {
            return false; // 绑定失败即视为被占用
        }
    }
}

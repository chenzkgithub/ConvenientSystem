using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace ConvenientSystem;

/// <summary>
/// 桌面程序入口：先在本机回环地址启动 Kestrel Web 服务（仅托管静态前端 + 反向代理），
/// 再用 WinForms + WebView2 宿主窗口嵌入显示前端页面。
/// 不包含后端控制器和数据库连接，所有 API 请求通过反向代理转发到独立的 API 服务。
/// </summary>
internal static class Program
{
    // 单实例互斥体名称（全局唯一）：保证同一时间只能运行一个实例。
    private const string SingleInstanceMutexName = "ConvenientSystem.SingleInstance.{9F2C4B1E-7A3D-4C58-9E21-1B6A0D5F3C77}";

    // 固定回环端口：使前端 origin（含端口）每次启动保持一致，
    // 从而 localStorage 登录态能跨"重启/退出程序"保留（localStorage 按 origin 隔离，认端口）。
    // 选用不常用的高端口以降低冲突概率；被占用时回退系统随机端口。
    private const int PreferredLoopbackPort = 51942;

    private const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    [STAThread]
    private static void Main(string[] args)
    {
        // 单实例：通过命名互斥体确保只运行一个实例；若已有实例在跑，则激活已有窗口并退出。
        using var singleInstance = new Mutex(true, SingleInstanceMutexName, out bool createdNew);
        if (!createdNew)
        {
            // 重启接力：新实例带 --restart 参数时，等待旧实例退出释放互斥体后再继续启动；
            // 否则保持"不多开"语义，激活已有窗口后退出。
            if (args.Contains("--restart"))
            {
                try
                {
                    if (!singleInstance.WaitOne(TimeSpan.FromSeconds(15)))
                    {
                        ActivateExistingInstance();
                        return;
                    }
                }
                catch (AbandonedMutexException)
                {
                    // 旧实例异常退出即视为已获得所有权，继续启动。
                }
            }
            else
            {
                ActivateExistingInstance();
                return;
            }
        }

        // 程序启动时自动清理旧的发布产物与编译缓存（obj / bin 目录），
        // 这样每次启动都能保持项目目录干净，无需手动执行 dotnet clean。
        CleanBuildArtifacts();

        var app = BuildWebApp(args);

        // 仅监听本机回环地址（127.0.0.1），不对外暴露服务。
        // 优先使用固定端口，使前端 origin 每次启动保持一致（localStorage 登录态才能跨重启/退出保留）；
        // 若该端口被占用，则回退到系统随机端口（该次会话登录态无法跨重启保留）。
        app.Urls.Clear();
        int port = IsPortAvailable(PreferredLoopbackPort) ? PreferredLoopbackPort : 0;
        app.Urls.Add($"http://127.0.0.1:{port}");
        try
        {
            app.Start();
        }
        catch
        {
            // 固定端口在检测之后被占用（极少见）：回退随机端口重建并启动。
            (app as IDisposable)?.Dispose();
            app = BuildWebApp(args);
            app.Urls.Clear();
            app.Urls.Add("http://127.0.0.1:0");
            app.Start();
        }

        var baseUrl = ResolveBaseUrl(app);

        ApplicationConfiguration.Initialize();
        using (var form = new MainForm(baseUrl))
        {
            Application.Run(form);
        }

        // 窗口关闭后优雅停止 Web 服务。
        app.StopAsync().GetAwaiter().GetResult();

        // 保持互斥体存活至进程退出（防止被 GC 提前回收导致误判为可再开）。
        GC.KeepAlive(singleInstance);
    }

    /// <summary>
    /// 再次启动时，找到已运行实例的主窗口并激活到前台（若最小化则先还原）。
    /// </summary>
    private static void ActivateExistingInstance()
    {
        try
        {
            var current = Process.GetCurrentProcess();
            foreach (var p in Process.GetProcessesByName(current.ProcessName))
            {
                if (p.Id == current.Id) continue;
                var h = p.MainWindowHandle;
                if (h != IntPtr.Zero)
                {
                    if (IsIconic(h)) ShowWindow(h, SW_RESTORE);
                    SetForegroundWindow(h);
                    break;
                }
            }
        }
        catch
        {
            // 激活失败时忽略（不影响"不多开"本身）
        }
    }

    /// <summary>
    /// 构建并配置 ASP.NET Core 应用（瘦客户端：静态文件 + 反向代理，不注册控制器、不连数据库）。
    /// </summary>
    private static WebApplication BuildWebApp(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // 反向代理 HttpClient
        builder.Services.AddHttpClient("ReverseProxy", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(5);
        });

        var app = builder.Build();

        // 默认文件 + 静态文件：让 "/" 直接返回 wwwroot/index.html。
        app.UseDefaultFiles();
        // 对 index.html（及所有 .html）禁用缓存：入口文件名固定，若被 WebView2 缓存，
        // 发布新前端后仍会加载旧 index.html → 引用旧哈希 JS → 调用已删除的旧接口路由而报错。
        // 带哈希的 assets 内容变更即文件名变更，可安全长期缓存，无需特殊处理。
        app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = ctx =>
            {
                var path = ctx.File.Name;
                if (path.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                {
                    var headers = ctx.Context.Response.Headers;
                    headers.CacheControl = "no-cache, no-store, must-revalidate";
                    headers.Pragma = "no-cache";
                    headers.Expires = "0";
                }
            }
        });

        // ========== 反向代理中间件：将 API 请求转发到独立的 API 服务 ==========
        app.UseMiddleware<ReverseProxyMiddleware>();

        return app;
    }

    /// <summary>
    /// 清理项目目录下的 obj / bin 编译缓存。
    /// 程序从 publish/ 目录运行，obj / bin 不被占用，可安全删除。
    /// </summary>
    private static void CleanBuildArtifacts()
    {
        var exeDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var projectDir = Directory.Exists(Path.Combine(exeDir, "ConvenientSystem"))
            ? Path.Combine(exeDir, "ConvenientSystem")
            : exeDir;

        if (Path.GetFileName(projectDir).Equals("publish", StringComparison.OrdinalIgnoreCase))
            projectDir = Path.GetDirectoryName(projectDir) ?? projectDir;

        string[] dirsToClean = ["obj", "bin"];
        foreach (var dirName in dirsToClean)
        {
            var targetPath = Path.Combine(projectDir, dirName);
            if (!Directory.Exists(targetPath)) continue;
            try { Directory.Delete(targetPath, recursive: true); }
            catch { /* 文件被占用或删除失败时静默忽略，不影响启动 */ }
        }
    }

    /// <summary>
    /// 读取 Kestrel 实际绑定的地址。
    /// </summary>
    private static string ResolveBaseUrl(WebApplication app)
    {
        var server = app.Services.GetRequiredService<IServer>();
        var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses;
        return addresses?.FirstOrDefault() ?? "http://127.0.0.1:51942";
    }

    /// <summary>检测指定回环端口当前是否可绑定（空闲）。</summary>
    private static bool IsPortAvailable(int port)
    {
        try
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch
        {
            return false;
        }
    }
}

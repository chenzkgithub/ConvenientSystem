using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using FreeSql;
using ConvenientSystem.Service.YunHan;
using ConvenientSystem.Desktop;

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

        // ========== 版本检查 ==========
        // 1. 首次安装（wwwroot 为空）→ 静默下载初始 Web 版本
        // 2. 已有版本 → 先检查桌面程序更新（弹窗），再静默检查 Web 前端更新
        var wwwrootDir = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .Build();
        var remoteUrl = (config["AppSettings:RemoteServerUrl"] ?? string.Empty).Trim();
        var remoteBaseUrl = !string.IsNullOrEmpty(remoteUrl) ? $"http://{remoteUrl.TrimEnd('/')}" : string.Empty;
        // 桌面版本号：以 appsettings.json 中的 DesktopVersion 为准，但安装包升级后
        // installer 默认不会覆盖已存在的 appsettings.json（避免丢失用户配置），
        // 因此需要和程序集版本取较高者，并回写 appsettings.json 使其保持同步。
        var desktopVersion = ResolveDesktopVersion(config, AppContext.BaseDirectory);

        if (!string.IsNullOrEmpty(remoteBaseUrl))
        {
            if (!File.Exists(Path.Combine(wwwrootDir, "index.html")))
            {
                WebUpdateService.DownloadInitialAsync(wwwrootDir, remoteBaseUrl).GetAwaiter().GetResult();
            }
            else
            {
                // 先检查桌面程序是否有更新
                var desktopUpdate = DesktopUpdateService.CheckAsync(remoteBaseUrl, desktopVersion).GetAwaiter().GetResult();
                if (desktopUpdate != null)
                {
                    // 同时获取 Web 更新信息，用于统一对话框展示
                    var webUpdate = WebUpdateService.PeekAsync(wwwrootDir, remoteBaseUrl).GetAwaiter().GetResult();
                    var mode = webUpdate != null ? UpdateDialogMode.DesktopAndWeb : UpdateDialogMode.DesktopOnly;

                    using var dialog = new UpdateDialog(
                        mode,
                        desktopVersion,
                        desktopUpdate.Version,
                        desktopUpdate.Description,
                        async progress =>
                        {
                            var setupPath = await DesktopUpdateService.DownloadAsync(remoteBaseUrl, desktopUpdate, progress);
                            progress?.Report((98, "正在启动安装程序..."));
                            DesktopUpdateService.LaunchInstaller(setupPath);
                            progress?.Report((100, "即将退出并安装新版本"));
                            // 短暂延迟让用户看到完成提示，然后退出当前进程
                            await Task.Delay(800);
                            Environment.Exit(0);
                        },
                        webUpdate);

                    Application.Run(dialog);
                    // 如果用户点击"以后再说"，继续下面的 Web 静默更新
                }

                // 没有桌面更新或用户跳过时，静默检查 Web 前端更新
                WebUpdateService.SilentUpdateAsync(wwwrootDir, remoteBaseUrl).GetAwaiter().GetResult();
            }
        }

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
    /// 构建并配置 ASP.NET Core 应用（瘦客户端：静态文件 + 反向代理）。
    /// 当配置了 YhSystemDb 连接串时，额外注册考勤控制器直连内网数据库。
    /// </summary>
    private static WebApplication BuildWebApp(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // 允许桌面端反向代理大文件上传（安装包/前端包），与远程 API 的 510MB 限制对齐
        builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = 600 * 1024 * 1024);
        builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 600_000_000);

        // wwwroot 指向 exe 同级目录（非嵌入静态资源），由版本管理服务下载更新
        builder.Environment.WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot");

        // 反向代理 HttpClient
        builder.Services.AddHttpClient("ReverseProxy", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(5);
        });

        // ========== 本地监控服务（始终注册，无数据库依赖） ==========
        builder.Services.AddSingleton<LocalMonitorService>();
        builder.Services.AddSingleton<UniversalBuildService>();
        builder.Services.AddSingleton<DeployService>();
        builder.Services.AddSingleton<UniversalScheduleService>();
        builder.Services.AddSingleton<GitService>();
        builder.Services.AddControllers()
            .AddJsonOptions(options =>
            {
                // 枚举使用字符串序列化/反序列化，适配前端传入的 'Web'/'Api' 等字符串
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            });

        // ========== 本地考勤查询：当配置了内网数据库连接串时注册控制器 ==========
        var yhConnStr = builder.Configuration.GetConnectionString("YhSystemDb");
        if (!string.IsNullOrWhiteSpace(yhConnStr))
        {
            // 注册 IFreeSql（直连内网 SQL Server，关闭自动建表）
            builder.Services.AddSingleton<IFreeSql>(sp =>
            {
                var fsql = new FreeSqlBuilder()
                    .UseConnectionString(FreeSql.DataType.SqlServer, yhConnStr)
                    .UseAutoSyncStructure(false)
                    .Build();
                fsql.Aop.CurdAfter += (s, e) =>
                {
                    var logger = sp.GetRequiredService<ILogger<IFreeSql>>();
                    logger.LogDebug("FreeSql(内网考勤) SQL执行：\n{Sql}\n耗时{Elapsed}ms", e.Sql, e.ElapsedMilliseconds);
                };
                return fsql;
            });

            builder.Services.AddSingleton<IAttendanceService, AttendanceService>();
        }

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
        // 考勤路径（/api/YunHan/Attendance/*）由本地控制器处理，不转发。
        app.UseMiddleware<ReverseProxyMiddleware>();

        // 注册控制器路由（MonitorController 始终可发现；AttendanceController 仅当 YhSystemDb 配置时有效）
        app.MapControllers();

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
    /// 解析当前桌面程序的真实版本号。
    /// 安装包升级时 Inno Setup 默认不会覆盖已存在的 appsettings.json，
    /// 因此将文件中的 DesktopVersion 与程序集版本取较高者，并回写文件保持同步。
    /// </summary>
    private static string ResolveDesktopVersion(IConfigurationRoot config, string baseDir)
    {
        var fileVersion = (config["AppSettings:DesktopVersion"] ?? string.Empty).Trim();
        var assemblyVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

        var effectiveVersion = IsHigherVersion(assemblyVersion, fileVersion) ? assemblyVersion : fileVersion;

        // 如果文件中的版本低于程序集版本，回写 appsettings.json（保留其他用户配置）
        if (!string.Equals(fileVersion, effectiveVersion, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var configPath = Path.Combine(baseDir, "appsettings.json");
                if (File.Exists(configPath))
                {
                    var json = File.ReadAllText(configPath);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    var options = new JsonSerializerOptions { WriteIndented = true };
                    using var stream = new MemoryStream();
                    using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
                    writer.WriteStartObject();

                    foreach (var property in root.EnumerateObject())
                    {
                        if (property.NameEquals("AppSettings"))
                        {
                            writer.WritePropertyName("AppSettings");
                            writer.WriteStartObject();
                            foreach (var appSetting in property.Value.EnumerateObject())
                            {
                                if (appSetting.NameEquals("DesktopVersion"))
                                {
                                    writer.WritePropertyName("DesktopVersion");
                                    writer.WriteStringValue(effectiveVersion);
                                }
                                else
                                {
                                    appSetting.WriteTo(writer);
                                }
                            }
                            writer.WriteEndObject();
                        }
                        else
                        {
                            property.WriteTo(writer);
                        }
                    }
                    writer.WriteEndObject();
                    writer.Flush();

                    File.WriteAllBytes(configPath, stream.ToArray());
                }
            }
            catch
            {
                // 写文件失败（如权限不足）不影响启动，effectiveVersion 已取到正确值
            }
        }

        return effectiveVersion;
    }

    /// <summary>语义版本比较：判断 a 是否高于 b。</summary>
    private static bool IsHigherVersion(string a, string b)
    {
        if (string.IsNullOrEmpty(b)) return true;
        var aParts = a.Split('.', '-').Select(s => int.TryParse(s, out var n) ? n : 0).ToArray();
        var bParts = b.Split('.', '-').Select(s => int.TryParse(s, out var n) ? n : 0).ToArray();
        for (int i = 0; i < Math.Max(aParts.Length, bParts.Length); i++)
        {
            var av = i < aParts.Length ? aParts[i] : 0;
            var bv = i < bParts.Length ? bParts[i] : 0;
            if (av > bv) return true;
            if (av < bv) return false;
        }
        return false;
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

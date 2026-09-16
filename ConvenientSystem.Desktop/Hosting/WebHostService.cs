using System.Net;
using System.Net.Sockets;
using System.Text.Json.Serialization;
using FreeSql;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.StaticFiles;
using ConvenientSystem.Service.Common;
using ConvenientSystem.Service.Common.ApiSpec;
using ConvenientSystem.Service.YunHan;

namespace ConvenientSystem;

/// <summary>桌面端 Kestrel 宿主服务：负责启动/停止本机回环 Web 服务。</summary>
internal sealed class WebHostService : IHostedService, IDisposable
{
    private readonly ILogger<WebHostService> _logger;
    private readonly IWritableOptions<AppSettings> _appSettings;
    private WebApplication? _app;

    public string BaseUrl { get; private set; } = string.Empty;

    public WebHostService(
        ILogger<WebHostService> logger,
        IWritableOptions<AppSettings> appSettings)
    {
        _logger = logger;
        _appSettings = appSettings;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var app = BuildWebApp(_appSettings);
        var preferredPort = 51942;
        var port = IsPortAvailable(preferredPort) ? preferredPort : 0;

        app.Urls.Clear();
        app.Urls.Add($"http://127.0.0.1:{port}");

        try
        {
            await app.StartAsync(cancellationToken);
        }
        catch
        {
            (app as IDisposable)?.Dispose();
            app = BuildWebApp(_appSettings);
            app.Urls.Clear();
            app.Urls.Add("http://127.0.0.1:0");
            await app.StartAsync(cancellationToken);
        }

        _app = app;
        BaseUrl = ResolveBaseUrl(app);
        _logger.LogInformation("Kestrel 已启动：{BaseUrl}", BaseUrl);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_app is null) return;
        await _app.StopAsync(cancellationToken);
        ( _app as IDisposable)?.Dispose();
        _app = null;
        _logger.LogInformation("Kestrel 已停止");
    }

    public void Dispose()
    {
        (_app as IDisposable)?.Dispose();
    }

    private static WebApplication BuildWebApp(IWritableOptions<AppSettings> appSettings)
    {
        var builder = WebApplication.CreateBuilder();

        // 把外部 Host 已注册的 IWritableOptions 透传给 Kestrel 子容器，
        // 使 ReverseProxyMiddleware / WebUpdateController 等内部中间件和控制器能解析同一配置实例。
        builder.Services.AddSingleton(appSettings);

        builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = 600 * 1024 * 1024);
        builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 600_000_000);
        builder.Environment.WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot");

        builder.Services.AddHttpClient("ReverseProxy", client => client.Timeout = TimeSpan.FromMinutes(5));

        builder.Services.AddSingleton<LocalMonitorService>();
        builder.Services.AddSingleton<UniversalBuildStore>();
        builder.Services.AddSingleton<UniversalBuildService>();
        builder.Services.AddSingleton<DeployStore>();
        builder.Services.AddSingleton<DeployService>();
        builder.Services.AddSingleton<SshCredentialStore>();
        builder.Services.AddSingleton<UiStateStore>();
        // 配置文件热编辑：exe 目录实际存在的配置文件（部署目录无各环境 variants，但有启动器配置）
        builder.Services.AddSingleton<IConfigEditorService>(_ => new ConfigEditorService(
            _.GetRequiredService<ILogger<ConfigEditorService>>(),
            _.GetRequiredService<IHostApplicationLifetime>(),
        [
            ("appsettings.json", "json"),
            ("launcher-items.json", "json"),
        ]));
        builder.Services.AddSingleton<LocalCodeScanService>();
        builder.Services.AddSingleton<PipelineStore>();
        builder.Services.AddSingleton<PipelineService>();
        builder.Services.AddSingleton<UniversalScheduleService>();
        builder.Services.AddSingleton<GitService>();
        builder.Services.AddSingleton<IApiExporter, OpenApiJsonExporter>();
        builder.Services.AddSingleton<IApiExporter, OpenApiYamlExporter>();
        builder.Services.AddSingleton<IApiExporter, PostmanExporter>();
        builder.Services.AddSingleton<IApiExporter, MarkdownExporter>();
        builder.Services.AddSingleton<ApiSpecService>();
        builder.Services.AddControllers()
            .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        // 本地考勤查询：当配置了内网数据库连接串时注册控制器。
        var yhConnStr = builder.Configuration.GetConnectionString("YhSystemDb");
        if (!string.IsNullOrWhiteSpace(yhConnStr))
        {
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

        app.UseDefaultFiles();
        app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = ctx =>
            {
                if (ctx.File.Name.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                {
                    var headers = ctx.Context.Response.Headers;
                    headers.CacheControl = "no-cache, no-store, must-revalidate";
                    headers.Pragma = "no-cache";
                    headers.Expires = "0";
                }
            }
        });

        app.UseWebSockets();
        app.UseMiddleware<ReverseProxyMiddleware>();
        app.MapControllers();

        return app;
    }

    private static string ResolveBaseUrl(WebApplication app)
    {
        var server = app.Services.GetRequiredService<IServer>();
        var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses;
        return addresses?.FirstOrDefault() ?? "http://127.0.0.1:51942";
    }

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

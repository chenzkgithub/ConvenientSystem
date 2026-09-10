using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ConvenientSystem;

/// <summary>桌面端启动引导器：协调单实例、清理、更新检查、Kestrel 启动和主窗体运行。</summary>
internal sealed class StartupBootstrapper
{
    public void Run(string[] args)
    {
        BuildArtifactCleaner.Clean();

        var host = CreateHost(args);

        // 更新检查需要先构造 WinForms 对话框，必须在 WinForms 消息泵启动前完成。
        using (var scope = host.Services.CreateScope())
        {
            var updateChecker = scope.ServiceProvider.GetRequiredService<DesktopUpdateChecker>();
            if (updateChecker.CheckOnStartupAsync().GetAwaiter().GetResult())
            {
                return;
            }
        }

        host.StartAsync().GetAwaiter().GetResult();

        try
        {
            var form = host.Services.GetRequiredService<MainForm>();
            Application.Run(form);
        }
        finally
        {
            host.StopAsync().GetAwaiter().GetResult();
            host.Dispose();
        }
    }

    private static IHost CreateHost(string[] args)
    {
        return Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration(config =>
            {
                config.SetBasePath(AppContext.BaseDirectory);
                config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
            })
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddConsole();
                logging.AddFile(options =>
                {
                    options.LogDirectory = "logs";
                    options.FileNamePrefix = "desktop";
                    options.MinimumLevel = LogLevel.Information;
                    options.RetainedFileCount = 7;
                });
            })
            .ConfigureServices((context, services) =>
            {
                services.ConfigureWritable<AppSettings>(context.Configuration, "AppSettings");

                services.AddSingleton<DesktopUpdateChecker>();
                services.AddSingleton<WebHostService>();
                services.AddHostedService(sp => sp.GetRequiredService<WebHostService>());

                // 索引与启动器依赖
                services.AddSingleton<AppIndexService>();
                services.AddSingleton<FileIndexService>();
                services.AddSingleton<GlobalHotkeyManager>();
                services.AddSingleton<LauncherStoreFactory>();
                services.AddSingleton<QuickLauncherFactory>();
                // 注意：LauncherStore 由 LauncherStoreFactory 在运行时创建，不直接注册到 DI。

                // WinForms 窗体生命周期由 Application.Run 管理，此处只注册为单例以便注入。
                // 窗体类为 internal，通过工厂委托创建，避免 DI 默认 Activator 要求 public 构造函数。
                services.AddSingleton(sp => new MainForm(
                    sp.GetRequiredService<WebHostService>(),
                    sp.GetRequiredService<AppIndexService>(),
                    sp.GetRequiredService<FileIndexService>(),
                    sp.GetRequiredService<GlobalHotkeyManager>(),
                    sp.GetRequiredService<LauncherStoreFactory>(),
                    sp.GetRequiredService<QuickLauncherFactory>(),
                    sp.GetRequiredService<SystemHelpDialog>(),
                    sp));
                services.AddSingleton(sp => new SystemHelpDialog(
                    sp.GetRequiredService<IWritableOptions<AppSettings>>()));

                // BrowserForm 会被多次打开，使用 Transient 每次创建新实例。
                services.AddTransient<BrowserForm>();
            })
            .Build();
    }
}

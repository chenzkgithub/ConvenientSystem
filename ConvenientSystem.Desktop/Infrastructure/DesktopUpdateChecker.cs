using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ConvenientSystem;

/// <summary>桌面端启动时更新检查：首次安装静默下载，后续检查桌面/Web 更新。</summary>
internal sealed class DesktopUpdateChecker
{
    private readonly ILogger<DesktopUpdateChecker> _logger;
    private readonly IWritableOptions<AppSettings> _appSettings;

    public DesktopUpdateChecker(ILogger<DesktopUpdateChecker> logger, IWritableOptions<AppSettings> appSettings)
    {
        _logger = logger;
        _appSettings = appSettings;
    }

    /// <summary>执行启动期更新检查。若需要立即更新，返回 true 并弹出更新对话框。</summary>
    public async Task<bool> CheckOnStartupAsync()
    {
        var remoteBaseUrl = ResolveRemoteBaseUrl();
        if (string.IsNullOrEmpty(remoteBaseUrl))
        {
            _logger.LogInformation("未配置远程更新服务器，跳过启动期更新检查");
            return false;
        }

        var wwwrootDir = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        var desktopVersion = ResolveDesktopVersion();

        try
        {
            if (!File.Exists(Path.Combine(wwwrootDir, "index.html")))
            {
                _logger.LogInformation("首次安装，静默下载初始 Web 前端版本");
                await WebUpdateService.DownloadInitialAsync(wwwrootDir, remoteBaseUrl);
                return false;
            }

            var desktopUpdate = await DesktopUpdateService.CheckAsync(remoteBaseUrl, desktopVersion);
            if (desktopUpdate == null)
            {
                _logger.LogInformation("桌面程序已是最新版本");
                return false;
            }

            var webUpdate = await WebUpdateService.PeekAsync(wwwrootDir, remoteBaseUrl);
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
                    var setupProcess = DesktopUpdateService.LaunchInstaller(setupPath);

                    await Task.Delay(2500);
                    try
                    {
                        setupProcess.Refresh();
                        if (setupProcess.HasExited)
                        {
                            progress?.Report((100, "安装程序未能正常启动，请手动运行安装包"));
                            await Task.Delay(2000);
                            return;
                        }
                    }
                    catch
                    {
                        // 无法访问进程句柄时视为已启动
                    }

                    progress?.Report((100, "安装程序已启动，即将关闭当前程序"));
                    await Task.Delay(800);
                    Environment.Exit(0);
                },
                webUpdate);

            Application.Run(dialog);
            // 对话框正常关闭只有两种情况：用户点"以后再说"，或更新失败后点"确定"，
            // 两者都应继续正常启动程序；点"立即更新并重启"时回调内已 Environment.Exit(0)，
            // 不会执行到这里。返回 false 让 StartupBootstrapper 继续启动 MainForm。
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动期更新检查失败");
            return false;
        }
    }

    private string ResolveRemoteBaseUrl()
    {
        var remote = _appSettings.Value.RemoteServerUrl.Trim();
        return string.IsNullOrEmpty(remote) ? string.Empty : $"http://{remote.TrimEnd('/')}";
    }

    /// <summary>解析桌面程序真实版本，并回写 appsettings.json 保持同步。</summary>
    private string ResolveDesktopVersion()
    {
        var fileVersion = _appSettings.Value.DesktopVersion.Trim();
        var assemblyVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? string.Empty;
        var hasAssemblyVersion = !string.IsNullOrEmpty(assemblyVersion) && assemblyVersion != "0.0.0.0";
        var effectiveVersion = hasAssemblyVersion ? assemblyVersion : (string.IsNullOrEmpty(fileVersion) ? "1.0.0.0" : fileVersion);

        if (!string.Equals(fileVersion, effectiveVersion, StringComparison.OrdinalIgnoreCase))
        {
            _appSettings.Update(s => s.DesktopVersion = effectiveVersion);
        }

        return effectiveVersion;
    }
}

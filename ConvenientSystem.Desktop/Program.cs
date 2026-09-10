using System.Diagnostics;
using System.Text.Json;
using System.Threading;

namespace ConvenientSystem;

/// <summary>
/// 桌面程序入口：仅保留全局异常兜底与启动引导，所有启动逻辑委托给 StartupBootstrapper。
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // 确保主线程以 STA（单线程单元）启动，WebView2 依赖于此。
        // 某些环境 / SDK 版本下 [STAThread] 可能不够可靠，这里再显式设置一次。
        try
        {
            Thread.CurrentThread.SetApartmentState(ApartmentState.STA);
        }
        catch (InvalidOperationException)
        {
            // 线程已经初始化 COM，无法再更改 ApartmentState。
            // 继续执行，后续 WebView2 初始化失败时会给出更明确的诊断。
        }

        // 必须在创建任何 WinForms 窗口之前调用（更新检查也会弹窗）。
        ApplicationConfiguration.Initialize();

        AttachGlobalExceptionHandlers();

        using var guard = new SingleInstanceGuard();
        if (!guard.TryAcquire(args.Contains("--restart")))
        {
            return;
        }

        try
        {
            new StartupBootstrapper().Run(args);
        }
        catch (Exception ex)
        {
            LogFatal("启动过程中发生未处理异常", ex);
            ShowFatalDialog(ex);
        }
    }

    /// <summary>注册全局异常处理，确保任何未捕获异常都能被记录。</summary>
    private static void AttachGlobalExceptionHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            LogFatal("AppDomain 未处理异常", e.ExceptionObject as Exception);
            if (e.IsTerminating)
                ShowFatalDialog(e.ExceptionObject as Exception);
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogFatal("未观察任务异常", e.Exception);
            e.SetObserved();
        };

        Application.ThreadException += (_, e) =>
        {
            LogFatal("UI 线程异常", e.Exception);
            // 不阻止默认处理，让 WinForms 继续显示其错误对话框
        };
    }

    /// <summary>记录致命异常到 Windows 事件日志或临时文件（日志服务尚未初始化时的兜底）。</summary>
    private static void LogFatal(string title, Exception? ex)
    {
        try
        {
            var logDir = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(logDir);
            var path = Path.Combine(logDir, $"fatal-{DateTime.Now:yyyyMMdd}.log");
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {title}{Environment.NewLine}{ex}{Environment.NewLine}";
            File.AppendAllText(path, line);
        }
        catch
        {
            // 兜底记录也失败时无法进一步处理
        }
    }

    private static void ShowFatalDialog(Exception? ex)
    {
        var message = ex is null
            ? "程序发生致命错误，即将退出。"
            : $"程序发生致命错误：{ex.Message}\n\n详情请查看 logs/fatal-*.log";
        MessageBox.Show(message, "致命错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}

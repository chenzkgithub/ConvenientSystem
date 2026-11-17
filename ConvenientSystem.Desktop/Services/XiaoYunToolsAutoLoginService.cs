using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace ConvenientSystem;

/// <summary>
/// 小昀工具箱免扫码启动：复用 ERP 自动登录适配器获取 token，写入工具箱本地数据库
/// thisapp.db 的 LoginToken 表后带参数启动。工具箱启动时只要当天存在 token 记录
/// 即跳过钉钉扫码直接进主界面（其启动校验只比较本地 TokenTime 日期，不请求服务器）。
///
/// 任何环节失败（未安装、无凭据、登录失败、用户取消验证码）均回退为原样启动，
/// 用户仍可手动扫码，对工具箱零侵入。
/// </summary>
public sealed class XiaoYunToolsAutoLoginService
{
    /// <summary>内网 ERP（lanerp 模式，工具箱探测顺序第一位）。</summary>
    private const string LanErpAddress = "http://192.168.16.240:8001/";
    /// <summary>外网 ERP（wlankf 模式，内网不通时使用）。</summary>
    private const string WLanErpAddress = "http://a.yunhanmy.com:8098/";
    /// <summary>测试模式 ERP（test 参数对应的 appsettings.test.json 反代地址）。</summary>
    private const string TestErpAddress = "http://192.168.16.240:8000/";

    /// <summary>固定候选目录（本机确认过的安装位置），未命中时再查注册表兜底。</summary>
    private static readonly string[] InstallDirectories =
    [
        @"D:\小昀工具箱",
        @"C:\Program Files\小昀工具箱",
    ];

    private readonly BrowserAutoLoginCoordinator _coordinator;
    private readonly ILogger<XiaoYunToolsAutoLoginService> _logger;

    public XiaoYunToolsAutoLoginService(
        BrowserAutoLoginCoordinator coordinator,
        ILogger<XiaoYunToolsAutoLoginService> logger)
    {
        _coordinator = coordinator;
        _logger = logger;
    }

    /// <summary>定位工具箱安装目录：固定路径优先，未命中时查注册表卸载信息兜底。</summary>
    public static string? FindInstallDirectory()
    {
        foreach (var directory in InstallDirectories)
        {
            if (IsValidInstallDirectory(directory))
                return directory;
        }
        return FindFromRegistry();
    }

    /// <summary>有效安装目录判定：exe 与 thisapp.db 必须同时存在，防止误中同名残留目录。</summary>
    private static bool IsValidInstallDirectory(string directory)
        => File.Exists(Path.Combine(directory, "小昀工具箱.exe")) &&
           File.Exists(Path.Combine(directory, "thisapp.db"));

    /// <summary>
    /// 注册表兜底：在标准卸载信息根键下按 DisplayName 含“小昀工具箱”匹配，
    /// 取 InstallLocation 再做 exe+db 验证，覆盖日后重装到其他盘符/目录的场景。
    /// 工具箱用 Inno Setup 安装（键名带 _is1 后缀），InstallLocation 可靠；
    /// DisplayName 不会匹配到同厂商的“小昀任务调度”。
    /// </summary>
    private static string? FindFromRegistry()
    {
        string[] roots =
        [
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
        ];
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            foreach (var root in roots)
            {
                using var baseKey = hive.OpenSubKey(root);
                if (baseKey is null) continue;

                foreach (var subKeyName in baseKey.GetSubKeyNames())
                {
                    using var key = baseKey.OpenSubKey(subKeyName);
                    if (key is null) continue;
                    if (key.GetValue("DisplayName") is not string displayName ||
                        !displayName.Contains("小昀工具箱", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (key.GetValue("InstallLocation") is not string location ||
                        string.IsNullOrWhiteSpace(location))
                        continue;

                    location = Environment.ExpandEnvironmentVariables(location.Trim());
                    if (IsValidInstallDirectory(location))
                        return location;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// 免扫码启动入口：按需登录注入 token 后启动工具箱。
    /// testMode 为 true 时以 "test" 参数启动（连接测试环境 ERP）。
    /// 菜单回调为 fire-and-forget 调用，这里兜底捕获全部异常，确保永不向外抛出。
    /// </summary>
    public async Task LaunchAsync(bool testMode, CancellationToken cancellationToken = default)
    {
        try
        {
            await LaunchCoreAsync(testMode, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "小昀工具箱免扫码启动发生未预期异常");
            LaunchAnyway(testMode);
            ReportFailure($"免扫码启动出现异常：{ex.Message}已直接启动工具箱，请手动扫码登录。");
        }
    }

    private async Task LaunchCoreAsync(bool testMode, CancellationToken cancellationToken)
    {
        var directory = FindInstallDirectory();
        if (directory is null)
        {
            ReportFailure("未找到小昀工具箱安装目录（需同时存在 小昀工具箱.exe 与 thisapp.db），请确认安装位置。");
            return;
        }

        if (IsRunning())
        {
            ReportFailure("小昀工具箱已在运行中，请直接使用当前窗口。");
            return;
        }

        var exePath = Path.Combine(directory, "小昀工具箱.exe");
        var databasePath = Path.Combine(directory, "thisapp.db");

        // 当天已有 token 记录则无需重新登录，工具箱启动校验只看日期。
        if (!HasTokenForToday(databasePath))
        {
            var targetAddress = testMode ? TestErpAddress : await ResolveNormalErpAddressAsync(cancellationToken);
            if (targetAddress is null)
            {
                LaunchTool(exePath, testMode);
                ReportFailure("内网与外网 ERP 均无法连接，已直接启动工具箱，请手动扫码登录。");
                return;
            }

            // 借用 ERP 浏览器自动登录链路（含验证码 OCR 与手动兜底）获取 token。
            var loginUri = new Uri(new Uri(targetAddress), "/login");
            var result = await _coordinator.LoginAsync(loginUri.AbsoluteUri, cancellationToken);
            if (!result.Success || string.IsNullOrWhiteSpace(result.Token))
            {
                LaunchTool(exePath, testMode);
                ReportFailure($"{result.Message}已直接启动工具箱，请手动扫码登录。");
                return;
            }

            try
            {
                WriteToken(databasePath, result.Token);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "写入小昀工具箱 LoginToken 失败");
                LaunchTool(exePath, testMode);
                ReportFailure($"写入登录凭据失败：{ex.Message}已直接启动工具箱，请手动扫码登录。");
                return;
            }
        }

        LaunchTool(exePath, testMode);
    }

    /// <summary>复刻工具箱自身的环境探测：内网可达优先内网，否则外网，均不可达返回 null。</summary>
    private static async Task<string?> ResolveNormalErpAddressAsync(CancellationToken cancellationToken)
    {
        foreach (var address in new[] { LanErpAddress, WLanErpAddress })
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(2));
                using var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
                using var response = await client.GetAsync(address, timeout.Token);
                if (response.IsSuccessStatusCode)
                    return address;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // 探测超时按不可达处理，继续尝试下一个地址
            }
            catch (Exception)
            {
                // 网络异常按不可达处理
            }
        }
        return null;
    }

    /// <summary>工具箱是单实例互斥（YunHanAiTool.exe），按进程名判断即可。</summary>
    private static bool IsRunning()
        => Process.GetProcessesByName("小昀工具箱").Length > 0;

    /// <summary>查询 LoginToken 表：最新一条记录的 TokenTime 是否为今天。</summary>
    private static bool HasTokenForToday(string databasePath)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT TokenTime FROM LoginToken ORDER BY Id DESC LIMIT 1";
        var value = command.ExecuteScalar() as string;
        if (value is null) return false;
        return DateTime.TryParse(value, out var time) && time.Date == DateTime.Today;
    }

    /// <summary>
    /// 清空历史记录后写入新 token。TokenType=0 为账密/扫码登录的普通令牌，
    /// TokenTime 采用与 FreeSql 一致的文本日期格式（yyyy-MM-dd HH:mm:ss）。
    /// </summary>
    private static void WriteToken(string databasePath, string token)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        using var clear = connection.CreateCommand();
        clear.CommandText = "DELETE FROM LoginToken";
        clear.ExecuteNonQuery();

        using var insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO LoginToken (TokenStr, TokenType, TokenTime)
            VALUES (@token, 0, @time)
            """;
        insert.Parameters.AddWithValue("@token", token);
        insert.Parameters.AddWithValue("@time", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        insert.ExecuteNonQuery();
    }

    private static void LaunchTool(string exePath, bool testMode)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = testMode ? "test" : string.Empty,
            UseShellExecute = true,
        });
    }

    /// <summary>异常兜底：尽力原样启动工具箱，让用户仍可手动扫码。</summary>
    private void LaunchAnyway(bool testMode)
    {
        try
        {
            var directory = FindInstallDirectory();
            if (directory is null || IsRunning()) return;
            LaunchTool(Path.Combine(directory, "小昀工具箱.exe"), testMode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "小昀工具箱兜底启动失败");
        }
    }

    private void ReportFailure(string message)
    {
        _logger.LogWarning("小昀工具箱免扫码启动回退：{Message}", message);
        MessageBox.Show(message, "小昀工具箱", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}

using System.Diagnostics;
using System.Net.Http.Json;

namespace ConvenientSystem;

/// <summary>
/// 桌面程序自更新服务：检查服务器上的安装包版本、下载 Setup.exe、启动安装程序。
/// </summary>
internal static class DesktopUpdateService
{
    private static readonly HttpClient s_http = new() { Timeout = TimeSpan.FromMinutes(30) };

    /// <summary>
    /// 检查服务器是否有更高版本的桌面安装包。
    /// </summary>
    public static async Task<DesktopUpdateInfo?> CheckAsync(string remoteBaseUrl, string currentVersion)
    {
        try
        {
            var url = $"{remoteBaseUrl}/api/Common/DesktopUpdate/Check?version={Uri.EscapeDataString(currentVersion)}";
            using var response = await s_http.GetAsync(url);
            if (!response.IsSuccessStatusCode) return null;

            var result = await response.Content.ReadFromJsonAsync<DesktopUpdateCheckResult>();
            if (result == null || !result.HasUpdate) return null;

            return new DesktopUpdateInfo
            {
                Version = result.Version,
                Description = result.Description,
                FileSize = result.FileSize,
                DownloadUrl = result.DownloadUrl,
            };
        }
        catch
        {
            return null; // 服务器不可达时静默跳过
        }
    }

    /// <summary>
    /// 下载安装包到临时目录，返回本地文件路径。
    /// </summary>
    public static async Task<string> DownloadAsync(
        string remoteBaseUrl,
        DesktopUpdateInfo info,
        IProgress<(int percent, string status)>? progress)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"ConvenientSystem-DesktopUpdate-{DateTime.Now:yyyyMMddHHmmss}");
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, $"ConvenientSystem-Setup-{info.Version}.exe");

        progress?.Report((5, "正在连接服务器..."));

        const int maxRetries = 3;
        Exception? lastEx = null;
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                await DownloadFileAsync(remoteBaseUrl + info.DownloadUrl, tempFile, progress);
                lastEx = null;
                break;
            }
            catch (Exception ex)
            {
                lastEx = ex;
                TryDelete(tempFile);
                if (attempt < maxRetries)
                {
                    progress?.Report((5, $"下载失败，第 {attempt}/{maxRetries} 次重试..."));
                    await Task.Delay(TimeSpan.FromSeconds(attempt * 2));
                }
            }
        }

        if (lastEx != null)
            throw new InvalidOperationException($"下载失败（已重试 {maxRetries} 次）：{lastEx.Message}", lastEx);

        return tempFile;
    }

    /// <summary>
    /// 启动下载好的安装程序并退出当前进程。
    /// </summary>
    public static void LaunchInstaller(string setupPath)
    {
        if (!File.Exists(setupPath))
            throw new FileNotFoundException("安装程序不存在", setupPath);

        // /SILENT       静默安装（显示进度条）
        // /CLOSEAPPLICATIONS  安装前关闭正在运行的目标程序
        // /RESTARTAPPLICATIONS 安装完成后重新启动目标程序
        var psi = new ProcessStartInfo(setupPath)
        {
            Arguments = "/SILENT /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS",
            UseShellExecute = true,
            Verb = "runas", // 请求管理员权限（安装到 Program Files 需要）
        };

        Process.Start(psi);
    }

    private static async Task DownloadFileAsync(string url, string destPath, IProgress<(int percent, string status)>? progress)
    {
        using var response = await s_http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1;
        await using var contentStream = await response.Content.ReadAsStreamAsync();
        await using var fileStream = File.Create(destPath);

        var buffer = new byte[81920];
        long bytesRead = 0;
        int read;
        while ((read = await contentStream.ReadAsync(buffer)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, read));
            bytesRead += read;

            if (totalBytes > 0)
            {
                var percent = (int)(bytesRead * 90 / totalBytes) + 5; // 5-95%
                progress?.Report((percent, $"正在下载 {FormatSize(bytesRead)} / {FormatSize(totalBytes)}..."));
            }
            else
            {
                progress?.Report((50, $"正在下载 {FormatSize(bytesRead)}..."));
            }
        }

        if (totalBytes > 0 && bytesRead != totalBytes)
            throw new InvalidOperationException($"下载不完整：期望 {FormatSize(totalBytes)}，实际 {FormatSize(bytesRead)}");
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static string FormatSize(long bytes)
        => bytes < 1024 ? $"{bytes} B"
         : bytes < 1024 * 1024 ? $"{bytes / 1024.0:F1} KB"
         : $"{bytes / 1024.0 / 1024.0:F1} MB";
}

/// <summary>桌面更新信息（由 CheckAsync 返回）。</summary>
internal sealed class DesktopUpdateInfo
{
    public string Version { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long FileSize { get; set; }
    public string DownloadUrl { get; set; } = string.Empty;
}

/// <summary>与服务器 DesktopUpdateCheckResult 对应的本地镜像（避免引用 Shared 项目）。</summary>
internal sealed class DesktopUpdateCheckResult
{
    public bool HasUpdate { get; set; }
    public string Version { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long FileSize { get; set; }
    public string DownloadUrl { get; set; } = string.Empty;
}

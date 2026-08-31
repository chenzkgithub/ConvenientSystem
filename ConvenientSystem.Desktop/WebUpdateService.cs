using System.IO.Compression;
using System.Net.Http.Json;
using System.Text.Json;

namespace ConvenientSystem;

/// <summary>
/// Web 前端版本更新服务：检查服务器版本、下载 zip、原子替换 wwwroot。
/// 桌面客户端启动时调用：首次安装静默下载，已有版本则弹窗让用户选择是否更新。
/// </summary>
internal static class WebUpdateService
{
    private static readonly HttpClient s_http = new() { Timeout = TimeSpan.FromMinutes(10) };

    /// <summary>本地版本信息文件（wwwroot/version.json）</summary>
    private static readonly JsonSerializerOptions s_jsonOpts = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// 首次安装：wwwroot 为空时静默下载激活版本并解压到 wwwroot。
    /// </summary>
    public static async Task DownloadInitialAsync(string wwwrootDir, string remoteBaseUrl)
    {
        try
        {
            Directory.CreateDirectory(wwwrootDir);
            var remoteInfo = await GetRemoteVersionAsync(remoteBaseUrl)
                ?? throw new InvalidOperationException("服务器无激活版本");
            await DownloadAndExtractAsync(wwwrootDir, remoteBaseUrl, remoteInfo, null);
        }
        catch
        {
            // 首次下载失败时静默忽略，用户将看到空白页面或错误页
        }
    }

    /// <summary>
    /// 检查服务器版本，远程版本高于本地时弹出更新对话框。
    /// </summary>
    public static async Task CheckAndShowDialogAsync(string wwwrootDir, string remoteBaseUrl)
    {
        var localVersion = ReadLocalVersion(wwwrootDir);
        var remoteInfo = await GetRemoteVersionAsync(remoteBaseUrl);

        if (remoteInfo == null) return;                     // 服务器无版本包，静默跳过
        if (!IsHigherVersion(remoteInfo.Version, localVersion)) return; // 远程不高于本地，静默跳过

        // 版本不同，弹窗（将已获取的版本信息直接传入，避免下载时重复请求）
        using var dialog = new UpdateDialog(
            UpdateDialogMode.WebOnly,
            localVersion ?? "(未知)",
            remoteInfo.Version,
            remoteInfo.Description,
            async progress =>
            {
                await DownloadAndExtractAsync(wwwrootDir, remoteBaseUrl, remoteInfo, progress);
            });

        Application.Run(dialog);
    }

    /// <summary>
    /// 静默检查并更新 Web 前端：远程版本高于本地时直接后台下载替换，不弹窗。
    /// 返回是否执行了更新。
    /// </summary>
    public static async Task<bool> SilentUpdateAsync(string wwwrootDir, string remoteBaseUrl)
    {
        var localVersion = ReadLocalVersion(wwwrootDir);
        var remoteInfo = await GetRemoteVersionAsync(remoteBaseUrl);

        if (remoteInfo == null) return false;
        if (!IsHigherVersion(remoteInfo.Version, localVersion)) return false;

        try
        {
            await DownloadAndExtractAsync(wwwrootDir, remoteBaseUrl, remoteInfo, null);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 仅检查远程 Web 版本信息，不执行下载或弹窗。用于统一更新对话框展示。
    /// </summary>
    public static async Task<WebUpdateInfo?> PeekAsync(string wwwrootDir, string remoteBaseUrl)
    {
        var localVersion = ReadLocalVersion(wwwrootDir);
        var remoteInfo = await GetRemoteVersionAsync(remoteBaseUrl);

        if (remoteInfo == null) return null;
        if (!IsHigherVersion(remoteInfo.Version, localVersion)) return null;

        return new WebUpdateInfo
        {
            LocalVersion = localVersion,
            Version = remoteInfo.Version,
            Description = remoteInfo.Description,
        };
    }

    /// <summary>
    /// 下载激活版本 zip，解压到临时目录后原子替换 wwwroot。
    /// </summary>
    private static async Task DownloadAndExtractAsync(
        string wwwrootDir, string remoteBaseUrl, RemoteVersionInfo remoteInfo, IProgress<(int percent, string status)>? progress)
    {
        // 所有临时文件/目录都放在 wwwrootDir 的父目录下，
        // 确保 Directory.Move 始终在同一卷内操作，不会跨盘失败。
        var localTempDir = Path.Combine(
            Path.GetDirectoryName(wwwrootDir) ?? AppContext.BaseDirectory,
            $".update-temp-{DateTime.Now:yyyyMMddHHmmss}");
        Directory.CreateDirectory(localTempDir);
        var tempZip = Path.Combine(localTempDir, "download.zip");
        var tempExtract = Path.Combine(localTempDir, "extracted");

        progress?.Report((5, "正在连接服务器..."));

        // 下载 zip（最多重试 3 次，指数退避）
        const int maxRetries = 3;
        Exception? lastDownloadEx = null;
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                await DownloadZipAsync(tempZip, remoteBaseUrl);
                lastDownloadEx = null;
                break;
            }
            catch (Exception ex)
            {
                lastDownloadEx = ex;
                TryDelete(tempZip);
                if (attempt < maxRetries)
                {
                    progress?.Report((5, $"下载失败，第 {attempt}/{maxRetries} 次重试..."));
                    await Task.Delay(TimeSpan.FromSeconds(attempt * 2));
                }
            }
        }
        if (lastDownloadEx != null)
            throw new InvalidOperationException($"下载失败（已重试 {maxRetries} 次）：{lastDownloadEx.Message}", lastDownloadEx);

        progress?.Report((95, "正在解压..."));

        ZipFile.ExtractToDirectory(tempZip, tempExtract);

        progress?.Report((98, "正在替换文件..."));

        // 原子替换 wwwroot：旧目录重命名后删除，新目录移动到位
        // 使用带时间戳的备份目录名，避免旧 .bak 残留或锁定导致移动失败
        var backupDir = wwwrootDir + ".bak." + DateTime.Now.ToString("yyyyMMddHHmmssfff");
        try
        {
            // 备份当前 wwwroot（加 copy+delete 兜底，处理文件被锁等 Directory.Move 失败场景）
            if (Directory.Exists(wwwrootDir))
                MoveDirectoryRobust(wwwrootDir, backupDir);

            // 移动新目录到 wwwroot
            MoveDirectoryRobust(tempExtract, wwwrootDir);

            // 删除备份
            TryDeleteDir(backupDir);
        }
        catch
        {
            // 恢复备份
            if (!Directory.Exists(wwwrootDir) && Directory.Exists(backupDir))
                try { MoveDirectoryRobust(backupDir, wwwrootDir); } catch { }
            throw;
        }
        finally
        {
            // 清理本地临时目录（zip + 解压产物 + 备份）
            TryDeleteDir(localTempDir);
            TryDeleteDir(backupDir);
        }

        // 写 version.json
        var versionJson = new { version = remoteInfo.Version, time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") };
        var versionPath = Path.Combine(wwwrootDir, "version.json");
        await File.WriteAllTextAsync(versionPath, JsonSerializer.Serialize(versionJson, s_jsonOpts));

        progress?.Report((100, "更新完成"));
    }

    /// <summary>执行单次 zip 下载（含进度回报）。</summary>
    private static async Task DownloadZipAsync(string tempZip, string remoteBaseUrl)
    {
        using var response = await s_http.GetAsync(
            $"{remoteBaseUrl}/api/Common/WebPackage/Download",
            HttpCompletionOption.ResponseHeadersRead);

        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1;
        await using var contentStream = await response.Content.ReadAsStreamAsync();
        await using var fileStream = File.Create(tempZip);

        var buffer = new byte[81920];
        long bytesRead = 0;
        int read;
        while ((read = await contentStream.ReadAsync(buffer)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, read));
            bytesRead += read;
        }

        // 校验下载完整性
        if (totalBytes > 0 && bytesRead != totalBytes)
            throw new InvalidOperationException($"下载不完整：期望 {FormatSize(totalBytes)}，实际 {FormatSize(bytesRead)}");
    }

    /// <summary>
    /// 调用 GetActive 端点获取服务器激活版本信息。
    /// </summary>
    private static async Task<RemoteVersionInfo?> GetRemoteVersionAsync(string remoteBaseUrl)
    {
        try
        {
            using var response = await s_http.GetAsync($"{remoteBaseUrl}/api/Common/WebPackage/GetActive");
            if (!response.IsSuccessStatusCode) return null;
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            if (!json.GetProperty("hasVersion").GetBoolean()) return null;
            return new RemoteVersionInfo
            {
                Version = json.GetProperty("version").GetString() ?? "",
                Description = json.TryGetProperty("description", out var desc) ? desc.GetString() : null,
            };
        }
        catch
        {
            return null; // 服务器不可达时静默跳过
        }
    }

    /// <summary>读取本地 version.json 中的版本号。</summary>
    private static string? ReadLocalVersion(string wwwrootDir)
    {
        var versionPath = Path.Combine(wwwrootDir, "version.json");
        if (!File.Exists(versionPath)) return null;
        try
        {
            var json = JsonSerializer.Deserialize<JsonElement>(
                File.ReadAllText(versionPath), s_jsonOpts);
            return json.GetProperty("version").GetString();
        }
        catch { return null; }
    }

    /// <summary>
    /// 语义版本比较：判断 remote 是否高于 local。
    /// 支持 major.minor.patch 格式，逐段数值比较；local 为空时视为有新版本。
    /// </summary>
    private static bool IsHigherVersion(string remote, string? local)
    {
        if (string.IsNullOrEmpty(local)) return true; // 本地无版本，视为有新版本

        var remoteParts = ParseVersion(remote);
        var localParts = ParseVersion(local);

        for (int i = 0; i < Math.Max(remoteParts.Length, localParts.Length); i++)
        {
            var r = i < remoteParts.Length ? remoteParts[i] : 0;
            var l = i < localParts.Length ? localParts[i] : 0;
            if (r > l) return true;
            if (r < l) return false;
        }
        return false; // 完全相等，不提示更新
    }

    private static int[] ParseVersion(string version)
    {
        return version.Split('.', '-')
            .Select(s => int.TryParse(s, out var n) ? n : 0)
            .ToArray();
    }

    private static string FormatSize(long bytes)
        => bytes < 1024 ? $"{bytes} B"
         : bytes < 1024 * 1024 ? $"{bytes / 1024.0:F1} KB"
         : $"{bytes / 1024.0 / 1024:F1} MB";

    private static void TryDelete(string path)
    { try { if (File.Exists(path)) File.Delete(path); } catch { } }

    private static void TryDeleteDir(string path)
    { try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { } }

    /// <summary>
    /// 尝试快速移动目录；若失败（跨卷、文件被锁等）则退化为递归复制+删除。
    /// </summary>
    private static void MoveDirectoryRobust(string sourceDir, string destDir)
    {
        // 先尝试原子重命名
        try
        {
            Directory.Move(sourceDir, destDir);
            return;
        }
        catch (IOException)
        {
            // 跨卷或目录非空导致移动失败时，退化为复制+删除
        }

        Directory.CreateDirectory(destDir);
        foreach (var dir in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, dir);
            Directory.CreateDirectory(Path.Combine(destDir, relative));
        }
        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, file);
            var destFile = Path.Combine(destDir, relative);
            File.Copy(file, destFile, overwrite: true);
        }
        Directory.Delete(sourceDir, recursive: true);
    }

    private sealed class RemoteVersionInfo
    {
        public string Version { get; set; } = "";
        public string? Description { get; set; }
    }
}

/// <summary>Web 前端更新信息（由 WebUpdateService.PeekAsync 返回）。</summary>
internal sealed class WebUpdateInfo
{
    public string? LocalVersion { get; set; }
    public string Version { get; set; } = string.Empty;
    public string? Description { get; set; }
}

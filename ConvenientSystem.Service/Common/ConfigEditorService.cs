using ConvenientSystem.Shared.Model.Common;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ConvenientSystem.Service.Common;

/// <summary>
/// 配置文件热编辑服务：白名单校验 + 自动备份 + 安全写入。
/// 可编辑文件仅限预定义白名单（通过构造函数注入），防止路径遍历攻击。
/// </summary>
public class ConfigEditorService : IConfigEditorService
{
    private readonly ILogger<ConfigEditorService> _logger;
    private readonly IHostApplicationLifetime _lifetime;

    /// <summary>应用根目录（即 exe 所在目录，与 appsettings.json 同级）。</summary>
    private readonly string _baseDir;

    /// <summary>服务启动时间。</summary>
    private readonly DateTime _startTime = DateTime.Now;

    /// <summary>可编辑配置文件白名单：{ 相对路径, 类型标签 }。</summary>
    private readonly (string Path, string Type)[] _whitelist;

    /// <summary>默认白名单：Api 服务器（部署目录含各环境配置）。</summary>
    public static readonly (string Path, string Type)[] DefaultWhitelist =
    [
        ("appsettings.json", "json"),
        ("appsettings.Development.json", "json"),
        ("appsettings.Production.json", "json"),
    ];

    public ConfigEditorService(
        ILogger<ConfigEditorService> logger,
        IHostApplicationLifetime lifetime,
        (string Path, string Type)[]? whitelist = null)
    {
        _logger = logger;
        _lifetime = lifetime;
        _baseDir = AppContext.BaseDirectory;
        _whitelist = whitelist ?? DefaultWhitelist;
    }

    /// <inheritdoc />
    public List<ConfigFileInfoDto> ListFiles()
    {
        var list = new List<ConfigFileInfoDto>();
        foreach (var (relPath, type) in _whitelist)
        {
            var fullPath = Path.Combine(_baseDir, relPath);
            if (!File.Exists(fullPath)) continue;

            var fi = new FileInfo(fullPath);
            list.Add(new ConfigFileInfoDto
            {
                Name = relPath,
                Type = type,
                Size = FormatSize(fi.Length),
                Modified = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm"),
            });
        }
        return list;
    }

    /// <inheritdoc />
    public ConfigFileContentDto ReadFile(string name)
    {
        var fullPath = ResolveAndValidate(name);
        var fi = new FileInfo(fullPath);
        return new ConfigFileContentDto
        {
            Name = name,
            Content = File.ReadAllText(fullPath),
            Modified = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm"),
        };
    }

    /// <inheritdoc />
    public void SaveFile(string name, string content)
    {
        var fullPath = ResolveAndValidate(name);

        // 自动备份：复制当前文件为 .bak（覆盖已有备份）
        var bakPath = fullPath + ".bak";
        if (File.Exists(fullPath))
        {
            File.Copy(fullPath, bakPath, overwrite: true);
            _logger.LogInformation("配置文件已备份: {Bak}", bakPath);
        }

        // 写入新内容
        File.WriteAllText(fullPath, content);
        _logger.LogInformation("配置文件已保存: {Name}", name);
    }

    /// <inheritdoc />
    public ConfigEditorStatusDto GetStatus()
    {
        return new ConfigEditorStatusDto
        {
            StartTime = _startTime.ToString("yyyy-MM-dd HH:mm:ss"),
            Restarting = false,
            BaseDirectory = _baseDir,
        };
    }

    /// <summary>
    /// 触发应用优雅停机（由 systemd/Docker/supervisor 等进程管理器自动拉起，等效于重启）。
    /// 前端收到 200 响应后再调用此方法，避免连接中断报错。
    /// </summary>
    public void TriggerRestart()
    {
        _logger.LogWarning("配置文件变更，触发服务重启...");
        Task.Delay(TimeSpan.FromSeconds(1)).ContinueWith(_ =>
        {
            _lifetime.StopApplication();
        });
    }

    /// <summary>
    /// 校验文件名在白名单内，返回完整物理路径。不在白名单或含路径穿越字符时抛异常。
    /// </summary>
    private string ResolveAndValidate(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("文件名不能为空");

        // 路径穿越检查
        if (name.Contains("..") || name.Contains('/') || name.Contains('\\'))
            throw new UnauthorizedAccessException($"非法文件名: {name}");

        // 白名单检查
        var matched = _whitelist.FirstOrDefault(w =>
            string.Equals(w.Path, name, StringComparison.OrdinalIgnoreCase));

        if (matched.Path == null)
            throw new UnauthorizedAccessException($"文件不在可编辑白名单中: {name}");

        var fullPath = Path.Combine(_baseDir, matched.Path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"配置文件不存在: {matched.Path}");

        return fullPath;
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }
}

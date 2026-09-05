using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ConvenientSystem.Desktop;

/// <summary>
/// Web 前端 UI 状态本地持久化：通用键值存储（key → JSON 字符串），
/// 落盘 exe 同目录的 ui-state.json（缩进格式，人类可读可备份）。
/// 存放通用构建页面的模板/卡片配置等非敏感数据（密码走 SshCredentialStore DPAPI 加密）。
/// 卸载程序不清理本文件（installer.iss 的 UninstallDelete 只删 wwwroot/logs），
/// 清理 WebView2 缓存、升级覆盖安装、卸载后重装均不丢数据。
/// </summary>
public sealed class UiStateStore
{
    private sealed class StoreFile
    {
        public Dictionary<string, string> States { get; set; } = new();
    }

    private static string StoreFilePath => Path.Combine(AppContext.BaseDirectory, "ui-state.json");

    private readonly ConcurrentDictionary<string, string> _states = new(StringComparer.Ordinal);
    private readonly ILogger<UiStateStore> _logger;
    private readonly object _saveLock = new();

    public UiStateStore(ILogger<UiStateStore> logger)
    {
        _logger = logger;
        Load();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(StoreFilePath)) return;
            var file = JsonSerializer.Deserialize<StoreFile>(File.ReadAllText(StoreFilePath));
            if (file?.States == null) return;
            foreach (var (key, value) in file.States)
                _states[key] = value;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "加载 ui-state.json 失败（文件损坏或无权限），将以空状态启动");
        }
    }

    private void Persist()
    {
        lock (_saveLock)
        {
            try
            {
                var json = JsonSerializer.Serialize(new StoreFile { States = new Dictionary<string, string>(_states) },
                    new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(StoreFilePath, json);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "写入 ui-state.json 失败（磁盘满或无权限）");
            }
        }
    }

    /// <summary>读取指定键的值；不存在返回 null。</summary>
    public string? Get(string key)
        => _states.TryGetValue(key, out var value) ? value : null;

    /// <summary>保存（覆盖）指定键。</summary>
    public void Set(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("key 不能为空");
        _states[key.Trim()] = value ?? string.Empty;
        Persist();
    }

    /// <summary>删除指定键；不存在返回 false。</summary>
    public bool Remove(string key)
    {
        var removed = _states.TryRemove(key, out _);
        if (removed) Persist();
        return removed;
    }
}

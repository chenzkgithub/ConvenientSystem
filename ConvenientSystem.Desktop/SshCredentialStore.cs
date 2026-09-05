using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ConvenientSystem.Desktop;

/// <summary>
/// SSH 凭据本地存储：密码经 DPAPI（当前用户作用域）加密后落盘 exe 同目录的
/// ssh-credentials.json，按 Host+UserName 索引。仅同一 Windows 用户可解密，
/// 文件拷贝到其他机器/用户无法还原出明文密码。
/// </summary>
public sealed class SshCredentialStore
{
    private sealed class Entry
    {
        public string Host { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string EncPassword { get; set; } = string.Empty;
    }

    private static string StoreFilePath => Path.Combine(AppContext.BaseDirectory, "ssh-credentials.json");
    // 熵值参与 DPAPI 密钥派生：换应用即无法解密（同一 Windows 用户也不行）
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ConvenientSystem.SshCredential.v1");

    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    private readonly ILogger<SshCredentialStore> _logger;
    private readonly object _saveLock = new();

    public SshCredentialStore(ILogger<SshCredentialStore> logger)
    {
        _logger = logger;
        Load();
    }

    private static string KeyOf(string host, string userName)
        => $"{host.Trim().ToLowerInvariant()}|{userName.Trim().ToLowerInvariant()}";

    /// <summary>保存（覆盖）一对 SSH 凭据。</summary>
    public void Save(string host, string userName, string password)
    {
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(userName) || password.Length == 0)
            throw new ArgumentException("主机、用户名与密码均不能为空");
        var enc = Convert.ToBase64String(ProtectedData.Protect(
            Encoding.UTF8.GetBytes(password), Entropy, DataProtectionScope.CurrentUser));
        _entries[KeyOf(host, userName)] = new Entry
        {
            Host = host.Trim(),
            UserName = userName.Trim(),
            EncPassword = enc,
        };
        Persist();
    }

    /// <summary>读取 SSH 密码；未保存过或解密失败返回 null。</summary>
    public string? Get(string host, string userName)
    {
        if (!_entries.TryGetValue(KeyOf(host, userName), out var entry)) return null;
        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(entry.EncPassword), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "解密 SSH 凭据失败（可能跨用户/跨机器迁移过）");
            return null;
        }
    }

    /// <summary>删除一对 SSH 凭据；不存在返回 false。</summary>
    public bool Remove(string host, string userName)
    {
        var removed = _entries.TryRemove(KeyOf(host, userName), out _);
        if (removed) Persist();
        return removed;
    }

    /// <summary>是否已保存指定凭据（用于前端提示，不返回密码）。</summary>
    public bool Exists(string host, string userName)
        => _entries.ContainsKey(KeyOf(host, userName));

    private void Load()
    {
        try
        {
            if (!File.Exists(StoreFilePath)) return;
            var list = JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(StoreFilePath)) ?? new();
            foreach (var e in list)
            {
                if (string.IsNullOrWhiteSpace(e.Host) || string.IsNullOrWhiteSpace(e.UserName)) continue;
                _entries[KeyOf(e.Host, e.UserName)] = e;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读取 SSH 凭据存储失败，从空存储开始");
        }
    }

    private void Persist()
    {
        try
        {
            lock (_saveLock)
            {
                File.WriteAllText(StoreFilePath, JsonSerializer.Serialize(_entries.Values.ToList()));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "保存 SSH 凭据存储失败");
        }
    }
}

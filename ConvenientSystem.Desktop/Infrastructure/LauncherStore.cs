using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ConvenientSystem;

/// <summary>
/// 启动器自定义条目存储：优先从数据库（用户级）读写，本地 JSON 文件作为离线缓存。
/// 未登录或 API 不可用时回退本地文件，保证断网仍可用。
/// </summary>
internal sealed class LauncherStore
{
    private readonly string _file;
    private readonly string _baseUrl;
    private readonly Func<string?> _getToken;
    private readonly List<LauncherCustomEntry> _entries = new();
    private readonly object _lock = new();
    private static readonly HttpClient Http = new();

    /// <summary>
    /// JSON 选项：与前端统一使用 camelCase，同时兼容旧版 PascalCase 本地缓存。
    /// </summary>
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public LauncherStore(string baseUrl, Func<string?> getToken)
    {
        _file = Path.Combine(Application.StartupPath, "launcher-items.json");
        _baseUrl = baseUrl.TrimEnd('/');
        _getToken = getToken;
        LoadLocal();
    }

    public IReadOnlyList<LauncherCustomEntry> Entries
    {
        get { lock (_lock) return _entries.ToList(); }
    }

    // ========== 本地文件读写（离线缓存） ==========

    private void LoadLocal()
    {
        try
        {
            if (!File.Exists(_file)) return;
            var json = File.ReadAllText(_file);
            var list = JsonSerializer.Deserialize<List<LauncherCustomEntry>>(json, JsonOpts);
            if (list is not null)
            {
                lock (_lock) { _entries.Clear(); _entries.AddRange(list); }
            }
        }
        catch { /* 损坏时忽略，返回空 */ }
    }

    private void SaveLocal()
    {
        try
        {
            lock (_lock)
            {
                var json = JsonSerializer.Serialize(_entries, JsonOpts);
                File.WriteAllText(_file, json);
            }
        }
        catch { /* 保存失败忽略 */ }
    }

    // ========== API 读写（用户级数据库） ==========

    /// <summary>从 API 拉取当前用户的启动器条目，覆盖本地缓存。</summary>
    public void ReloadFromApi()
    {
        var token = _getToken();
        if (string.IsNullOrEmpty(token)) return;

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/api/Common/UserConfig/GetLauncherItems");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var resp = Http.Send(req);
            if (!resp.IsSuccessStatusCode) return;

            // API 直接返回 JSON 数组（或 null），无需二次解析字符串。
            var body = resp.Content.ReadAsStringAsync().Result;
            if (string.IsNullOrWhiteSpace(body) || body.Trim() == "null") return;

            var list = JsonSerializer.Deserialize<List<LauncherCustomEntry>>(body, JsonOpts);
            if (list is not null)
            {
                lock (_lock) { _entries.Clear(); _entries.AddRange(list); }
                SaveLocal();
            }
        }
        catch { /* API 不可用时保持本地数据 */ }
    }

    private void SaveToApi()
    {
        var token = _getToken();
        if (string.IsNullOrEmpty(token)) return;

        try
        {
            string json;
            lock (_lock) { json = JsonSerializer.Serialize(_entries, JsonOpts); }

            using var req = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/Common/UserConfig/SaveLauncherItems");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = new StringContent(JsonSerializer.Serialize(json), Encoding.UTF8, "application/json");
            Http.Send(req);
        }
        catch { /* API 保存失败不影响本地 */ }
    }

    // ========== 公共操作 ==========

    public void Save()
    {
        SaveLocal();
        SaveToApi();
    }

    public void Add(LauncherCustomEntry entry)
    {
        lock (_lock) _entries.Add(entry);
        Save();
    }

    public void Remove(LauncherCustomEntry entry)
    {
        lock (_lock) _entries.RemoveAll(e => e.Title == entry.Title && e.Target == entry.Target);
        Save();
    }

    /// <summary>替换全部条目（编辑器批量保存用）。</summary>
    public void ReplaceAll(IEnumerable<LauncherCustomEntry> entries)
    {
        lock (_lock)
        {
            _entries.Clear();
            _entries.AddRange(entries);
        }
        Save();
    }
}

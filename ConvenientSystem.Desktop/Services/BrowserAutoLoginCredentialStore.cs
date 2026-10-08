using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ConvenientSystem;

/// <summary>按网站地址维护自动登录凭据，密码仅以当前 Windows 用户可解密的 DPAPI 密文落盘。</summary>
public sealed class BrowserAutoLoginCredentialStore
{
    public const string ErpAdapterType = "erp";
    public const string AliyunAdapterType = "aliyun";

    private sealed class StoreDocument
    {
        public int Version { get; set; } = 3;
        public List<Entry> Sites { get; set; } = [];
    }

    private sealed class Entry
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string BaseAddress { get; set; } = string.Empty;
        public string AdapterType { get; set; } = ErpAdapterType;
        public string UserName { get; set; } = string.Empty;
        public string EncPassword { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;
        public bool MatchAnyPort { get; set; }
    }

    private sealed class LegacyEntry
    {
        public string UserName { get; set; } = string.Empty;
        public string EncPassword { get; set; } = string.Empty;
    }

    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ConvenientSystem.BrowserAutoLogin.v1");
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };
    private static string StoreFilePath => Path.Combine(AppContext.BaseDirectory, "browser-auto-login.json");

    private readonly ILogger<BrowserAutoLoginCredentialStore> _logger;
    private readonly object _syncRoot = new();
    private StoreDocument _document = new();
    private LegacyEntry? _legacyEntry;

    public BrowserAutoLoginCredentialStore(ILogger<BrowserAutoLoginCredentialStore> logger)
    {
        _logger = logger;
        Load();
    }

    public IReadOnlyList<BrowserAutoLoginSiteCredential> GetAll()
    {
        lock (_syncRoot)
        {
            return _document.Sites
                .Select(Decrypt)
                .OrderBy(site => site.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
    }

    public BrowserAutoLoginSiteCredential? FindForTarget(Uri target)
    {
        lock (_syncRoot)
        {
            var entry = _document.Sites.FirstOrDefault(site =>
                site.Enabled && !site.MatchAnyPort && MatchesTarget(site, target));
            entry ??= _document.Sites.FirstOrDefault(site =>
                site.Enabled && site.MatchAnyPort && MatchesTarget(site, target));
            if (entry is null) return null;

            var credential = Decrypt(entry);
            return credential.CredentialsAvailable ? credential : null;
        }
    }

    public BrowserAutoLoginCredential? GetLegacyCredential()
    {
        lock (_syncRoot)
        {
            if (_legacyEntry is null) return null;
            try
            {
                return new BrowserAutoLoginCredential(
                    _legacyEntry.UserName,
                    DecryptPassword(_legacyEntry.EncPassword));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "解密旧版浏览器自动登录凭据失败");
                return null;
            }
        }
    }

    public void Save(BrowserAutoLoginSiteCredential site)
    {
        if (!TryNormalizeBaseAddress(site.BaseAddress, out var baseAddress))
            throw new ArgumentException("网站地址必须是有效的 HTTP 或 HTTPS 地址。");
        if (string.IsNullOrWhiteSpace(site.Name))
            throw new ArgumentException("网站名称不能为空。");
        if (string.IsNullOrWhiteSpace(site.UserName) || string.IsNullOrEmpty(site.Password))
            throw new ArgumentException("账号和密码不能为空。");

        var entry = new Entry
        {
            Id = site.Id == Guid.Empty ? Guid.NewGuid() : site.Id,
            Name = site.Name.Trim(),
            BaseAddress = baseAddress,
            AdapterType = string.IsNullOrWhiteSpace(site.AdapterType) ? ErpAdapterType : site.AdapterType,
            UserName = site.UserName.Trim(),
            EncPassword = EncryptPassword(site.Password),
            Enabled = site.Enabled,
            MatchAnyPort = site.MatchAnyPort,
        };

        lock (_syncRoot)
        {
            var duplicate = _document.Sites.FirstOrDefault(item =>
                item.Id != entry.Id && HasSameMatchRule(item, entry));
            if (duplicate is not null)
                throw new InvalidOperationException("该网站地址已经配置，请直接编辑现有记录。");

            var sites = _document.Sites.ToList();
            var index = sites.FindIndex(item => item.Id == entry.Id);
            if (index >= 0)
                sites[index] = entry;
            else
                sites.Add(entry);

            var updated = new StoreDocument { Sites = sites };
            Persist(updated);
            _document = updated;
            _legacyEntry = null;
        }
    }

    public void Delete(Guid id)
    {
        lock (_syncRoot)
        {
            var sites = _document.Sites.Where(site => site.Id != id).ToList();
            if (sites.Count == _document.Sites.Count) return;

            var updated = new StoreDocument { Sites = sites };
            Persist(updated);
            _document = updated;
        }
    }

    public static bool TryNormalizeBaseAddress(string value, out string baseAddress)
        => TryNormalizeSiteAddress(value, out baseAddress, out _);

    public static bool TryNormalizeSiteAddress(
        string value,
        out string baseAddress,
        out bool matchAnyPort)
    {
        baseAddress = string.Empty;
        matchAnyPort = false;
        var trimmed = value.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        var explicitPort = HasExplicitPort(trimmed);
        var host = uri.HostNameType == UriHostNameType.IPv6 ? $"[{uri.Host}]" : uri.IdnHost;
        baseAddress = $"{uri.Scheme}://{host}";
        if (explicitPort) baseAddress += $":{uri.Port}";
        matchAnyPort = !explicitPort;
        return true;
    }

    private static bool HasExplicitPort(string value)
    {
        var schemeEnd = value.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd < 0) return false;

        var authorityStart = schemeEnd + 3;
        var authorityEnd = value.IndexOfAny(['/', '?', '#'], authorityStart);
        var authority = authorityEnd < 0
            ? value[authorityStart..]
            : value[authorityStart..authorityEnd];
        if (authority.StartsWith('['))
        {
            var bracket = authority.IndexOf(']');
            return bracket >= 0 && bracket + 1 < authority.Length && authority[bracket + 1] == ':';
        }

        var colon = authority.LastIndexOf(':');
        return colon >= 0 && colon + 1 < authority.Length &&
               authority[(colon + 1)..].All(char.IsDigit);
    }

    private static bool MatchesTarget(Entry entry, Uri target)
    {
        if (!Uri.TryCreate(entry.BaseAddress, UriKind.Absolute, out var configured)) return false;
        if (!configured.Scheme.Equals(target.Scheme, StringComparison.OrdinalIgnoreCase) ||
            !configured.Host.Equals(target.Host, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return entry.MatchAnyPort || configured.Port == target.Port;
    }

    private static bool HasSameMatchRule(Entry left, Entry right)
    {
        if (!Uri.TryCreate(left.BaseAddress, UriKind.Absolute, out var leftUri) ||
            !Uri.TryCreate(right.BaseAddress, UriKind.Absolute, out var rightUri))
        {
            return left.BaseAddress.Equals(right.BaseAddress, StringComparison.OrdinalIgnoreCase) &&
                   left.MatchAnyPort == right.MatchAnyPort;
        }

        if (!leftUri.Scheme.Equals(rightUri.Scheme, StringComparison.OrdinalIgnoreCase) ||
            !leftUri.Host.Equals(rightUri.Host, StringComparison.OrdinalIgnoreCase) ||
            left.MatchAnyPort != right.MatchAnyPort)
        {
            return false;
        }
        return left.MatchAnyPort || leftUri.Port == rightUri.Port;
    }

    private BrowserAutoLoginSiteCredential Decrypt(Entry entry)
    {
        try
        {
            return new BrowserAutoLoginSiteCredential(
                entry.Id,
                entry.Name,
                entry.BaseAddress,
                entry.AdapterType,
                entry.UserName,
                DecryptPassword(entry.EncPassword),
                entry.Enabled,
                entry.MatchAnyPort);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "解密网站 {SiteName} 的自动登录凭据失败", entry.Name);
            return new BrowserAutoLoginSiteCredential(
                entry.Id,
                entry.Name,
                entry.BaseAddress,
                entry.AdapterType,
                entry.UserName,
                string.Empty,
                entry.Enabled,
                entry.MatchAnyPort,
                false);
        }
    }

    private static string EncryptPassword(string password)
        => Convert.ToBase64String(ProtectedData.Protect(
            Encoding.UTF8.GetBytes(password), Entropy, DataProtectionScope.CurrentUser));

    private static string DecryptPassword(string encryptedPassword)
        => Encoding.UTF8.GetString(ProtectedData.Unprotect(
            Convert.FromBase64String(encryptedPassword), Entropy, DataProtectionScope.CurrentUser));

    private void Load()
    {
        try
        {
            if (!File.Exists(StoreFilePath)) return;

            var json = File.ReadAllText(StoreFilePath);
            using var parsed = JsonDocument.Parse(json);
            if (parsed.RootElement.TryGetProperty("Sites", out var sitesElement))
            {
                var persistedSites = sitesElement.EnumerateArray().ToArray();
                _document = JsonSerializer.Deserialize<StoreDocument>(json, JsonOptions) ?? new StoreDocument();
                for (var index = 0; index < _document.Sites.Count; index++)
                {
                    var site = _document.Sites[index];
                    if (site.Id == Guid.Empty) site.Id = Guid.NewGuid();
                    if (TryNormalizeSiteAddress(site.BaseAddress, out var normalized, out var matchAnyPort))
                    {
                        site.BaseAddress = normalized;
                        if (index >= persistedSites.Length ||
                            !persistedSites[index].TryGetProperty("MatchAnyPort", out _))
                        {
                            site.MatchAnyPort = matchAnyPort;
                        }
                    }
                }
                _document.Sites.RemoveAll(site => string.IsNullOrWhiteSpace(site.BaseAddress));
                return;
            }

            _legacyEntry = JsonSerializer.Deserialize<LegacyEntry>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读取浏览器自动登录凭据失败");
            _document = new StoreDocument();
            _legacyEntry = null;
        }
    }

    private void Persist(StoreDocument document)
    {
        var temporaryPath = StoreFilePath + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(document, JsonOptions));
            File.Move(temporaryPath, StoreFilePath, true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存浏览器自动登录凭据失败");
            throw;
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}

/// <summary>一个网站地址唯一对应的一组自动登录配置。</summary>
public sealed record BrowserAutoLoginSiteCredential(
    Guid Id,
    string Name,
    string BaseAddress,
    string AdapterType,
    string UserName,
    string Password,
    bool Enabled,
    bool MatchAnyPort,
    bool CredentialsAvailable = true);

/// <summary>旧版单账号配置，仅用于首次迁移时回填。</summary>
public sealed record BrowserAutoLoginCredential(string UserName, string Password);

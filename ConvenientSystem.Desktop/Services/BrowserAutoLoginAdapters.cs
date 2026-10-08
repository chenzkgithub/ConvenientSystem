using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ConvenientSystem;

/// <summary>定义一种网站登录方式，每个网站配置通过 AdapterType 选择对应实现。</summary>
public interface IBrowserAutoLoginAdapter
{
    string AdapterType { get; }
    string DisplayName { get; }
    bool CanHandle(Uri target);
    Task<BrowserAutoLoginAttemptResult> LoginAsync(
        Uri target,
        BrowserAutoLoginSiteCredential credential,
        Func<string, string?>? requestVerifyCode,
        CancellationToken cancellationToken);
}

/// <summary>站点适配器单次登录结果，凭据错误时由协调器决定是否打开配置界面重试。</summary>
public sealed record BrowserAutoLoginAttemptResult(
    BrowserAutoLoginResult Result,
    bool CredentialsRejected = false);

/// <summary>向阿里云登录页发送账号密码填充指令，验证码和二次验证仍由用户完成。</summary>
public sealed class AliyunBrowserAutoLoginAdapter : IBrowserAutoLoginAdapter
{
    public string AdapterType => BrowserAutoLoginCredentialStore.AliyunAdapterType;
    public string DisplayName => "阿里云账号密码登录";

    public bool CanHandle(Uri target)
        => target.Host.Equals("account.aliyun.com", StringComparison.OrdinalIgnoreCase) &&
           target.AbsolutePath.StartsWith("/login/", StringComparison.OrdinalIgnoreCase);

    public Task<BrowserAutoLoginAttemptResult> LoginAsync(
        Uri target,
        BrowserAutoLoginSiteCredential credential,
        Func<string, string?>? requestVerifyCode,
        CancellationToken cancellationToken)
    {
        var result = BrowserAutoLoginResult.FillPage(
            new BrowserAutoLoginPageAction(AdapterType, credential.UserName, credential.Password),
            "阿里云登录已提交，如出现安全验证请按页面提示完成。");
        return Task.FromResult(new BrowserAutoLoginAttemptResult(result));
    }
}

/// <summary>通过 ERP 验证码及密码加密接口完成登录。</summary>
public sealed class ErpBrowserAutoLoginAdapter : IBrowserAutoLoginAdapter
{
    private readonly BrowserAutoLoginOcrService _ocrService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ErpBrowserAutoLoginAdapter> _logger;

    public ErpBrowserAutoLoginAdapter(
        BrowserAutoLoginOcrService ocrService,
        IHttpClientFactory httpClientFactory,
        ILogger<ErpBrowserAutoLoginAdapter> logger)
    {
        _ocrService = ocrService;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public string AdapterType => BrowserAutoLoginCredentialStore.ErpAdapterType;
    public string DisplayName => "ERP账号密码登录";

    public bool CanHandle(Uri target)
    {
        var loginPath = target.AbsolutePath.TrimEnd('/');
        return loginPath.Equals("/login", StringComparison.OrdinalIgnoreCase) ||
               loginPath.Equals("/rebotlogin", StringComparison.OrdinalIgnoreCase) ||
               loginPath.Equals("/qrlogin", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<BrowserAutoLoginAttemptResult> LoginAsync(
        Uri target,
        BrowserAutoLoginSiteCredential credential,
        Func<string, string?>? requestVerifyCode,
        CancellationToken cancellationToken)
    {
        var apiBase = new Uri(target.GetLeftPart(UriPartial.Authority) + "/api/admin/auth/");
        var automaticImageAttempts = _ocrService.IsAvailable ? 3 : 0;

        try
        {
            for (var imageAttempt = 0; imageAttempt <= automaticImageAttempts; imageAttempt++)
            {
                var verify = await GetVerifyCodeAsync(apiBase, cancellationToken);
                var useManualVerifyCode = imageAttempt == automaticImageAttempts;
                IReadOnlyList<string> verifyCodes;
                if (useManualVerifyCode)
                {
                    var manualCode = requestVerifyCode?.Invoke(verify.ImageBase64);
                    if (string.IsNullOrWhiteSpace(manualCode))
                        return new(BrowserAutoLoginResult.Fail("已取消自动登录。"));
                    verifyCodes = [manualCode];
                }
                else
                {
                    verifyCodes = await _ocrService.RecognizeCandidatesAsync(verify.ImageBase64, cancellationToken);
                    if (verifyCodes.Count == 0) continue;
                }

                var passwordKey = await GetPasswordKeyAsync(apiBase, cancellationToken);
                var encryptedPassword = EncryptPassword(credential.Password, passwordKey.EncryptKey);
                var lastMessage = string.Empty;

                foreach (var verifyCode in verifyCodes)
                {
                    var login = await SubmitLoginAsync(
                        apiBase,
                        credential.UserName,
                        encryptedPassword,
                        passwordKey.Key,
                        verifyCode,
                        verify.Key,
                        cancellationToken);
                    lastMessage = login.Message;

                    if (login.Success && !string.IsNullOrWhiteSpace(login.Token))
                    {
                        return new(BrowserAutoLoginResult.Ok(
                            login.Token,
                            new Uri(target.GetLeftPart(UriPartial.Authority) + "/").AbsoluteUri));
                    }

                    if (login.ErrorCode == 2) continue;
                    if (login.ErrorCode == 1) break;
                    if (login.ErrorCode is 3 or 4)
                    {
                        return new(
                            BrowserAutoLoginResult.Fail(login.Message.Length > 0 ? login.Message : "ERP 账号或密码错误。"),
                            true);
                    }

                    return new(BrowserAutoLoginResult.Fail(
                        login.Message.Length > 0 ? login.Message : "ERP 登录失败。"));
                }

                if (useManualVerifyCode)
                {
                    return new(BrowserAutoLoginResult.Fail(
                        lastMessage.Length > 0 ? lastMessage : "验证码输入有误。"));
                }
            }

            return new(BrowserAutoLoginResult.Fail("验证码识别失败，请重新触发自动登录。"));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(BrowserAutoLoginResult.Fail("自动登录已取消。"));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "浏览器 ERP 自动登录失败，目标站点：{Host}", target.Host);
            return new(BrowserAutoLoginResult.Fail($"连接 ERP 登录接口失败：{ex.Message}"));
        }
    }

    private async Task<VerifyCodeChallenge> GetVerifyCodeAsync(Uri apiBase, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("BrowserAutoLogin");
        using var response = await client.GetAsync(new Uri(apiBase, "getverifycode?lastKey="), cancellationToken);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        var data = GetSuccessfulData(json.RootElement);
        return new VerifyCodeChallenge(
            GetRequiredString(data, "key"),
            GetRequiredString(data, "img"));
    }

    private async Task<PasswordKeyChallenge> GetPasswordKeyAsync(Uri apiBase, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("BrowserAutoLogin");
        using var response = await client.GetAsync(new Uri(apiBase, "getPassWordEncryptKey"), cancellationToken);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        var data = GetSuccessfulData(json.RootElement);
        return new PasswordKeyChallenge(
            GetRequiredString(data, "key"),
            GetRequiredString(data, "encyptKey"));
    }

    private async Task<LoginResponse> SubmitLoginAsync(
        Uri apiBase,
        string userName,
        string encryptedPassword,
        string passwordKey,
        string verifyCode,
        string verifyCodeKey,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new
        {
            corpId = string.Empty,
            userName,
            password = encryptedPassword,
            passwordKey,
            verifyCode,
            verifyCodeKey,
        });

        var client = _httpClientFactory.CreateClient("BrowserAutoLogin");
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(new Uri(apiBase, "login"), content, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        var root = json.RootElement;
        var success = TryGetProperty(root, "success", out var successElement) && successElement.GetBoolean();
        var message = TryGetProperty(root, "msg", out var messageElement)
            ? messageElement.GetString() ?? string.Empty
            : string.Empty;

        if (success && TryGetProperty(root, "data", out var data) &&
            TryGetProperty(data, "token", out var tokenElement))
        {
            return new LoginResponse(true, tokenElement.GetString(), message, null);
        }

        int? errorCode = null;
        if (TryGetProperty(root, "data", out var errorData) && errorData.ValueKind == JsonValueKind.Number &&
            errorData.TryGetInt32(out var parsedCode))
        {
            errorCode = parsedCode;
        }
        return new LoginResponse(false, null, message, errorCode);
    }

    private static JsonElement GetSuccessfulData(JsonElement root)
    {
        if (!TryGetProperty(root, "success", out var success) || !success.GetBoolean())
        {
            var message = TryGetProperty(root, "msg", out var msg) ? msg.GetString() : null;
            throw new InvalidOperationException(message ?? "ERP 接口返回失败。");
        }
        if (!TryGetProperty(root, "data", out var data))
            throw new InvalidOperationException("ERP 接口未返回数据。");
        return data;
    }

    private static string GetRequiredString(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var value) || string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidOperationException($"ERP 接口缺少 {propertyName}。");
        return value.GetString()!;
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    private static string EncryptPassword(string password, string key)
    {
        var keyBytes = Encoding.UTF8.GetBytes(key);
        if (keyBytes.Length != 8)
            throw new InvalidOperationException("ERP 返回的密码加密密钥格式无效。");

        using var des = DES.Create();
        des.Mode = CipherMode.ECB;
        des.Padding = PaddingMode.PKCS7;
        des.Key = keyBytes;
        using var encryptor = des.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(password);
        return Convert.ToBase64String(encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length));
    }

    private sealed record VerifyCodeChallenge(string Key, string ImageBase64);
    private sealed record PasswordKeyChallenge(string Key, string EncryptKey);
    private sealed record LoginResponse(bool Success, string? Token, string Message, int? ErrorCode);
}

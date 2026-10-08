using Microsoft.Extensions.Logging;

namespace ConvenientSystem;

/// <summary>根据当前网站配置选择登录适配器，并协调配置补录与凭据错误重试。</summary>
public sealed class BrowserAutoLoginCoordinator
{
    private readonly BrowserAutoLoginCredentialStore _credentialStore;
    private readonly IReadOnlyDictionary<string, IBrowserAutoLoginAdapter> _adapters;
    private readonly ILogger<BrowserAutoLoginCoordinator> _logger;
    private readonly SemaphoreSlim _loginGate = new(1, 1);
    private Func<string, bool>? _showCredentialSettings;
    private Func<string, string?>? _requestVerifyCode;

    public BrowserAutoLoginCoordinator(
        BrowserAutoLoginCredentialStore credentialStore,
        IEnumerable<IBrowserAutoLoginAdapter> adapters,
        ILogger<BrowserAutoLoginCoordinator> logger)
    {
        _credentialStore = credentialStore;
        var adapterList = adapters.ToArray();
        _adapters = adapterList.ToDictionary(adapter => adapter.AdapterType, StringComparer.OrdinalIgnoreCase);
        SupportedAdapters = adapterList
            .Select(adapter => new BrowserAutoLoginAdapterOption(adapter.AdapterType, adapter.DisplayName))
            .ToArray();
        _logger = logger;
    }

    public IReadOnlyList<BrowserAutoLoginAdapterOption> SupportedAdapters { get; }

    public void ConfigureUi(Func<string, bool> showCredentialSettings, Func<string, string?> requestVerifyCode)
    {
        _showCredentialSettings = showCredentialSettings;
        _requestVerifyCode = requestVerifyCode;
    }

    public async Task<BrowserAutoLoginResult> LoginAsync(string targetUrl, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return BrowserAutoLoginResult.Fail("自动登录已取消。");
        if (!_loginGate.Wait(0))
            return BrowserAutoLoginResult.Fail("已有自动登录正在处理中，请先完成当前操作。");

        try
        {
            return await LoginCoreAsync(targetUrl, cancellationToken);
        }
        finally
        {
            _loginGate.Release();
        }
    }

    private async Task<BrowserAutoLoginResult> LoginCoreAsync(
        string targetUrl,
        CancellationToken cancellationToken)
    {
        if (!TryResolveTarget(targetUrl, out var target))
            return BrowserAutoLoginResult.Fail("当前页面地址无效，无法自动登录。");

        var site = _credentialStore.FindForTarget(target);
        if (site is null)
        {
            var configured = _showCredentialSettings?.Invoke(target.GetLeftPart(UriPartial.Authority)) ?? false;
            if (!configured)
                return BrowserAutoLoginResult.Fail("当前网站尚未配置自动登录账号密码。");
            site = _credentialStore.FindForTarget(target);
        }

        if (site is null)
            return BrowserAutoLoginResult.Fail("未找到与当前网站地址匹配的自动登录配置。");

        if (!_adapters.TryGetValue(site.AdapterType, out var adapter))
            return BrowserAutoLoginResult.Fail($"当前网站配置的登录方式不受支持：{site.AdapterType}");
        if (!adapter.CanHandle(target))
            return BrowserAutoLoginResult.Fail("请在该网站的登录页面使用自动登录功能。");

        try
        {
            var attempt = await adapter.LoginAsync(target, site, _requestVerifyCode, cancellationToken);
            if (!attempt.CredentialsRejected)
                return attempt.Result;

            var updated = _showCredentialSettings?.Invoke(target.GetLeftPart(UriPartial.Authority)) ?? false;
            if (!updated)
                return attempt.Result;

            site = _credentialStore.FindForTarget(target);
            if (site is null)
                return BrowserAutoLoginResult.Fail("自动登录配置已被删除。");
            if (!_adapters.TryGetValue(site.AdapterType, out adapter) || !adapter.CanHandle(target))
                return BrowserAutoLoginResult.Fail("更新后的登录方式与当前页面不匹配。");

            return (await adapter.LoginAsync(target, site, _requestVerifyCode, cancellationToken)).Result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return BrowserAutoLoginResult.Fail("自动登录已取消。");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "网站自动登录失败，目标站点：{Host}", target.Host);
            return BrowserAutoLoginResult.Fail($"自动登录失败：{ex.Message}");
        }
    }

    private static bool TryResolveTarget(string targetUrl, out Uri target)
    {
        return Uri.TryCreate(targetUrl, UriKind.Absolute, out target!) &&
               (target.Scheme == Uri.UriSchemeHttp || target.Scheme == Uri.UriSchemeHttps);
    }
}

public sealed record BrowserAutoLoginAdapterOption(
    string AdapterType,
    string DisplayName);

public sealed record BrowserAutoLoginPageAction(
    string AdapterType,
    string UserName,
    string Password);

public sealed record BrowserAutoLoginResult(
    bool Success,
    string Message,
    string? Token = null,
    string CookieName = "token",
    string? CookieUrl = null,
    BrowserAutoLoginPageAction? PageAction = null)
{
    public static BrowserAutoLoginResult Ok(string token, string cookieUrl)
        => new(true, "登录成功，正在刷新页面。", token, "token", cookieUrl);

    public static BrowserAutoLoginResult FillPage(BrowserAutoLoginPageAction pageAction, string message)
        => new(true, message, PageAction: pageAction);

    public static BrowserAutoLoginResult Fail(string message)
        => new(false, message);
}

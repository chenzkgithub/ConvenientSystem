namespace ConvenientSystem;

/// <summary>为 <see cref="LauncherStore"/> 提供运行时依赖的工厂（基址与 JWT 获取器）。</summary>
internal sealed class LauncherStoreFactory
{
    private readonly WebHostService _webHost;

    public LauncherStoreFactory(WebHostService webHost)
    {
        _webHost = webHost;
    }

    public LauncherStore Create()
    {
        return new LauncherStore(_webHost.BaseUrl, () => LockCoordinator.CachedJwt);
    }
}

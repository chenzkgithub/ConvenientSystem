namespace ConvenientSystem;

/// <summary>桌面端 appsettings.json → AppSettings 节点的强类型配置。</summary>
public sealed class AppSettings
{
    /// <summary>远程更新服务器地址（不含 http:// 协议头）。</summary>
    public string RemoteServerUrl { get; set; } = string.Empty;

    /// <summary>桌面程序版本（仅作展示/排查，真实版本以程序集版本为准）。</summary>
    public string DesktopVersion { get; set; } = string.Empty;
}

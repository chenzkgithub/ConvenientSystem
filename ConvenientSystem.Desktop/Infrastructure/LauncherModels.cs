namespace ConvenientSystem;

/// <summary>启动器结果项类型。</summary>
internal enum LauncherItemType
{
    App,      // 本机程序/快捷方式
    Custom,   // 用户自定义条目（网址/文档/命令）
    Page,     // 系统内页面
    File      // 本地文件（第二阶段）
}

/// <summary>启动器统一搜索结果项。</summary>
internal sealed class LauncherItem
{
    public LauncherItemType Type { get; set; }
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string Target { get; set; } = "";
    public bool External { get; set; }
    public Image? Icon { get; set; }
    /// <summary>自定义条目类型：url | file | command。仅 Custom 类型使用。</summary>
    public string Kind { get; set; } = "";
}

/// <summary>用户自定义启动条目（持久化到 JSON）。</summary>
internal sealed class LauncherCustomEntry
{
    public string Title { get; set; } = "";
    public string Target { get; set; } = "";
    /// <summary>url | file | command</summary>
    public string Kind { get; set; } = "url";
}

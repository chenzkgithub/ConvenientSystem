namespace ConvenientSystem.Shared.Model.Common;

/// <summary>配置文件列表项 DTO。</summary>
public class ConfigFileInfoDto
{
    /// <summary>文件名（相对路径，如 appsettings.json）。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>文件类型标签（json / env / conf / yaml 等），用于前端彩色图标。</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>文件大小（人类可读，如 "1.2 KB"）。</summary>
    public string Size { get; set; } = string.Empty;

    /// <summary>最后修改时间（格式化字符串）。</summary>
    public string Modified { get; set; } = string.Empty;
}

/// <summary>配置文件内容 DTO。</summary>
public class ConfigFileContentDto
{
    /// <summary>文件名。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>文件完整文本内容。</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>最后修改时间。</summary>
    public string Modified { get; set; } = string.Empty;
}

/// <summary>服务状态 DTO。</summary>
public class ConfigEditorStatusDto
{
    /// <summary>服务启动时间。</summary>
    public string StartTime { get; set; } = string.Empty;

    /// <summary>是否正在重启中。</summary>
    public bool Restarting { get; set; }

    /// <summary>配置文件所在目录（绝对路径），用于前端展示“配置来源”。</summary>
    public string BaseDirectory { get; set; } = string.Empty;
}

/// <summary>保存配置文件请求 DTO。</summary>
public class ConfigFileSaveDto
{
    /// <summary>文件名（必须在白名单内）。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>新文件完整文本内容。</summary>
    public string Content { get; set; } = string.Empty;
}

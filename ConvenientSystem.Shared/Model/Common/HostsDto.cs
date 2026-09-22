namespace ConvenientSystem.Shared.Model.Common;

/// <summary>Hosts 文件中的一行（三类：entry 条目 / group 分组标记 / other 原样保留行）。</summary>
public class HostsLineDto
{
    /// <summary>行类型：entry（IP 映射条目）| group（# ===== 名 ===== 分组标记）| other（注释/空行，原样保留）。</summary>
    public string Kind { get; set; } = "other";

    /// <summary>other 行：原文，保存时原样写回。</summary>
    public string? Raw { get; set; }

    /// <summary>entry：IP 地址。</summary>
    public string? Ip { get; set; }

    /// <summary>entry：映射的域名列表（一行可挂多个域名）。</summary>
    public List<string> Domains { get; set; } = new();

    /// <summary>entry：是否启用；false 表示整行被 # 注释掉。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>entry：行尾备注（# 之后的内容）。</summary>
    public string? Comment { get; set; }

    /// <summary>group：分组名；entry：其上方最近的分组标记（仅展示用，保存时以行位置为准）。</summary>
    public string? Group { get; set; }
}

/// <summary>Hosts 文件完整视图。</summary>
public class HostsFileDto
{
    /// <summary>hosts 文件完整路径。</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>当前进程是否可写（写 hosts 需要管理员身份，不可写时编辑操作将失败）。</summary>
    public bool Writable { get; set; }

    /// <summary>全量行列表（按文件顺序）。</summary>
    public List<HostsLineDto> Lines { get; set; } = new();

    /// <summary>有效条目行数。</summary>
    public int EntryCount { get; set; }

    /// <summary>原样保留行数（注释/空行）。</summary>
    public int OtherLineCount { get; set; }
}

/// <summary>Hosts 保存请求：全量行往返，条目区由前端维护顺序。</summary>
public class HostsSaveDto
{
    public List<HostsLineDto> Lines { get; set; } = new();
}

/// <summary>Hosts 备份文件信息。</summary>
public class HostsBackupDto
{
    /// <summary>备份文件名（hosts.bak.*）。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>备份时间（yyyy-MM-dd HH:mm:ss）。</summary>
    public string Modified { get; set; } = string.Empty;

    /// <summary>文件大小（字节）。</summary>
    public long Size { get; set; }
}

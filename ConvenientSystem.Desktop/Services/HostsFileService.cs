using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem;

/// <summary>
/// 本机 Hosts 文件管理服务：解析 / 序列化 / 备份 / 还原 C:\Windows\System32\drivers\etc\hosts。
/// 仅在桌面端内嵌 Kestrel 中可用（操作的是运行进程所在机器的 hosts）。
/// 全量行往返协议：解析时把文件拆为 entry / group / other 三类行，保存时按前端回传的行列表重建文本，
/// other 行（手工注释、空行）原样保留，避免破坏用户在文件里手工维护的内容。
/// </summary>
public sealed class HostsFileService
{
    /// <summary>hosts 文件完整路径。</summary>
    public string HostsPath => Path.Combine(Environment.SystemDirectory, "drivers", "etc", "hosts");

    /// <summary>备份文件前缀：hosts.bak.yyyyMMdd-HHmmss。</summary>
    private const string BackupPrefix = "hosts.bak.";

    /// <summary>自动备份保留份数，超出按时间淘汰最旧。</summary>
    private const int MaxBackups = 20;

    /// <summary>分组标记行：# ===== 名称 =====（等号数量不敏感，名称允许中英文/空格）。</summary>
    private static readonly Regex GroupRegex = new(@"^#\s*=+\s*(?<name>.+?)\s*=+\s*$", RegexOptions.Compiled);

    // ===================== 读取 =====================

    /// <summary>读取并解析 hosts 文件。</summary>
    public HostsFileDto List()
    {
        var rawLines = File.ReadAllLines(HostsPath);
        var lines = new List<HostsLineDto>();
        string? currentGroup = null;

        foreach (var raw in rawLines)
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                lines.Add(new HostsLineDto { Kind = "other", Raw = raw });
                continue;
            }

            if (line.StartsWith('#'))
            {
                var groupMatch = GroupRegex.Match(line);
                if (groupMatch.Success)
                {
                    currentGroup = groupMatch.Groups["name"].Value.Trim();
                    lines.Add(new HostsLineDto { Kind = "group", Group = currentGroup, Raw = raw });
                }
                else if (TryParseEntry(line, currentGroup, out var commentedEntry))
                {
                    // 被整行注释掉的条目（# 127.0.0.1 example.com）：还原为禁用条目
                    commentedEntry.Raw = raw;
                    lines.Add(commentedEntry);
                }
                else
                {
                    lines.Add(new HostsLineDto { Kind = "other", Raw = raw });
                }
                continue;
            }

            if (TryParseEntry(line, currentGroup, out var entry))
            {
                entry.Raw = raw;
                entry.Enabled = true;
                lines.Add(entry);
            }
            else
            {
                // 非 IP 开头的行（损坏行、未知内容）：按原样保留，保存时不丢
                lines.Add(new HostsLineDto { Kind = "other", Raw = raw });
            }
        }

        return new HostsFileDto
        {
            Path = HostsPath,
            Writable = CheckWritable(),
            Lines = lines,
            EntryCount = lines.Count(l => l.Kind == "entry"),
            OtherLineCount = lines.Count(l => l.Kind == "other"),
        };
    }

    /// <summary>解析条目行（含行首带 # 的注释条目）。body 为一行的有效内容。</summary>
    private static bool TryParseEntry(string line, string? currentGroup, out HostsLineDto entry)
    {
        entry = new HostsLineDto { Kind = "entry", Group = currentGroup };
        var body = line.TrimStart('#').Trim();

        // 行尾注释：# 之后的部分（hosts 域名合法字符不含 #，首个 # 即注释起点）
        var hashIdx = body.IndexOf('#');
        string? comment = null;
        if (hashIdx >= 0)
        {
            comment = body[(hashIdx + 1)..].Trim();
            body = body[..hashIdx].Trim();
        }

        var tokens = body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 2) return false; // 条目至少要 IP + 一个域名
        if (!IPAddress.TryParse(tokens[0], out _)) return false;

        entry.Ip = tokens[0];
        entry.Domains = tokens[1..].ToList();
        entry.Comment = string.IsNullOrEmpty(comment) ? null : comment;
        entry.Enabled = !line.TrimStart().StartsWith('#');
        return true;
    }

    /// <summary>探测当前进程对 hosts 文件是否有写权限（写 hosts 需要管理员身份）。</summary>
    private bool CheckWritable()
    {
        try
        {
            using var fs = new FileStream(HostsPath, FileMode.Open, FileAccess.Write, FileShare.Read);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // ===================== 保存 =====================

    /// <summary>校验并整体写回 hosts（写入前自动备份，保留最近 MaxBackups 份）。</summary>
    /// <returns>本次写入创建的备份文件名；校验失败抛 ArgumentException，权限不足抛 UnauthorizedAccessException。</returns>
    public string Save(HostsSaveDto dto)
    {
        Validate(dto.Lines);
        var text = Serialize(dto.Lines);
        var backupName = Backup();
        File.WriteAllText(HostsPath, text, new UTF8Encoding(false));
        return backupName;
    }

    /// <summary>保存前校验：IP 合法、域名非空且合法，避免写坏 hosts 导致网络解析异常。</summary>
    private static void Validate(List<HostsLineDto> lines)
    {
        var index = 0;
        foreach (var line in lines)
        {
            index++;
            if (line.Kind == "entry")
            {
                if (string.IsNullOrWhiteSpace(line.Ip) || !IPAddress.TryParse(line.Ip.Trim(), out _))
                    throw new ArgumentException($"第 {index} 行：IP 地址格式不正确（{line.Ip}）");
                if (line.Domains is null || line.Domains.Count == 0 || line.Domains.Any(string.IsNullOrWhiteSpace))
                    throw new ArgumentException($"第 {index} 行：域名不能为空");
                var bad = line.Domains.FirstOrDefault(d => d.Contains('#') || d.Contains(' '));
                if (bad is not null)
                    throw new ArgumentException($"第 {index} 行：域名含非法字符（{bad}）");
            }
            else if (line.Kind == "group")
            {
                if (string.IsNullOrWhiteSpace(line.Group))
                    throw new ArgumentException($"第 {index} 行：分组名不能为空");
            }
        }
    }

    /// <summary>按行列表重建 hosts 文本（entry/group 规范化输出，other 原样写回）。</summary>
    private static string Serialize(List<HostsLineDto> lines)
    {
        var sb = new StringBuilder();
        foreach (var line in lines)
        {
            switch (line.Kind)
            {
                case "entry":
                {
                    var body = $"{line.Ip!.Trim()} {string.Join(" ", line.Domains.Select(d => d.Trim()))}";
                    if (!string.IsNullOrWhiteSpace(line.Comment)) body += $" # {line.Comment.Trim()}";
                    sb.AppendLine(line.Enabled ? body : $"# {body}");
                    break;
                }
                case "group":
                    sb.AppendLine($"# ===== {line.Group!.Trim()} =====");
                    break;
                default:
                    sb.AppendLine(line.Raw ?? string.Empty);
                    break;
            }
        }
        return sb.ToString();
    }

    // ===================== 备份 / 还原 =====================

    /// <summary>备份当前 hosts 到 hosts.bak.yyyyMMdd-HHmmss，并清理超出保留份数的旧备份。</summary>
    /// <returns>备份文件名。</returns>
    public string Backup()
    {
        var backupName = BackupPrefix + DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var backupPath = Path.Combine(BackupDirectory, backupName);
        File.Copy(HostsPath, backupPath, overwrite: false);

        // 淘汰最旧备份：同秒多次备份文件名唯一（Copy overwrite:false 已挡同秒重复），按时间排序淘汰
        var backups = Directory.GetFiles(BackupDirectory, BackupPrefix + "*")
            .Select(p => new FileInfo(p))
            .OrderByDescending(f => f.LastWriteTime)
            .ToList();
        foreach (var old in backups.Skip(MaxBackups))
        {
            try { old.Delete(); } catch { /* 删除失败忽略，不阻断主流程 */ }
        }
        return backupName;
    }

    private string _backupDir = string.Empty;

    /// <summary>备份目录（hosts 所在目录）。</summary>
    private string BackupDirectory
    {
        get
        {
            if (_backupDir.Length == 0) _backupDir = Path.GetDirectoryName(HostsPath)!;
            return _backupDir;
        }
    }

    /// <summary>备份列表（新的在前）。</summary>
    public List<HostsBackupDto> ListBackups()
    {
        if (!Directory.Exists(BackupDirectory)) return new List<HostsBackupDto>();
        return Directory.GetFiles(BackupDirectory, BackupPrefix + "*")
            .Select(p => new FileInfo(p))
            .OrderByDescending(f => f.LastWriteTime)
            .Select(f => new HostsBackupDto
            {
                Name = f.Name,
                Modified = f.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"),
                Size = f.Length,
            })
            .ToList();
    }

    /// <summary>用指定备份还原 hosts（还原前自动备份当前版本）。</summary>
    /// <param name="name">备份文件名（仅允许 hosts.bak.*，防路径穿越）。</param>
    public string Restore(string name)
    {
        // 防路径穿越：只允许纯文件名且匹配备份前缀
        if (string.IsNullOrWhiteSpace(name)
            || name != Path.GetFileName(name)
            || !name.StartsWith(BackupPrefix, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("备份文件名不合法");

        var backupPath = Path.Combine(BackupDirectory, name);
        if (!File.Exists(backupPath)) throw new FileNotFoundException($"备份文件不存在：{name}");

        var currentBackup = Backup();
        File.Copy(backupPath, HostsPath, overwrite: true);
        return currentBackup;
    }
}

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ConvenientSystem.Desktop;

// ============================ DTO ============================

/// <summary>本地 Git 仓库条目（git-repos.json 持久化）。</summary>
public sealed class GitRepo
{
    public string Path { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime AddedAt { get; set; } = DateTime.Now;
}

/// <summary>仓库状态总览。</summary>
public sealed class GitRepoStatusDto
{
    public string Path { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>目录有效且是 Git 仓库。</summary>
    public bool IsRepo { get; set; }
    public string Branch { get; set; } = string.Empty;
    /// <summary>HEAD 短哈希（空仓库为空）。</summary>
    public string ShortHash { get; set; } = string.Empty;
    /// <summary>领先远程提交数。</summary>
    public int Ahead { get; set; }
    /// <summary>落后远程提交数。</summary>
    public int Behind { get; set; }
    /// <summary>工作区改动文件数（含未跟踪）。</summary>
    public int Changes { get; set; }
    /// <summary>最近一次提交（短哈希 + 说明，空仓库为空）。</summary>
    public string LastCommit { get; set; } = string.Empty;
    /// <summary>远程名（如 origin，无远程为空）。</summary>
    public string Remote { get; set; } = string.Empty;
    /// <summary>状态不可用原因（目录不存在等，正常为空）。</summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>分支条目。</summary>
public sealed class GitBranchDto
{
    public string Name { get; set; } = string.Empty;
    /// <summary>是否当前分支。</summary>
    public bool IsCurrent { get; set; }
    /// <summary>是否远程分支（remotes/origin/xxx）。</summary>
    public bool IsRemote { get; set; }
}

/// <summary>Git 命令执行结果。</summary>
public sealed class GitCommandResultDto
{
    public bool Success { get; set; }
    /// <summary>合并后的输出（stdout + stderr）。</summary>
    public string Output { get; set; } = string.Empty;
    public int ExitCode { get; set; }
}

/// <summary>子目录扫描发现的仓库。</summary>
public sealed class GitDiscoveredRepoDto
{
    public string Path { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Branch { get; set; } = string.Empty;
}

/// <summary>添加仓库结果。</summary>
public sealed class GitAddRepoResult
{
    public bool Ok { get; set; }
    public string Message { get; set; } = string.Empty;
    /// <summary>添加成功时的仓库状态（可直接展示）。</summary>
    public GitRepoStatusDto? Status { get; set; }
}

/// <summary>合并中间状态（供“合并进行中”横幅与一键放弃）。</summary>
public sealed class GitMergeStateDto
{
    public bool InProgress { get; set; }
    /// <summary>来源分支（来自 MERGE_MSG 首行，解析不出为空）。</summary>
    public string SourceBranch { get; set; } = string.Empty;
    /// <summary>冲突文件数（无冲突为 0）。</summary>
    public int Conflicts { get; set; }
}

/// <summary>Git 环境检测结果。</summary>
public sealed class GitEnvDto
{
    public bool Installed { get; set; }
    public string Version { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
}

/// <summary>全局 Git 配置项。</summary>
public sealed class GitConfigItemDto
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

/// <summary>设置单项全局 Git 配置请求（Value 为 null 时执行 --unset）。</summary>
public sealed class GitConfigSetRequest
{
    public string Key { get; set; } = string.Empty;
    public string? Value { get; set; }
}

/// <summary>取消操作结果。</summary>
public sealed class GitCancelResultDto
{
    /// <summary>是否找到并杀掉了运行中操作。</summary>
    public bool Cancelled { get; set; }
    public string Message { get; set; } = string.Empty;
}

/// <summary>提交历史条目。</summary>
public sealed class GitLogEntryDto
{
    public string Hash { get; set; } = string.Empty;
    public string ShortHash { get; set; } = string.Empty;
    /// <summary>父提交哈希列表（首个为 first parent，前端画分支线用）。</summary>
    public List<string> Parents { get; set; } = new();
    public string Author { get; set; } = string.Empty;
    /// <summary>提交时间（yyyy-MM-dd HH:mm）。</summary>
    public string Date { get; set; } = string.Empty;
    /// <summary>提交说明（首行）。</summary>
    public string Subject { get; set; } = string.Empty;
    /// <summary>指向此提交的引用装饰（HEAD -> main、origin/main、tag: v1.0 等）。</summary>
    public List<string> Refs { get; set; } = new();
}

/// <summary>提交变更文件（含状态）。</summary>
public sealed class GitCommitFileDto
{
    /// <summary>状态字母（M/A/D/R/C）。</summary>
    public string Status { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    /// <summary>重命名/复制的旧路径（其余状态为空）。</summary>
    public string OldPath { get; set; } = string.Empty;
}

/// <summary>按文件切好的 diff 文本。</summary>
public sealed class GitDiffFileDto
{
    public string Path { get; set; } = string.Empty;
    public string Diff { get; set; } = string.Empty;
}

/// <summary>单提交详情：元信息 + 变更文件 + 按文件切分的 diff。</summary>
public sealed class GitCommitDetailDto
{
    public GitLogEntryDto Commit { get; set; } = new();
    public List<GitCommitFileDto> Files { get; set; } = new();
    public List<GitDiffFileDto> Diffs { get; set; } = new();
}

// ============================ 请求模型 ============================

public sealed class GitPathRequest
{
    public string Path { get; set; } = string.Empty;
    /// <summary>操作 ID（前端生成，用于取消；拉取/推送等可取消操作传）。</summary>
    public string OpId { get; set; } = string.Empty;
}

public sealed class GitMergeRequest
{
    public string Path { get; set; } = string.Empty;
    /// <summary>合并来源分支（合入当前分支）。</summary>
    public string SourceBranch { get; set; } = string.Empty;
    /// <summary>操作 ID（用于取消）。</summary>
    public string OpId { get; set; } = string.Empty;
}

public sealed class GitCheckoutRequest
{
    public string Path { get; set; } = string.Empty;
    /// <summary>切换目标分支（已有分支）。</summary>
    public string Branch { get; set; } = string.Empty;
    /// <summary>新建分支名（提供时以 Branch 为起点创建并切换）。</summary>
    public string NewBranch { get; set; } = string.Empty;
    /// <summary>操作 ID（用于取消）。</summary>
    public string OpId { get; set; } = string.Empty;
}

public sealed class GitExecRequest
{
    public string Path { get; set; } = string.Empty;
    /// <summary>完整命令（必须以 git 开头，如 git log --oneline -10）。</summary>
    public string Command { get; set; } = string.Empty;
    /// <summary>操作 ID（前端生成，用于取消；拉取/推送/合并/切换/执行可传）。</summary>
    public string OpId { get; set; } = string.Empty;
}

public sealed class GitCloneRequest
{
    /// <summary>远程仓库地址（https / ssh 均可）。</summary>
    public string Url { get; set; } = string.Empty;
    /// <summary>保存位置（父目录，需已存在）。</summary>
    public string ParentDir { get; set; } = string.Empty;
    /// <summary>克隆后的目录名（空则从 URL 推断）。</summary>
    public string DirName { get; set; } = string.Empty;
    /// <summary>操作 ID（前端生成，用于取消）。</summary>
    public string OpId { get; set; } = string.Empty;
}

/// <summary>取消请求：opId 对应运行中的操作进程。</summary>
public sealed class GitCancelRequest
{
    public string OpId { get; set; } = string.Empty;
}

public sealed class GitLogRequest
{
    public string Path { get; set; } = string.Empty;
    /// <summary>筛选分支（空 = 当前 HEAD 全部历史）。</summary>
    public string Branch { get; set; } = string.Empty;
    /// <summary>跳过条数（分页，滚到底自动累加）。</summary>
    public int Skip { get; set; }
    /// <summary>本页条数（默认 50，上限 200）。</summary>
    public int Take { get; set; } = 50;
}

public sealed class GitCommitDetailRequest
{
    public string Path { get; set; } = string.Empty;
    /// <summary>提交哈希（短/长均可，仅十六进制）。</summary>
    public string Hash { get; set; } = string.Empty;
}

/// <summary>工作区改动文件（porcelain 状态解析结果）。</summary>
public sealed class GitChangeFileDto
{
    /// <summary>展示状态字母（M/A/D/R/U/“?”未跟踪）。</summary>
    public string Status { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    /// <summary>重命名的旧路径（其余状态为空）。</summary>
    public string OldPath { get; set; } = string.Empty;
    /// <summary>是否未跟踪（放弃 = 物理删除）。</summary>
    public bool IsUntracked { get; set; }
    /// <summary>是否冲突未解决（合并/变基中）。</summary>
    public bool IsConflict { get; set; }
}

/// <summary>工作区改动分组列表。</summary>
public sealed class GitChangesDto
{
    /// <summary>已暂存（含新增/删除/重命名）。</summary>
    public List<GitChangeFileDto> Staged { get; set; } = new();
    /// <summary>未暂存（含未跟踪）。</summary>
    public List<GitChangeFileDto> Unstaged { get; set; } = new();
    /// <summary>合并中间状态（与 MergeState 端点同源，横幅复用）。</summary>
    public GitMergeStateDto MergeState { get; set; } = new();
}

/// <summary>单文件 diff 预览请求。</summary>
public sealed class GitFileDiffRequest
{
    public string Path { get; set; } = string.Empty;
    /// <summary>仓库相对路径。</summary>
    public string FilePath { get; set; } = string.Empty;
    /// <summary>看已暂存 diff（true）或工作区 diff（false）。</summary>
    public bool Staged { get; set; }
    /// <summary>操作 ID（用于取消，暂不用）。</summary>
    public string OpId { get; set; } = string.Empty;
}

/// <summary>单文件 diff 预览结果。</summary>
public sealed class GitFileDiffDto
{
    public string Path { get; set; } = string.Empty;
    /// <summary>unified diff 文本（未跟踪文件为内容合成的 + 行，二进制为占位提示）。</summary>
    public string Diff { get; set; } = string.Empty;
    /// <summary>文件被删除（diff 显示删除内容）。</summary>
    public bool Deleted { get; set; }
    /// <summary>二进制文件（无文本 diff）。</summary>
    public bool Binary { get; set; }
}

/// <summary>暂存/取消暂存请求。</summary>
public sealed class GitStageRequest
{
    public string Path { get; set; } = string.Empty;
    /// <summary>目标文件（仓库相对路径，null/空 = 全部）。</summary>
    public string? FilePath { get; set; }
    /// <summary>true 暂存 / false 取消暂存。</summary>
    public bool Stage { get; set; } = true;
}

/// <summary>提交请求。</summary>
public sealed class GitCommitRequest
{
    public string Path { get; set; } = string.Empty;
    /// <summary>提交说明（必填）。</summary>
    public string Message { get; set; } = string.Empty;
    /// <summary>提交后顺带推送。</summary>
    public bool Push { get; set; }
    /// <summary>操作 ID（提交/推送可取消）。</summary>
    public string OpId { get; set; } = string.Empty;
}

/// <summary>放弃改动请求。</summary>
public sealed class GitDiscardRequest
{
    public string Path { get; set; } = string.Empty;
    /// <summary>目标文件（仓库相对路径，null/空 = 放弃全部未暂存）。</summary>
    public string? FilePath { get; set; }
    /// <summary>未跟踪文件也一并删除（单文件时由状态自动判定，此参数仅“全部放弃”用）。</summary>
    public bool IncludeUntracked { get; set; } = true;
}

/// <summary>
/// Git 代码管理服务：本地仓库列表持久化、状态总览、拉取/推送/合并/切换、
/// 子目录仓库发现与白名单命令执行。所有命令走 git -C {dir} + ArgumentList 直传，
/// 不经过 shell；GIT_TERMINAL_PROMPT=0 保证凭证缺失时快速失败而不是挂起。
/// </summary>
public sealed class GitService
{
    private readonly ILogger<GitService> _logger;
    private readonly object _lock = new();
    private List<GitRepo> _repos = new();

    /// <summary>运行中可取消操作注册表：opId → 进程。杀进程即取消，原请求随 WaitForExit 返回。</summary>
    private readonly Dictionary<string, Process> _runningOps = new();
    private readonly object _opsLock = new();

    private static string StorePath => Path.Combine(AppContext.BaseDirectory, "git-repos.json");
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>本地命令超时（毫秒）。</summary>
    private const int LocalTimeoutMs = 60_000;
    /// <summary>网络命令（pull/push/fetch）超时（毫秒）。</summary>
    private const int NetworkTimeoutMs = 180_000;
    /// <summary>克隆超时（毫秒）：大仓库下载耗时长，单独放宽到 10 分钟。</summary>
    private const int CloneTimeoutMs = 600_000;

    public GitService(ILogger<GitService> logger)
    {
        _logger = logger;
        Load();
    }

    // ============================ 仓库列表 ============================

    /// <summary>仓库列表（附带实时状态，供列表展示分支徽章）。</summary>
    public IReadOnlyList<GitRepoStatusDto> GetReposWithStatus()
    {
        lock (_lock) return _repos.Select(r => GetStatus(r.Path)).ToList();
    }

    /// <summary>添加仓库：自动解析仓库根目录（子目录也可识别），重复添加跳过。</summary>
    public GitAddRepoResult AddRepo(string path)
    {
        path = path?.Trim() ?? string.Empty;
        if (path.Length == 0 || !Directory.Exists(path))
            return new GitAddRepoResult { Ok = false, Message = $"目录不存在: {path}" };

        // 子目录场景：解析到仓库根
        var root = ResolveRepoRoot(path);
        if (string.IsNullOrEmpty(root))
            return new GitAddRepoResult { Ok = false, Message = "该目录不是 Git 仓库（可尝试扫描其子目录）" };

        lock (_lock)
        {
            if (_repos.Any(r => r.Path.Equals(root, StringComparison.OrdinalIgnoreCase)))
                return new GitAddRepoResult { Ok = false, Message = "该仓库已在列表中" };

            _repos.Add(new GitRepo
            {
                Path = root,
                Name = Path.GetFileName(root.TrimEnd('\\', '/')),
                AddedAt = DateTime.Now,
            });
            SaveLocked();
        }
        return new GitAddRepoResult { Ok = true, Message = "已添加", Status = GetStatus(root) };
    }

    /// <summary>移除仓库（只移除列表记录，不碰磁盘）。</summary>
    public bool RemoveRepo(string path)
    {
        lock (_lock)
        {
            var repo = _repos.FirstOrDefault(r => r.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
            if (repo == null) return false;
            _repos.Remove(repo);
            SaveLocked();
            return true;
        }
    }

    /// <summary>扫描目录的一级子目录，发现其中的 Git 仓库（供“发现仓库”勾选列表）。</summary>
    public IReadOnlyList<GitDiscoveredRepoDto> DiscoverRepos(string path)
    {
        var result = new List<GitDiscoveredRepoDto>();
        path = path?.Trim() ?? string.Empty;
        if (path.Length == 0 || !Directory.Exists(path)) return result;

        foreach (var dir in Directory.GetDirectories(path))
        {
            // 仓库根判定：.git 为目录（标准克隆）或文件（worktree/子模块）
            if (!Directory.Exists(Path.Combine(dir, ".git")) && !File.Exists(Path.Combine(dir, ".git")))
                continue;
            var status = GetStatus(dir);
            result.Add(new GitDiscoveredRepoDto
            {
                Path = dir,
                Name = Path.GetFileName(dir),
                Branch = status.IsRepo ? status.Branch : string.Empty,
            });
        }
        return result;
    }

    // ============================ 状态与分支 ============================

    /// <summary>查询仓库状态总览（目录无效或非仓库时 IsRepo=false）。</summary>
    public GitRepoStatusDto GetStatus(string path)
    {
        var dto = new GitRepoStatusDto
        {
            Path = path ?? string.Empty,
            Name = string.IsNullOrWhiteSpace(path) ? string.Empty : Path.GetFileName(path.TrimEnd('\\', '/')),
        };

        try
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                dto.Message = "目录不存在或已被移动";
                return dto;
            }

            var check = RunGit(path, "rev-parse", "--is-inside-work-tree");
            if (check.ExitCode != 0 || check.Output.Trim() != "true")
            {
                dto.Message = "不是 Git 仓库";
                return dto;
            }
            dto.IsRepo = true;

            // 分支与短哈希（空仓库时 rev-parse HEAD 报错，保持空值）
            var branch = RunGit(path, "rev-parse", "--abbrev-ref", "HEAD");
            if (branch.ExitCode == 0)
                dto.Branch = branch.Output.Trim() == "HEAD" ? "HEAD（游离）" : branch.Output.Trim();

            var hash = RunGit(path, "rev-parse", "--short", "HEAD");
            if (hash.ExitCode == 0)
                dto.ShortHash = hash.Output.Trim();

            // ahead/behind/改动数（porcelain -b）
            var status = RunGit(path, "status", "--porcelain=v1", "-b");
            if (status.ExitCode == 0)
            {
                var lines = status.Output.Split('\n');
                foreach (var line in lines)
                {
                    var t = line.TrimEnd('\r');
                    if (t.StartsWith("## "))
                    {
                        var header = t[3..];
                        var m = Regex.Match(header, @"ahead (\d+)");
                        if (m.Success) dto.Ahead = int.Parse(m.Groups[1].Value);
                        m = Regex.Match(header, @"behind (\d+)");
                        if (m.Success) dto.Behind = int.Parse(m.Groups[1].Value);
                        break;
                    }
                }
                // 非空且非分支标题行即改动（含 ?? 未跟踪文件）
                dto.Changes = lines.Count(l =>
                {
                    var t = l.TrimEnd('\r');
                    return t.Length > 0 && !t.StartsWith("## ");
                });
            }

            var log = RunGit(path, "log", "-1", "--pretty=format:%h %s");
            if (log.ExitCode == 0 && log.Output.Length > 0)
                dto.LastCommit = log.Output.Trim();

            var remote = RunGit(path, "remote");
            if (remote.ExitCode == 0)
                dto.Remote = remote.Output.Split('\n').FirstOrDefault(x => x.Trim().Length > 0)?.Trim() ?? string.Empty;
        }
        catch (Exception ex)
        {
            dto.Message = "状态查询失败: " + ex.Message;
            _logger.LogWarning(ex, "Git 状态查询失败: {Path}", path);
        }
        return dto;
    }

    /// <summary>分支列表（本地在前、远程在后）。</summary>
    public IReadOnlyList<GitBranchDto> GetBranches(string path)
    {
        var list = new List<GitBranchDto>();
        var result = RunGit(path, "for-each-ref", "--format=%(refname:short)%09%(HEAD)", "refs/heads", "refs/remotes");
        if (result.ExitCode != 0)
            throw new InvalidOperationException("获取分支失败: " + result.Output);

        foreach (var line in result.Output.Split('\n'))
        {
            var t = line.TrimEnd('\r');
            if (t.Length == 0) continue;
            var parts = t.Split('\t');
            var name = parts[0];
            var isCurrent = parts.Length > 1 && parts[1] == "*";
            var isRemote = name.Contains('/');
            list.Add(new GitBranchDto { Name = name, IsCurrent = isCurrent, IsRemote = isRemote });
        }
        // 本地分支排前面，同组内当前分支优先
        return list.OrderBy(b => b.IsRemote).ThenByDescending(b => b.IsCurrent).ThenBy(b => b.Name).ToList();
    }

    // ============================ 提交历史 ============================

    /// <summary>提交历史单页条数上限。</summary>
    private const int LogMaxTake = 200;
    /// <summary>单提交 diff 输出上限（字符）：防止超大提交撑爆响应。</summary>
    private const int DiffMaxChars = 512_000;

    /// <summary>
    /// 提交历史（新→旧）：含父提交列表（前端画分支线）与指向各提交的 refs 装饰。
    /// branch 为空时取当前 HEAD 历史；skip/take 分页，take 上限 200。
    /// 字段用不可见分隔符（\x1f 字段 / \x1e 记录）切分，稳定解析含空格的说明。
    /// </summary>
    public IReadOnlyList<GitLogEntryDto> GetLog(string path, string branch, int skip, int take)
    {
        branch = branch?.Trim() ?? string.Empty;
        if (skip < 0) skip = 0;
        if (take <= 0) take = 50;
        if (take > LogMaxTake) take = LogMaxTake;

        var args = new List<string>
        {
            "log",
            $"--skip={skip}",
            // -n 不支持 -n=N 等号形式，用 --max-count
            $"--max-count={take}",
            "--date=format:%Y-%m-%d %H:%M",
            "--pretty=format:%H%x1f%h%x1f%P%x1f%an%x1f%ad%x1f%s%x1f%d%x1e",
        };
        if (branch.Length > 0)
        {
            if (branch.StartsWith('-') || branch.Any(char.IsWhiteSpace))
                throw new ArgumentException("无效的分支名");
            args.Add(branch);
        }

        var result = RunGit(path, LocalTimeoutMs, null, args.ToArray());
        if (result.ExitCode != 0)
        {
            // 空仓库（当前分支还没有任何提交）返回空列表而不是报错
            if (result.Output.Contains("does not have any commits", StringComparison.Ordinal))
                return Array.Empty<GitLogEntryDto>();
            throw new InvalidOperationException("获取提交历史失败: " + result.Output);
        }

        var list = new List<GitLogEntryDto>();
        foreach (var record in result.Output.Split('\x1e'))
        {
            var fields = record.Trim('\n', '\r').Split('\x1f');
            if (fields.Length < 7) continue;
            var entry = new GitLogEntryDto
            {
                Hash = fields[0],
                ShortHash = fields[1],
                Author = fields[3],
                Date = fields[4],
                Subject = fields[5],
            };
            foreach (var p in fields[2].Split(' ', StringSplitOptions.RemoveEmptyEntries))
                entry.Parents.Add(p);
            // refs 装饰形如 " (HEAD -> main, origin/main, tag: v1.0)"：去括号按逗号切
            var refs = fields[6].Trim();
            if (refs.StartsWith('(') && refs.EndsWith(')'))
            {
                foreach (var r in refs[1..^1].Split(", "))
                    if (r.Trim().Length > 0) entry.Refs.Add(r.Trim());
            }
            list.Add(entry);
        }
        return list;
    }

    /// <summary>
    /// 单提交详情：元信息 + 变更文件（含 M/A/D/R 状态，重命名带旧路径）
    /// + 按文件切好的 diff 文本（超长截断）。
    /// </summary>
    public GitCommitDetailDto GetCommitDetail(string path, string hash)
    {
        hash = hash?.Trim() ?? string.Empty;
        // 只接受十六进制哈希（短/长均可），拒绝任意 rev 语法与选项注入
        if (hash.Length is < 4 or > 40 || !hash.All(Uri.IsHexDigit))
            throw new ArgumentException("无效的提交哈希");

        // 元信息 + 文件状态列表（--name-status 隐含压制 patch 正文）
        var meta = RunGit(path, LocalTimeoutMs, null,
            "show", "--no-color", "--name-status",
            "--pretty=format:%H%x1f%h%x1f%P%x1f%an%x1f%ad%x1f%s",
            "--date=format:%Y-%m-%d %H:%M", hash);
        if (meta.ExitCode != 0)
            throw new InvalidOperationException("获取提交详情失败: " + meta.Output);

        var dto = new GitCommitDetailDto();
        var lines = meta.Output.Replace("\r\n", "\n").Split('\n');
        if (lines.Length > 0 && lines[0].Contains('\x1f'))
        {
            var f = lines[0].Split('\x1f');
            if (f.Length >= 6)
            {
                dto.Commit = new GitLogEntryDto
                {
                    Hash = f[0],
                    ShortHash = f[1],
                    Author = f[3],
                    Date = f[4],
                    Subject = f[5],
                };
                foreach (var p in f[2].Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    dto.Commit.Parents.Add(p);
            }
        }

        // 首行为 format 字段行（format 模式无结尾空行），其后直接是 name-status 列表（tab 分隔）
        foreach (var line in lines.Skip(1))
        {
            if (line.Length == 0) continue;
            var parts = line.Split('\t');
            if (parts.Length < 2) continue;
            var file = new GitCommitFileDto
            {
                // R100/C75 相似度后缀只留首字母
                Status = parts[0].Length > 0 ? parts[0][0].ToString() : "?",
            };
            if (parts.Length >= 3) { file.Path = parts[^1]; file.OldPath = parts[1]; }
            else file.Path = parts[1];
            if (file.Path.Length > 0) dto.Files.Add(file);
        }

        // 纯 diff（空 format 压制 commit 头）：按 "diff --git " 块切分文件
        var diff = RunGit(path, LocalTimeoutMs, null,
            "show", "--no-color", "--pretty=format:", hash);
        if (diff.ExitCode == 0)
        {
            var text = diff.Output.Replace("\r\n", "\n");
            if (text.Length > DiffMaxChars)
                text = text[..DiffMaxChars] + "\n… diff 过长已截断";
            string? curPath = null;
            var sb = new StringBuilder();
            foreach (var line in text.Split('\n'))
            {
                if (line.StartsWith("diff --git ", StringComparison.Ordinal))
                {
                    if (curPath != null)
                        dto.Diffs.Add(new GitDiffFileDto { Path = curPath, Diff = sb.ToString().TrimEnd('\n') });
                    curPath = ExtractDiffPath(line);
                    sb.Clear();
                }
                if (curPath != null)
                {
                    sb.AppendLine(line);
                }
            }
            if (curPath != null)
                dto.Diffs.Add(new GitDiffFileDto { Path = curPath, Diff = sb.ToString().TrimEnd('\n') });
        }
        return dto;
    }

    /// <summary>从 "diff --git a/path b/path" 行提取 b 侧（新）路径；含空格时 git 给两侧加引号。</summary>
    private static string ExtractDiffPath(string line)
    {
        var marker = line.Contains("\"b/") ? "\"b/" : " b/";
        var idx = line.LastIndexOf(marker, StringComparison.Ordinal);
        return idx < 0 ? string.Empty : line[(idx + marker.Length)..].Trim('"');
    }

    // ============================ 工作区改动 / 暂存 / 提交 ============================

    /// <summary>
    /// 工作区改动列表：porcelain 状态解析为已暂存/未暂存两组（含未跟踪与冲突标记）。
    /// XY 两列：X=暂存区状态，Y=工作区状态；未跟踪 ?? 归入未暂存组。
    /// </summary>
    public GitChangesDto GetChanges(string path)
    {
        var dto = new GitChangesDto();
        var result = RunGit(path, "status", "--porcelain=v1");
        if (result.ExitCode != 0)
            throw new InvalidOperationException("获取改动列表失败: " + result.Output);

        foreach (var raw in result.Output.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length < 4) continue;
            var x = line[0];
            var y = line[1];
            var rest = line[3..];

            // 重命名/复制带箭头：“old -> new”
            string filePath = rest, oldPath = string.Empty;
            var arrow = rest.IndexOf(" -> ", StringComparison.Ordinal);
            if (arrow >= 0)
            {
                oldPath = rest[..arrow];
                filePath = rest[(arrow + 4)..];
            }

            bool untracked = x == '?' && y == '?';
            bool conflict = x is 'A' or 'D' or 'U' || y is 'A' or 'D' or 'U';

            // X 列：已暂存组；“ ”空格 = 未暂存；? 入未暂存组
            if (x != ' ' && x != '?')
            {
                dto.Staged.Add(new GitChangeFileDto
                {
                    Status = x.ToString(),
                    Path = filePath,
                    OldPath = oldPath,
                    IsUntracked = false,
                    IsConflict = x is 'A' or 'D' or 'U',
                });
            }
            // Y 列：未暂存组（含未跟踪 ??）
            if (y != ' ' || untracked)
            {
                dto.Unstaged.Add(new GitChangeFileDto
                {
                    Status = untracked ? "?" : y.ToString(),
                    Path = filePath,
                    OldPath = x == 'R' || x == 'C' ? oldPath : string.Empty,
                    IsUntracked = untracked,
                    IsConflict = y is 'A' or 'D' or 'U',
                });
            }
        }
        dto.MergeState = GetMergeState(path);
        return dto;
    }

    /// <summary>
    /// 暂存/取消暂存：单文件或全部。暂存 = add，取消暂存 = restore --staged；
    /// 文件路径以 -- 分隔传给 git，防选项注入。
    /// </summary>
    public GitCommandResultDto Stage(string path, string? filePath, bool stage)
    {
        filePath = filePath?.Trim();
        var verb = stage ? "暂存" : "取消暂存";
        // 文件路径以 -- 分隔传给 git，防选项注入；未指定文件 = 全部
        var result = string.IsNullOrEmpty(filePath)
            ? (stage ? RunGit(path, "add", "-A") : RunGit(path, "restore", "--staged", "."))
            : (stage ? RunGit(path, "add", "--", filePath) : RunGit(path, "restore", "--staged", "--", filePath));
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"{verb}失败: " + result.Output);
        return new GitCommandResultDto { Success = true, Output = $"{verb}完成", ExitCode = 0 };
    }

    /// <summary>
    /// 提交已暂存改动（git commit -m）。合并进行中时即完成合并；
    /// Push=true 时提交后顺带推送（无上游自动建立）。
    /// </summary>
    public GitCommandResultDto Commit(string path, string message, bool push, string? opId = null)
    {
        message = message?.Trim() ?? string.Empty;
        if (message.Length == 0)
            throw new ArgumentException("提交说明不能为空");

        var result = RunGit(path, LocalTimeoutMs, opId, "commit", "-m", message);
        if (result.ExitCode != 0)
            return ToResult(result);

        var output = result.Output;
        if (push)
        {
            var pushResult = Push(path, opId);
            output = output + "\n" + pushResult.Output;
            if (!pushResult.Success)
            {
                return new GitCommandResultDto
                {
                    Success = false,
                    Output = output + "\n（提交成功，但推送失败）",
                    ExitCode = pushResult.ExitCode,
                };
            }
        }
        return new GitCommandResultDto { Success = true, Output = output, ExitCode = 0 };
    }

    /// <summary>
    /// 放弃改动（不可恢复）：单文件时按状态分组处理——已跟踪 restore 还原，
    /// 未跟踪物理删除；未指定文件 = 放弃全部未暂存（可选含未跟踪）。
    /// </summary>
    public GitCommandResultDto Discard(string path, string? filePath, bool includeUntracked)
    {
        filePath = filePath?.Trim();
        var output = new StringBuilder();

        if (string.IsNullOrEmpty(filePath))
        {
            // 全部放弃：已跟踪 restore，未跟踪 clean
            var restore = RunGit(path, "restore", "--");
            if (restore.ExitCode != 0 && restore.Output.Trim().Length > 0)
                throw new InvalidOperationException("放弃失败: " + restore.Output);
            output.AppendLine("已还原全部已跟踪文件的改动");
            if (includeUntracked)
            {
                var clean = RunGit(path, "clean", "-f", "-d");
                if (clean.ExitCode != 0)
                    throw new InvalidOperationException("删除未跟踪文件失败: " + clean.Output);
                output.AppendLine("已删除未跟踪文件/目录");
            }
        }
        else
        {
            // 单文件：从 porcelain 判当前状态，已跟踪还原 / 未跟踪删除
            var status = RunGit(path, "status", "--porcelain=v1", "--", filePath);
            var line = status.Output.Split('\n').FirstOrDefault(l => l.TrimEnd('\r').Length >= 4);
            if (line == null)
                throw new InvalidOperationException("文件没有未暂存改动");
            var untracked = line.StartsWith("??");
            if (untracked)
            {
                var clean = RunGit(path, "clean", "-f", "--", filePath);
                if (clean.ExitCode != 0)
                    throw new InvalidOperationException("删除未跟踪文件失败: " + clean.Output);
                output.AppendLine($"已删除未跟踪文件: {filePath}");
            }
            else
            {
                var restore = RunGit(path, "restore", "--", filePath);
                if (restore.ExitCode != 0)
                    throw new InvalidOperationException("放弃失败: " + restore.Output);
                output.AppendLine($"已还原: {filePath}");
            }
        }
        return new GitCommandResultDto { Success = true, Output = output.ToString().TrimEnd(), ExitCode = 0 };
    }

    /// <summary>
    /// 单文件 diff 预览：已暂存（git diff --cached）或工作区（git diff）；
    /// 未跟踪文件读内容合成 + 行（仓库目录内校验，防目录穿越）；
    /// 二进制只给占位提示，超长截断。
    /// </summary>
    public GitFileDiffDto GetFileDiff(string path, string filePath, bool staged)
    {
        filePath = filePath?.Trim() ?? string.Empty;
        if (filePath.Length == 0 || filePath.Contains('"'))
            throw new ArgumentException("无效的文件路径");

        var dto = new GitFileDiffDto { Path = filePath };

        // 未跟踪文件：不在索引里，git diff 无输出，读内容合成
        var tracked = RunGit(path, "ls-files", "--", filePath);
        if (tracked.ExitCode == 0 && tracked.Output.Trim().Length == 0)
        {
            var full = Path.GetFullPath(Path.Combine(path, filePath));
            var root = Path.GetFullPath(path);
            if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("文件路径越出仓库目录");
            if (!File.Exists(full))
            {
                dto.Diff = "（文件已被删除）";
                dto.Deleted = true;
                return dto;
            }
            // 粗判二进制：前 8KB 含 NUL 即视为二进制
            var probe = new byte[8192];
            using (var fs = File.OpenRead(full))
            {
                var n = fs.Read(probe, 0, probe.Length);
                if (Array.IndexOf(probe, (byte)0, 0, n) >= 0)
                {
                    dto.Binary = true;
                    dto.Diff = "（二进制文件，不支持文本 diff）";
                    return dto;
                }
            }
            var text = File.ReadAllText(full);
            if (text.Length > DiffMaxChars) text = text[..DiffMaxChars] + "\n… 文件过大已截断";
            var sb = new StringBuilder();
            foreach (var line in text.Split('\n'))
                sb.Append('+').AppendLine(line.TrimEnd('\r'));
            dto.Diff = sb.ToString().TrimEnd('\n');
            return dto;
        }

        var args = new List<string> { "diff", "--no-color" };
        if (staged) args.Add("--cached");
        args.Add("--");
        args.Add(filePath);
        var result = RunGit(path, args.ToArray());
        if (result.ExitCode != 0)
            throw new InvalidOperationException("获取 diff 失败: " + result.Output);

        var diffText = result.Output.Replace("\r\n", "\n");
        if (diffText.Length > DiffMaxChars)
            diffText = diffText[..DiffMaxChars] + "\n… diff 过长已截断";
        dto.Diff = diffText;
        if (diffText.Contains("Binary files") || diffText.Contains("GIT binary patch"))
        {
            dto.Binary = true;
            dto.Diff = "（二进制文件，不支持文本 diff）";
        }
        return dto;
    }

    // ============================ 核心操作 ============================

    /// <summary>拉取当前分支（无跟踪信息时回退 origin {branch}）。</summary>
    public GitCommandResultDto Pull(string path, string? opId = null)
    {
        var result = RunGit(path, NetworkTimeoutMs, opId, "pull", "--no-edit");
        if (result.ExitCode != 0 && HasNoUpstream(result.Output))
        {
            var branch = GetPlainBranch(path);
            if (!string.IsNullOrEmpty(branch))
                result = RunGit(path, NetworkTimeoutMs, opId, "pull", "origin", branch);
        }
        return ToResult(result);
    }

    /// <summary>推送当前分支（无上游时自动 --set-upstream origin {branch}）。</summary>
    public GitCommandResultDto Push(string path, string? opId = null)
    {
        var result = RunGit(path, NetworkTimeoutMs, opId, "push");
        if (result.ExitCode != 0 && HasNoUpstream(result.Output))
        {
            var branch = GetPlainBranch(path);
            if (!string.IsNullOrEmpty(branch))
                result = RunGit(path, NetworkTimeoutMs, opId, "push", "--set-upstream", "origin", branch);
        }
        return ToResult(result);
    }

    /// <summary>合并来源分支到当前分支（--no-edit 防 merge 编辑器阻塞）。</summary>
    public GitCommandResultDto Merge(string path, string sourceBranch, string? opId = null)
    {
        sourceBranch = sourceBranch?.Trim() ?? string.Empty;
        if (sourceBranch.Length == 0 || sourceBranch.StartsWith('-'))
            throw new ArgumentException("无效的来源分支名");
        return ToResult(RunGit(path, LocalTimeoutMs, opId, "merge", "--no-edit", sourceBranch));
    }

    /// <summary>切换分支；提供 newBranch 时以 branch（可空=当前 HEAD）为起点新建并切换。</summary>
    public GitCommandResultDto Checkout(string path, string branch, string? newBranch, string? opId = null)
    {
        branch = branch?.Trim() ?? string.Empty;
        newBranch = newBranch?.Trim();
        if (!string.IsNullOrEmpty(newBranch))
        {
            if (newBranch.StartsWith('-')) throw new ArgumentException("无效的新分支名");
            var args = new List<string> { "switch", "-c", newBranch };
            if (branch.Length > 0)
            {
                if (branch.StartsWith('-')) throw new ArgumentException("无效的起点分支名");
                args.Add(branch);
            }
            return ToResult(RunGit(path, LocalTimeoutMs, opId, args.ToArray()));
        }
        if (branch.Length == 0 || branch.StartsWith('-'))
            throw new ArgumentException("无效的目标分支名");
        return ToResult(RunGit(path, LocalTimeoutMs, opId, "switch", branch));
    }

    /// <summary>
    /// 克隆远程仓库到指定目录，成功后自动添加到仓库列表。
    /// 目标目录已存在时必须为空（防止误写非空目录），目录名空则从 URL 末段推断。
    /// 失败或被取消时：目录是本次克隆新建的则删除半成品，避免残留空壳/垃圾对象。
    /// </summary>
    public GitCommandResultDto Clone(string url, string parentDir, string dirName, string? opId = null)
    {
        url = url?.Trim() ?? string.Empty;
        parentDir = parentDir?.Trim() ?? string.Empty;
        dirName = dirName?.Trim() ?? string.Empty;
        if (url.Length == 0 || url.StartsWith('-'))
            throw new ArgumentException("无效的仓库地址");
        if (parentDir.Length == 0 || !Directory.Exists(parentDir))
            throw new ArgumentException($"保存位置不存在: {parentDir}");

        // 目录名空则从 URL 推断：去查询串后取末段，再去掉 .git 后缀
        if (dirName.Length == 0)
        {
            var tail = url.Split('?', '#')[0].TrimEnd('/');
            var last = tail.Split('/')[^1];
            dirName = last.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? last[..^4] : last;
        }
        if (dirName.Length == 0 || dirName.Contains('/') || dirName.Contains('\\') || dirName.Contains("..") || dirName.StartsWith('-'))
            throw new ArgumentException("无效的目录名（不允许包含路径分隔符）");

        var targetDir = Path.Combine(parentDir, dirName);
        // 记住目标目录是否本次新建：取消/失败后仅清理新建的，已存在的空目录不动
        var createdNew = false;
        if (Directory.Exists(targetDir))
        {
            if (Directory.EnumerateFileSystemEntries(targetDir).Any())
                throw new ArgumentException($"目标目录已存在且非空: {targetDir}");
        }
        else
        {
            createdNew = true;
        }

        var result = RunGit(parentDir, CloneTimeoutMs, opId, "clone", url, dirName);
        if (result.ExitCode == 0)
        {
            // 克隆成功自动入库（同仓库已在列表时忽略，不影响克隆结果）
            try { AddRepo(targetDir); }
            catch { /* 入库失败不影响克隆 */ }
        }
        else if (createdNew && Directory.Exists(targetDir))
        {
            // 失败/取消：删除本次新建目录里的半成品（git 中断残留的 .git 与零散文件）
            try { Directory.Delete(targetDir, recursive: true); }
            catch (Exception ex) { _logger.LogWarning(ex, "清理克隆半成品目录失败: {Dir}", targetDir); }
        }
        return ToResult(result);
    }

    /// <summary>
    /// 白名单执行：命令必须以 git 开头，参数经引号感知切分后直传（不经 shell）。
    /// 仅限本机仓库目录，网络命令放宽超时。
    /// </summary>
    public GitCommandResultDto Exec(string path, string command, string? opId = null)
    {
        command = command?.Trim() ?? string.Empty;
        if (command.Length == 0)
            throw new ArgumentException("命令不能为空");
        if (!command.Equals("git", StringComparison.OrdinalIgnoreCase) &&
            !command.StartsWith("git ", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("仅允许执行 git 命令");

        var args = SplitArguments(command);
        // 去掉开头的 git，其余作为参数直传
        args = args.Skip(1).ToArray();
        if (args.Length == 0)
            throw new ArgumentException("缺少子命令（如 git status）");

        var isNetwork = args[0] is "pull" or "push" or "fetch" or "clone" or "remote";
        var result = RunGit(path, isNetwork ? NetworkTimeoutMs : LocalTimeoutMs, opId, args);
        return ToResult(result);
    }

    // ============================ 取消与合并状态 ============================

    /// <summary>
    /// 取消运行中操作：杀对应 git 进程树。原阻塞请求随 WaitForExit 返回，
    /// 带回已产生的输出与非零退出码（不会断连）。
    /// </summary>
    public GitCancelResultDto Cancel(string opId)
    {
        opId = opId?.Trim() ?? string.Empty;
        if (opId.Length == 0)
            return new GitCancelResultDto { Cancelled = false, Message = "缺少操作 ID" };

        Process? proc;
        lock (_opsLock)
        {
            if (!_runningOps.TryGetValue(opId, out proc))
                return new GitCancelResultDto { Cancelled = false, Message = "操作已结束或不存在" };
        }

        try
        {
            proc.Kill(entireProcessTree: true);
            return new GitCancelResultDto { Cancelled = true, Message = "已终止操作进程" };
        }
        catch (Exception ex)
        {
            // 进程刚好自行退出（正常完成/超时被杀）时 Kill 会抛异常，属正常竞争
            return new GitCancelResultDto { Cancelled = false, Message = "操作刚结束：" + ex.Message };
        }
    }

    /// <summary>
    /// 查询合并中间状态：MERGE_HEAD 存在即合并进行中（含冲突未解决）。
    /// 取消/失败的合并会残留此状态，供“放弃合并”一键收尾。
    /// </summary>
    public GitMergeStateDto GetMergeState(string path)
    {
        var dto = new GitMergeStateDto();
        var mergeHead = Path.Combine(path ?? string.Empty, ".git", "MERGE_HEAD");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(mergeHead))
            return dto;

        dto.InProgress = true;
        // 来源分支：从 MERGE_MSG 首行 "Merge branch 'xxx'..." 提取，解析不出留空
        try
        {
            var msgFile = Path.Combine(path, ".git", "MERGE_MSG");
            if (File.Exists(msgFile))
            {
                var firstLine = File.ReadLines(msgFile).FirstOrDefault() ?? string.Empty;
                var m = Regex.Match(firstLine, @"Merge (?:branch|remote-tracking branch|tag) '([^']+)'");
                if (m.Success) dto.SourceBranch = m.Groups[1].Value;
            }
        }
        catch { /* 读取失败不影响判定 */ }

        // 冲突数：porcelain 输出中未解决条目计数
        var status = RunGit(path, "status", "--porcelain=v1");
        if (status.ExitCode == 0)
        {
            dto.Conflicts = status.Output.Split('\n').Count(l =>
            {
                var t = l.TrimEnd('\r');
                return t.Length >= 2 && (t[0] == 'U' || t[1] == 'U' || t.StartsWith("AA") || t.StartsWith("DD"));
            });
        }
        return dto;
    }

    // ============================ 内部实现 ============================

    /// <summary>解析目录所属仓库的根目录（非仓库返回 null）。</summary>
    private string? ResolveRepoRoot(string path)
    {
        var result = RunGit(path, "rev-parse", "--show-toplevel");
        if (result.ExitCode != 0) return null;
        var root = result.Output.Trim().Replace('/', '\\');
        return root.Length == 0 ? null : root;
    }

    /// <summary>当前分支名（游离 HEAD 或空仓库返回空串）。</summary>
    private string GetPlainBranch(string path)
    {
        var result = RunGit(path, "rev-parse", "--abbrev-ref", "HEAD");
        var branch = result.Output.Trim();
        return result.ExitCode == 0 && branch.Length > 0 && branch != "HEAD" ? branch : string.Empty;
    }

    private static bool HasNoUpstream(string output) =>
        output.Contains("no upstream", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("no tracking", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("当前分支没有", StringComparison.Ordinal);

    private static GitCommandResultDto ToResult((int ExitCode, string Output) r) =>
        new()
        {
            Success = r.ExitCode == 0,
            ExitCode = r.ExitCode,
            Output = r.Output,
        };

    /// <summary>
    /// 执行 git 命令核心：ArgumentList 直传防注入、UTF-8 输出、
    /// core.quotepath=false 让中文文件名正常显示、超时杀进程树；
    /// 提供 opId 时注册进可取消注册表，取消/结束后自动注销。
    /// </summary>
    private (int ExitCode, string Output) RunGit(string workDir, params string[] args)
        => RunGit(workDir, LocalTimeoutMs, null, args);

    private (int ExitCode, string Output) RunGit(string workDir, int timeoutMs, string? opId, params string[] args)
    {
        if (string.IsNullOrWhiteSpace(workDir) || !Directory.Exists(workDir))
            return (-1, $"目录不存在: {workDir}");

        var psi = new ProcessStartInfo
        {
            FileName = "git",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = workDir,
        };
        // 中文文件名不再被转义成 \xxx 八进制
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("core.quotepath=false");
        foreach (var a in args) psi.ArgumentList.Add(a);
        // 凭证缺失时快速失败，绝不挂起等待终端输入
        psi.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("无法启动 git，请确认已安装并加入 PATH");

        // 可取消操作注册：结束后（任何路径）统一注销
        if (!string.IsNullOrEmpty(opId))
        {
            lock (_opsLock) { _runningOps[opId] = proc; }
        }
        try
        {
            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();

            if (!proc.WaitForExit(timeoutMs))
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
                throw new TimeoutException($"git 命令超时（{timeoutMs / 1000} 秒）已终止: git {string.Join(' ', args)}");
            }

            var stdout = stdoutTask.Result;
            var stderr = stderrTask.Result;
            // git 把进度类信息写到 stderr（如 pull/push 的对象计数），统一合并展示
            var output = stdout;
            if (stderr.Length > 0)
                output = output.Length == 0 ? stderr : output + "\n" + stderr;
            return (proc.ExitCode, output);
        }
        finally
        {
            if (!string.IsNullOrEmpty(opId))
            {
                lock (_opsLock) { _runningOps.Remove(opId); }
            }
        }
    }

    /// <summary>按空格切分命令参数，双引号内空格不切分（如 commit -m "fix: xxx"）。</summary>
    private static string[] SplitArguments(string text)
    {
        var args = new List<string>();
        var sb = new StringBuilder();
        var inQuote = false;
        foreach (var c in text)
        {
            if (c == '"') { inQuote = !inQuote; continue; }
            if (char.IsWhiteSpace(c) && !inQuote)
            {
                if (sb.Length > 0) { args.Add(sb.ToString()); sb.Clear(); }
                continue;
            }
            sb.Append(c);
        }
        if (sb.Length > 0) args.Add(sb.ToString());
        return args.ToArray();
    }

    // ============================ 持久化 ============================

    private void Load()
    {
        try
        {
            if (!File.Exists(StorePath)) return;
            _repos = JsonSerializer.Deserialize<List<GitRepo>>(File.ReadAllText(StorePath), JsonOpts) ?? new();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读取仓库列表失败");
        }
    }

    /// <summary>保存到磁盘（调用方需持有 _lock）。</summary>
    private void SaveLocked()
    {
        File.WriteAllText(StorePath, JsonSerializer.Serialize(_repos, JsonOpts));
    }

    // ============================ 环境检测与配置管理 ============================

    /// <summary>检测 git 是否已安装并读取全局身份配置。</summary>
    public GitEnvDto GetEnv()
    {
        var dto = new GitEnvDto();
        try
        {
            var ver = RunGitRaw(null, "--version");
            if (ver.ExitCode != 0) return dto; // git 未安装
            dto.Installed = true;
            // 解析版本号：git version 2.43.0.windows.1 → 2.43.0
            var m = Regex.Match(ver.Output.Trim(), @"(\d+\.\d+\.\d+)");
            dto.Version = m.Success ? m.Groups[1].Value : ver.Output.Trim();
            dto.UserName = RunGitRaw(null, "config", "--global", "user.name").Output.Trim();
            dto.UserEmail = RunGitRaw(null, "config", "--global", "user.email").Output.Trim();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // git 未安装：Process.Start 抛 Win32Exception
        }
        return dto;
    }

    /// <summary>读取全局 git config 列表（--global --list）。</summary>
    public List<GitConfigItemDto> GetConfigList()
    {
        var list = new List<GitConfigItemDto>();
        var result = RunGitRaw(null, "config", "--global", "--list");
        if (result.ExitCode != 0) return list;
        foreach (var line in result.Output.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (string.IsNullOrEmpty(trimmed)) continue;
            var eq = trimmed.IndexOf('=');
            if (eq <= 0) continue;
            list.Add(new GitConfigItemDto
            {
                Key = trimmed[..eq],
                Value = trimmed[(eq + 1)..]
            });
        }
        return list;
    }

    /// <summary>设置或删除一项全局配置。</summary>
    public GitCommandResultDto SetConfig(string key, string? value)
    {
        // 严格校验 key：仅允许字母/数字/.-，必须包含至少一个点
        if (!Regex.IsMatch(key ?? string.Empty, @"^[A-Za-z][A-Za-z0-9]*\.[A-Za-z0-9][A-Za-z0-9._-]*$"))
            throw new ArgumentException($"配置键格式非法：{key}");

        GitCommandResultDto result;
        if (value is null)
        {
            result = RunGitRaw(null, "config", "--global", "--unset", key!);
            // exit 5 = 键不存在，也算成功
            if (result.ExitCode != 0 && result.ExitCode != 5)
                throw new InvalidOperationException("删除配置失败: " + result.Output);
        }
        else
        {
            result = RunGitRaw(null, "config", "--global", key!, value);
            if (result.ExitCode != 0)
                throw new InvalidOperationException("设置配置失败: " + result.Output);
        }
        return new GitCommandResultDto { Success = true, Output = value is null ? $"已删除 {key}" : $"已设置 {key}={value}", ExitCode = 0 };
    }

    /// <summary>运行 git 命令（不需要仓库路径，如 --version / config --global）。</summary>
    private GitCommandResultDto RunGitRaw(string? workDir, params string[] args)
    {
        try
        {
            using var p = new Process();
            p.StartInfo = new ProcessStartInfo("git")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                CreateNoWindow = true,
            };
            if (!string.IsNullOrEmpty(workDir)) p.StartInfo.WorkingDirectory = workDir;
            foreach (var a in args) p.StartInfo.ArgumentList.Add(a);
            p.Start();
            var stdout = p.StandardOutput.ReadToEnd();
            var stderr = p.StandardError.ReadToEnd();
            p.WaitForExit();
            return new GitCommandResultDto
            {
                Success = p.ExitCode == 0,
                Output = (stdout + stderr).Trim(),
                ExitCode = p.ExitCode
            };
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return new GitCommandResultDto { Success = false, Output = "git 未安装或未加入 PATH", ExitCode = -1 };
        }
    }
}

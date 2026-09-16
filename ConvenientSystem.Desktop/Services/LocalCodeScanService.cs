using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using ConvenientSystem.Shared.Model.Common;
using Microsoft.Extensions.Logging;

namespace ConvenientSystem;

/// <summary>
/// 代码扫描本地服务：规则 JSON 持久化（exe 同目录 code-scan-rules.json）+ 正则扫描引擎。
/// 与 ConvenientSystem.Service 中的 CodeScanService 逻辑一致，
/// 但存储从数据库改为本地 JSON 文件（Desktop 无主库连接）。
/// 扫描历史仅存内存，exe 重启后清空。
/// </summary>
public sealed class LocalCodeScanService
{
    private sealed class RuleEntry
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Pattern { get; set; } = string.Empty;
        public string Severity { get; set; } = "Warning";
        public string FileGlob { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;
        public string Description { get; set; } = string.Empty;
    }

    private sealed class StoreFile
    {
        public List<RuleEntry> Rules { get; set; } = new();
    }

    private static string RulesFilePath => Path.Combine(AppContext.BaseDirectory, "code-scan-rules.json");

    private readonly ConcurrentDictionary<int, RuleEntry> _rules = new();
    private readonly List<CodeScanResultDto> _results = new();
    private readonly ILogger<LocalCodeScanService> _logger;
    private readonly object _saveLock = new();
    private int _nextRuleId = 1;
    private int _nextResultId = 1;

    /// <summary>扫描时跳过的目录名。</summary>
    private static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", ".git", "bin", "obj", "dist", ".vs", ".vscode",
        "__pycache__", ".next", ".nuxt", "vendor", "packages"
    };

    /// <summary>扫描时跳过的文件扩展名（二进制/生成文件）。</summary>
    private static readonly HashSet<string> SkipExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dll", ".exe", ".pdb", ".png", ".jpg", ".jpeg", ".gif", ".ico",
        ".woff", ".woff2", ".ttf", ".eot", ".mp4", ".mp3", ".zip",
        ".tar", ".gz", ".bak", ".lock", ".map"
    };

    public LocalCodeScanService(ILogger<LocalCodeScanService> logger)
    {
        _logger = logger;
        Load();
    }

    // ===== 规则 CRUD =====

    public List<CodeScanRuleDto> ListRules()
        => _rules.Values.OrderBy(r => r.Id).Select(ToDto).ToList();

    public void SaveRule(CodeScanRuleSaveDto dto)
    {
        try { _ = new Regex(dto.Pattern); }
        catch (ArgumentException ex) { throw new ArgumentException($"正则表达式无效: {ex.Message}"); }

        if (dto.Id is int id && id > 0 && _rules.ContainsKey(id))
        {
            var existing = _rules[id];
            existing.Name = dto.Name;
            existing.Pattern = dto.Pattern;
            existing.Severity = dto.Severity;
            existing.FileGlob = dto.FileGlob;
            existing.Enabled = dto.Enabled;
            existing.Description = dto.Description;
        }
        else
        {
            var newId = Interlocked.Increment(ref _nextRuleId) - 1;
            _rules[newId] = new RuleEntry
            {
                Id = newId,
                Name = dto.Name,
                Pattern = dto.Pattern,
                Severity = dto.Severity,
                FileGlob = dto.FileGlob,
                Enabled = dto.Enabled,
                Description = dto.Description,
            };
        }
        PersistRules();
    }

    public void DeleteRule(int id)
    {
        _rules.TryRemove(id, out _);
        PersistRules();
    }

    public void ImportTemplates()
    {
        var templates = new (string Name, string Pattern, string Severity, string FileGlob, string Description)[]
        {
            ("硬编码密码", @"(?i)(password|passwd|pwd)\s*[:=]\s*""[^""]{3,}""", "Error", "*.cs;*.json;*.ts", "检测代码中硬编码的密码字符串"),
            ("连接串明文", @"(?i)(password|pwd)\s*=\s*[^;\s""']+", "Error", "*.json;*.config;*.xml", "检测配置文件中的明文连接字符串密码"),
            ("Console输出残留", @"Console\.(Write|WriteLine)\(", "Warning", "*.cs", "生产代码中不应出现 Console 输出"),
            ("TODO/HACK注释", @"(?i)\b(TODO|HACK|FIXME|XXX)\b", "Info", "*.cs;*.ts;*.vue;*.js", "标记待处理的 TODO/HACK/FIXME 注释"),
            ("Thread.Sleep", @"\bThread\.Sleep\s*\(", "Warning", "*.cs", "应使用 async/await + Task.Delay 替代 Thread.Sleep"),
            ("硬编码IP地址", @"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b", "Info", "*.cs;*.json;*.ts", "硬编码 IP 地址应提取为配置项"),
        };

        foreach (var (name, pattern, severity, fileGlob, description) in templates)
        {
            if (_rules.Values.Any(r => r.Name == name)) continue;
            var newId = Interlocked.Increment(ref _nextRuleId) - 1;
            _rules[newId] = new RuleEntry
            {
                Id = newId, Name = name, Pattern = pattern,
                Severity = severity, FileGlob = fileGlob,
                Enabled = true, Description = description,
            };
        }
        PersistRules();
    }

    // ===== 扫描执行 =====

    public CodeScanResultDto Scan(CodeScanRequestDto request)
    {
        var sw = Stopwatch.StartNew();
        var targetPath = Path.GetFullPath(request.TargetPath);
        if (!Directory.Exists(targetPath))
            throw new DirectoryNotFoundException($"目录不存在: {targetPath}");

        var enabledRules = _rules.Values.Where(r => r.Enabled).ToList();
        if (enabledRules.Count == 0)
            throw new InvalidOperationException("没有启用的检测规则");

        var compiledRules = enabledRules.Select(r => new
        {
            Rule = r,
            Regex = new Regex(r.Pattern, RegexOptions.Compiled),
            Globs = string.IsNullOrWhiteSpace(r.FileGlob)
                ? null
                : r.FileGlob.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        }).ToList();

        var files = request.ScanMode == "diff"
            ? GetGitDiffFiles(targetPath)
            : GetAllFiles(targetPath);

        var issues = new List<CodeScanIssueDto>();
        var nextIssueId = 1;

        foreach (var file in files)
        {
            var ext = Path.GetExtension(file);
            if (SkipExts.Contains(ext)) continue;

            var relativePath = Path.GetRelativePath(targetPath, file);

            foreach (var cr in compiledRules)
            {
                if (cr.Globs != null && !cr.Globs.Any(g => MatchGlob(file, g)))
                    continue;

                string[] lines;
                try { lines = File.ReadAllLines(file); }
                catch { continue; }

                for (int i = 0; i < lines.Length; i++)
                {
                    if (cr.Regex.IsMatch(lines[i]))
                    {
                        issues.Add(new CodeScanIssueDto
                        {
                            Id = nextIssueId++,
                            RuleName = cr.Rule.Name,
                            Severity = cr.Rule.Severity,
                            FilePath = relativePath,
                            LineNumber = i + 1,
                            LineContent = lines[i].Length > 200 ? lines[i][..200] + "..." : lines[i],
                        });
                    }
                }
            }
        }

        sw.Stop();

        var result = new CodeScanResultDto
        {
            Id = Interlocked.Increment(ref _nextResultId) - 1,
            TargetPath = request.TargetPath,
            ScanMode = request.ScanMode,
            FileCount = files.Count,
            IssueCount = issues.Count,
            ErrorCount = issues.Count(i => i.Severity == "Error"),
            WarningCount = issues.Count(i => i.Severity == "Warning"),
            InfoCount = issues.Count(i => i.Severity == "Info"),
            DurationMs = sw.ElapsedMilliseconds,
            CreateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            Issues = issues.OrderBy(i => i.FilePath).ThenBy(i => i.LineNumber).ToList(),
        };

        lock (_results) { _results.Insert(0, result); }
        _logger.LogInformation("本地代码扫描完成: {Files} 文件, {Issues} 问题, {Ms}ms",
            files.Count, issues.Count, sw.ElapsedMilliseconds);

        return result;
    }

    public List<CodeScanResultDto> ListResults(int limit = 20)
    {
        lock (_results) return _results.Take(limit).ToList();
    }

    public CodeScanResultDto? GetResult(int scanId)
    {
        lock (_results) return _results.FirstOrDefault(r => r.Id == scanId);
    }

    // ===== 私有辅助 =====

    private void Load()
    {
        try
        {
            if (!File.Exists(RulesFilePath)) return;
            var file = JsonSerializer.Deserialize<StoreFile>(File.ReadAllText(RulesFilePath));
            if (file?.Rules == null) return;
            foreach (var rule in file.Rules)
            {
                _rules[rule.Id] = rule;
                if (rule.Id >= _nextRuleId) _nextRuleId = rule.Id + 1;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "加载 code-scan-rules.json 失败，将以空规则启动");
        }
    }

    private void PersistRules()
    {
        lock (_saveLock)
        {
            try
            {
                var file = new StoreFile { Rules = _rules.Values.OrderBy(r => r.Id).ToList() };
                var json = JsonSerializer.Serialize(file, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(RulesFilePath, json);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "写入 code-scan-rules.json 失败");
            }
        }
    }

    private static CodeScanRuleDto ToDto(RuleEntry r)
        => new() { Id = r.Id, Name = r.Name, Pattern = r.Pattern, Severity = r.Severity, FileGlob = r.FileGlob, Enabled = r.Enabled, Description = r.Description };

    private List<string> GetAllFiles(string rootDir)
    {
        var files = new List<string>();
        WalkDirectory(rootDir, files);
        return files;
    }

    private void WalkDirectory(string dir, List<string> files)
    {
        try
        {
            foreach (var file in Directory.GetFiles(dir))
                files.Add(file);

            foreach (var subDir in Directory.GetDirectories(dir))
            {
                var dirName = Path.GetFileName(subDir);
                if (!SkipDirs.Contains(dirName))
                    WalkDirectory(subDir, files);
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
    }

    private List<string> GetGitDiffFiles(string repoDir)
    {
        try
        {
            var psi = new ProcessStartInfo("git", "diff --name-only HEAD")
            {
                WorkingDirectory = repoDir,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi)!;
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();

            return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(f => Path.Combine(repoDir, f))
                .Where(File.Exists)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning("git diff 失败，回退到全量扫描: {Msg}", ex.Message);
            return GetAllFiles(repoDir);
        }
    }

    private static bool MatchGlob(string filePath, string pattern)
    {
        var fileName = Path.GetFileName(filePath);
        var regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return Regex.IsMatch(fileName, regexPattern, RegexOptions.IgnoreCase);
    }
}

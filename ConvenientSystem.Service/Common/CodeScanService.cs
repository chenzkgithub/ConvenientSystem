using ConvenientSystem.Shared.Common;
using System.Diagnostics;
using System.Text.RegularExpressions;
using ConvenientSystem.Shared.Entity.Common;
using ConvenientSystem.Shared.Model.Common;
using FreeSql;
using Microsoft.Extensions.Logging;

namespace ConvenientSystem.Service.Common;

/// <summary>
/// 代码扫描服务：基于正则规则 + Git Diff 增量扫描。
/// 规则存 CodeScanRule 表，扫描记录存 CodeScanResult/CodeScanIssue 表。
/// </summary>
public class CodeScanService : ICodeScanService
{
    private readonly IFreeSql _db;
    private readonly ILogger<CodeScanService> _logger;

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

    public CodeScanService(IFreeSql db, ILogger<CodeScanService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public List<CodeScanRuleDto> ListRules()
    {
        return _db.Select<CodeScanRuleEntity>()
            .OrderBy(r => r.Id)
            .ToList(r => new CodeScanRuleDto
            {
                Id = r.Id,
                Name = r.Name,
                Pattern = r.Pattern,
                Severity = r.Severity,
                FileGlob = r.FileGlob,
                Enabled = r.Enabled,
                Description = r.Description,
            });
    }

    public void SaveRule(CodeScanRuleSaveDto dto)
    {
        // 验证正则合法性
        try { _ = new Regex(dto.Pattern); }
        catch (ArgumentException ex) { throw new ArgumentException($"正则表达式无效: {ex.Message}"); }

        if (dto.Id is int id && id > 0)
        {
            _db.Update<CodeScanRuleEntity>()
                .Set(r => r.Name, dto.Name)
                .Set(r => r.Pattern, dto.Pattern)
                .Set(r => r.Severity, dto.Severity)
                .Set(r => r.FileGlob, dto.FileGlob)
                .Set(r => r.Enabled, dto.Enabled)
                .Set(r => r.Description, dto.Description)
                .Where(r => r.Id == id)
                .ExecuteAffrows();
        }
        else
        {
            _db.Insert(new CodeScanRuleEntity
            {
                Name = dto.Name,
                Pattern = dto.Pattern,
                Severity = dto.Severity,
                FileGlob = dto.FileGlob,
                Enabled = dto.Enabled,
                Description = dto.Description,
            }).ExecuteAffrows();
        }
    }

    public void DeleteRule(int id)
    {
        _db.Delete<CodeScanRuleEntity>().Where(r => r.Id == id).ExecuteAffrows();
    }

    public void ImportTemplates()
    {
        var templates = new CodeScanRuleEntity[]
        {
            new() { Name = "硬编码密码", Pattern = @"(?i)(password|passwd|pwd)\s*[:=]\s*""[^""]{3,}""", Severity = "Error", FileGlob = "*.cs;*.json;*.ts", Enabled = true, Description = "检测代码中硬编码的密码字符串" },
            new() { Name = "连接串明文", Pattern = @"(?i)(password|pwd)\s*=\s*[^;\s""']+", Severity = "Error", FileGlob = "*.json;*.config;*.xml", Enabled = true, Description = "检测配置文件中的明文连接字符串密码" },
            new() { Name = "Console输出残留", Pattern = @"Console\.(Write|WriteLine)\(", Severity = "Warning", FileGlob = "*.cs", Enabled = true, Description = "生产代码中不应出现 Console 输出" },
            new() { Name = "TODO/HACK注释", Pattern = @"(?i)\b(TODO|HACK|FIXME|XXX)\b", Severity = "Info", FileGlob = "*.cs;*.ts;*.vue;*.js", Enabled = true, Description = "标记待处理的 TODO/HACK/FIXME 注释" },
            new() { Name = "Thread.Sleep", Pattern = @"\bThread\.Sleep\s*\(", Severity = "Warning", FileGlob = "*.cs", Enabled = true, Description = "应使用 async/await + Task.Delay 替代 Thread.Sleep" },
            new() { Name = "硬编码IP地址", Pattern = @"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b", Severity = "Info", FileGlob = "*.cs;*.json;*.ts", Enabled = true, Description = "硬编码 IP 地址应提取为配置项" },
        };

        foreach (var t in templates)
        {
            var exists = _db.Select<CodeScanRuleEntity>()
                .Where(r => r.Name == t.Name)
                .Any();
            if (!exists)
                _db.Insert(t).ExecuteAffrows();
        }
    }

    public CodeScanResultDto Scan(CodeScanRequestDto request)
    {
        var sw = Stopwatch.StartNew();
        var targetPath = Path.GetFullPath(request.TargetPath);
        if (!Directory.Exists(targetPath))
            throw new DirectoryNotFoundException($"目录不存在: {targetPath}");

        // 获取启用的规则
        var rules = _db.Select<CodeScanRuleEntity>()
            .Where(r => r.Enabled)
            .ToList();

        if (rules.Count == 0)
            throw new InvalidOperationException("没有启用的检测规则");

        // 编译正则
        var compiledRules = rules.Select(r => new
        {
            Rule = r,
            Regex = new Regex(r.Pattern, RegexOptions.Compiled),
            Globs = string.IsNullOrWhiteSpace(r.FileGlob)
                ? null
                : r.FileGlob.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        }).ToList();

        // 获取待扫描文件列表
        var files = request.ScanMode == "diff"
            ? GetGitDiffFiles(targetPath)
            : GetAllFiles(targetPath);

        var issues = new List<CodeScanIssueDto>();

        foreach (var file in files)
        {
            var ext = Path.GetExtension(file);
            if (SkipExts.Contains(ext)) continue;

            var relativePath = Path.GetRelativePath(targetPath, file);

            foreach (var cr in compiledRules)
            {
                // glob 过滤
                if (cr.Globs != null && !cr.Globs.Any(g => MatchGlob(file, g)))
                    continue;

                string[] lines;
                try { lines = File.ReadAllLines(file); }
                catch { continue; } // 无法读取则跳过

                for (int i = 0; i < lines.Length; i++)
                {
                    if (cr.Regex.IsMatch(lines[i]))
                    {
                        issues.Add(new CodeScanIssueDto
                        {
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

        // 写入数据库
        var resultEntity = new CodeScanResultEntity
        {
            TargetPath = request.TargetPath,
            ScanMode = request.ScanMode,
            FileCount = files.Count,
            IssueCount = issues.Count,
            ErrorCount = issues.Count(i => i.Severity == "Error"),
            WarningCount = issues.Count(i => i.Severity == "Warning"),
            InfoCount = issues.Count(i => i.Severity == "Info"),
            DurationMs = sw.ElapsedMilliseconds,
            CreateTime = TimeHelper.Now,
        };
        _db.Insert(resultEntity).ExecuteAffrows();

        var scanId = resultEntity.Id;
        if (issues.Count > 0)
        {
            var entities = issues.Select(i => new CodeScanIssueEntity
            {
                ScanId = scanId,
                RuleName = i.RuleName,
                Severity = i.Severity,
                FilePath = i.FilePath,
                LineNumber = i.LineNumber,
                LineContent = i.LineContent,
            }).ToList();
            _db.Insert(entities).ExecuteAffrows();

            // 回填 Id
            var saved = _db.Select<CodeScanIssueEntity>()
                .Where(i => i.ScanId == scanId)
                .OrderBy(i => i.Id)
                .ToList(i => new CodeScanIssueDto
                {
                    Id = i.Id,
                    RuleName = i.RuleName,
                    Severity = i.Severity,
                    FilePath = i.FilePath,
                    LineNumber = i.LineNumber,
                    LineContent = i.LineContent,
                });
            issues = saved;
        }

        _logger.LogInformation("代码扫描完成: {Files} 文件, {Issues} 问题, {Ms}ms",
            files.Count, issues.Count, sw.ElapsedMilliseconds);

        return new CodeScanResultDto
        {
            Id = scanId,
            TargetPath = request.TargetPath,
            ScanMode = request.ScanMode,
            FileCount = files.Count,
            IssueCount = issues.Count,
            ErrorCount = resultEntity.ErrorCount,
            WarningCount = resultEntity.WarningCount,
            InfoCount = resultEntity.InfoCount,
            DurationMs = sw.ElapsedMilliseconds,
            CreateTime = resultEntity.CreateTime.ToString("yyyy-MM-dd HH:mm:ss"),
            Issues = issues,
        };
    }

    public List<CodeScanResultDto> ListResults(int limit = 20)
    {
        return _db.Select<CodeScanResultEntity>()
            .OrderByDescending(r => r.Id)
            .Take(limit)
            .ToList(r => new CodeScanResultDto
            {
                Id = r.Id,
                TargetPath = r.TargetPath,
                ScanMode = r.ScanMode,
                FileCount = r.FileCount,
                IssueCount = r.IssueCount,
                ErrorCount = r.ErrorCount,
                WarningCount = r.WarningCount,
                InfoCount = r.InfoCount,
                DurationMs = r.DurationMs,
                CreateTime = r.CreateTime.ToString("yyyy-MM-dd HH:mm:ss"),
            });
    }

    public CodeScanResultDto GetResult(int scanId)
    {
        var result = _db.Select<CodeScanResultEntity>()
            .Where(r => r.Id == scanId)
            .First(r => new CodeScanResultDto
            {
                Id = r.Id,
                TargetPath = r.TargetPath,
                ScanMode = r.ScanMode,
                FileCount = r.FileCount,
                IssueCount = r.IssueCount,
                ErrorCount = r.ErrorCount,
                WarningCount = r.WarningCount,
                InfoCount = r.InfoCount,
                DurationMs = r.DurationMs,
                CreateTime = r.CreateTime.ToString("yyyy-MM-dd HH:mm:ss"),
            });

        if (result != null)
        {
            result.Issues = _db.Select<CodeScanIssueEntity>()
                .Where(i => i.ScanId == scanId)
                .ToList(i => new CodeScanIssueDto
                {
                    Id = i.Id,
                    RuleName = i.RuleName,
                    Severity = i.Severity,
                    FilePath = i.FilePath,
                    LineNumber = i.LineNumber,
                    LineContent = i.LineContent,
                })
                .OrderBy(i => i.FilePath).ThenBy(i => i.LineNumber)
                .ToList();
        }

        return result!;
    }

    // ===== 私有辅助方法 =====

    /// <summary>遍历目录获取所有文件（跳过 node_modules/.git/bin/obj 等）。</summary>
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
        catch (UnauthorizedAccessException) { /* 跳过无权限目录 */ }
        catch (IOException) { /* 跳过被占用目录 */ }
    }

    /// <summary>通过 git diff 获取变更文件列表。</summary>
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

    /// <summary>简单的 glob 匹配（支持 * 和 ? 通配符）。</summary>
    private static bool MatchGlob(string filePath, string pattern)
    {
        var fileName = Path.GetFileName(filePath);
        var regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return Regex.IsMatch(fileName, regexPattern, RegexOptions.IgnoreCase);
    }
}

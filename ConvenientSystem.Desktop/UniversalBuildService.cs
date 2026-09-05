using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ConvenientSystem.Desktop;

/// <summary>通用构建任务类型。</summary>
public enum UniversalBuildType
{
    Web,
    Node,
    DotNet,
    JavaMaven,
    JavaGradle,
    Installer,
}

/// <summary>通用构建任务状态。</summary>
public enum UniversalBuildStatus
{
    Pending,
    Waiting,
    Running,
    Success,
    Failed,
    Cancelled,
}

/// <summary>环境检测结果。</summary>
public sealed class UniversalEnvironmentInfo
{
    public string Type { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool Installed { get; set; }
    public string Version { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
}

/// <summary>通用构建任务请求。</summary>
public sealed class UniversalBuildRequest
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public UniversalBuildType Type { get; set; }
    public string ProjectDir { get; set; } = string.Empty;
    public string? OutputDir { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>构建前先执行 git pull --ff-only 拉取远端最新代码。</summary>
    public bool PrePull { get; set; }
}

/// <summary>通用构建任务 DTO。</summary>
public sealed class UniversalBuildJobDto
{
    public string Id { get; set; } = string.Empty;
    public UniversalBuildType Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public UniversalBuildStatus Status { get; set; }
    public string ProjectDir { get; set; } = string.Empty;
    public string OutputDir { get; set; } = string.Empty;
    public int Progress { get; set; }
    /// <summary>排队位置（仅 Waiting 状态有值，按启动顺序 1 起）。</summary>
    public int? QueuePosition { get; set; }
    public string Log { get; set; } = string.Empty;
    public int? ExitCode { get; set; }
    /// <summary>构建产物总大小（字节，构建成功后统计；失败/未构建为 null）。</summary>
    public long? ArtifactSize { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? CompletedTime { get; set; }
}

/// <summary>产物目录占用统计条目。</summary>
public sealed class ArtifactUsageItem
{
    public string Path { get; set; } = string.Empty;
    /// <summary>目录是否存在。</summary>
    public bool Exists { get; set; }
    /// <summary>总大小（字节）。</summary>
    public long SizeBytes { get; set; }
    /// <summary>文件数（含子目录）。</summary>
    public int FileCount { get; set; }
    /// <summary>最近一次产物修改时间。</summary>
    public DateTime? LastWriteTime { get; set; }
}

/// <summary>通用构建任务内部模型。</summary>
public sealed class UniversalBuildJob
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public UniversalBuildType Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ProjectDir { get; set; } = string.Empty;
    public string OutputDir { get; set; } = string.Empty;
    public UniversalBuildStatus Status { get; set; } = UniversalBuildStatus.Pending;
    public int Progress { get; set; }
    /// <summary>最近命中的阶段锚点进度（日志关键字触发），插值起点。</summary>
    public int ProgressAnchor { get; set; }
    /// <summary>进入当前锚点的时间（Running 开始时初始化），插值时间基准。</summary>
    public DateTime AnchorTime { get; set; }
    public StringBuilder Log { get; set; } = new();
    public int? ExitCode { get; set; }
    /// <summary>构建产物总大小（字节，构建成功后统计）。</summary>
    public long? ArtifactSize { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? CompletedTime { get; set; }
    public CancellationTokenSource? Cts { get; set; }
    /// <summary>构建前先执行 git pull --ff-only 拉取远端最新代码。</summary>
    public bool PrePull { get; set; }
}

/// <summary>通用本地构建服务：支持 Web/Node/C#/Java/Maven/Gradle/Installer。</summary>
public sealed class UniversalBuildService
{
    private readonly ILogger<UniversalBuildService> _logger;
    private readonly ConcurrentDictionary<string, UniversalBuildJob> _jobs = new();
    private readonly SemaphoreSlim _concurrency = new(10, 10);

    public UniversalBuildService(ILogger<UniversalBuildService> logger)
    {
        _logger = logger;
    }

    /// <summary>检测所有支持的环境。</summary>
    public IReadOnlyList<UniversalEnvironmentInfo> CheckEnvironment()
    {
        return new List<UniversalEnvironmentInfo>
        {
            CheckCommand("node", "Node.js", "node --version", "https://nodejs.org/", @"v(\d+\.\d+\.\d+)"),
            CheckCommand("npm", "npm", "npm --version", "https://nodejs.org/", @"(\d+\.\d+\.\d+)"),
            CheckCommand("dotnet", ".NET SDK", "dotnet --version", "https://dotnet.microsoft.com/download", @"(\d+\.\d+\.\d+)"),
            CheckCommand("java", "Java", "java -version", "https://adoptium.net/", @"version ""(\d+[\.\d+]*)"""),
            CheckCommand("mvn", "Maven", "mvn -version", "https://maven.apache.org/download.cgi", @"Apache Maven (\d+\.\d+\.\d+)"),
            CheckCommand("gradle", "Gradle", "gradle -v", "https://gradle.org/install/", @"Gradle (\d+\.\d+\.\d+)"),
            CheckCommand("iscc", "Inno Setup", "iscc /?", "https://jrsoftware.org/isdl.php", null),
        };
    }

    /// <summary>检测指定类型的环境是否就绪。</summary>
    public IReadOnlyList<UniversalEnvironmentInfo> CheckEnvironmentForType(UniversalBuildType type)
    {
        var required = GetRequiredCommands(type);
        var all = CheckEnvironment();
        return all.Where(x => required.Contains(x.Type, StringComparer.OrdinalIgnoreCase)).ToList();
    }

    private static IReadOnlyCollection<string> GetRequiredCommands(UniversalBuildType type)
    {
        return type switch
        {
            UniversalBuildType.Web or UniversalBuildType.Node => new[] { "node", "npm" },
            UniversalBuildType.DotNet => new[] { "dotnet" },
            UniversalBuildType.JavaMaven => new[] { "java", "mvn" },
            UniversalBuildType.JavaGradle => new[] { "java", "gradle" },
            UniversalBuildType.Installer => new[] { "iscc" },
            _ => Array.Empty<string>(),
        };
    }

    private static UniversalEnvironmentInfo CheckCommand(string type, string name, string command, string downloadUrl, string? versionPattern)
    {
        var info = new UniversalEnvironmentInfo
        {
            Type = type,
            Name = name,
            DownloadUrl = downloadUrl,
        };

        try
        {
            var parts = command.Split(' ', 2);
            var fileName = parts[0];
            var arguments = parts.Length > 1 ? parts[1] : string.Empty;

            // 优先 PATH 查找，找不到时回退到常见安装路径（与构建与发布页面一致）
            var resolved = LocalBuildService.FindInPath(fileName);
            if (string.IsNullOrEmpty(resolved))
                resolved = GetCandidatePath(fileName) ?? fileName;

            var psi = new ProcessStartInfo(resolved, arguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                info.Message = "未检测到";
                return info;
            }

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit(5000);

            var combined = output + Environment.NewLine + error;
            if (process.ExitCode != 0 && type != "java" && type != "iscc")
            {
                info.Message = "检测失败";
                return info;
            }

            info.Installed = true;
            if (!string.IsNullOrEmpty(versionPattern))
            {
                var match = Regex.Match(combined, versionPattern);
                info.Version = match.Success ? match.Groups[1].Value : combined.Trim().Split('\n').FirstOrDefault()?.Trim() ?? string.Empty;
            }
            else
            {
                info.Version = "已安装";
            }
            info.Message = string.IsNullOrEmpty(info.Version) ? "已安装" : info.Version;
        }
        catch
        {
            info.Message = "未检测到";
        }

        return info;
    }

    /// <summary>根据命令名回退查找常见安装路径。</summary>
    private static string? GetCandidatePath(string command)
    {
        var candidates = command.ToLowerInvariant() switch
        {
            "dotnet" or "dotnet.exe" => LocalBuildService.DotNetCandidates(),
            "node" or "node.exe" => LocalBuildService.NodeCandidates(),
            "npm" or "npm.cmd" => LocalBuildService.NpmCandidates(),
            "iscc" or "iscc.exe" => LocalBuildService.InnoSetupCandidates(),
            "java" or "java.exe" => JavaCandidates(),
            "mvn" or "mvn.cmd" => MavenCandidates(),
            "gradle" or "gradle.bat" => GradleCandidates(),
            _ => Array.Empty<string>(),
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>Java 候选路径：优先 JAVA_HOME，再搜索常见安装目录。</summary>
    private static string[] JavaCandidates()
    {
        var list = new List<string>();

        // 1. JAVA_HOME 环境变量
        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrEmpty(javaHome))
            list.Add(Path.Combine(javaHome, "bin", "java.exe"));

        // 2. 常见安装目录（通配符搜索）
        var searchDirs = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".jdks"),
            @"D:\ch\install\jdk",
            @"D:\ch\install",
        };

        foreach (var baseDir in searchDirs.Distinct())
        {
            if (string.IsNullOrEmpty(baseDir) || !Directory.Exists(baseDir)) continue;
            try
            {
                // 直接命中 java.exe
                var direct = Path.Combine(baseDir, "bin", "java.exe");
                if (File.Exists(direct)) list.Add(direct);

                // 搜索子目录（jdk-xx、jdk_xx 等）
                foreach (var dir in Directory.GetDirectories(baseDir))
                {
                    var exe = Path.Combine(dir, "bin", "java.exe");
                    if (File.Exists(exe)) list.Add(exe);
                }
            }
            catch { /* 忽略权限错误 */ }
        }

        return list.ToArray();
    }

    /// <summary>Maven 候选路径：优先 MAVEN_HOME/M2_HOME，再搜索常见安装目录。</summary>
    private static string[] MavenCandidates()
    {
        var list = new List<string>();

        // 1. MAVEN_HOME / M2_HOME 环境变量
        var mavenHome = Environment.GetEnvironmentVariable("MAVEN_HOME")
                        ?? Environment.GetEnvironmentVariable("M2_HOME");
        if (!string.IsNullOrEmpty(mavenHome))
            list.Add(Path.Combine(mavenHome, "bin", "mvn.cmd"));

        // 2. 常见安装目录
        var searchDirs = new[]
        {
            @"C:\Program Files\apache-maven",
            @"D:\ch\install",
            @"D:\ch\install\mvn",
        };

        foreach (var baseDir in searchDirs)
        {
            if (!Directory.Exists(baseDir)) continue;
            try
            {
                // 直接命中
                var direct = Path.Combine(baseDir, "bin", "mvn.cmd");
                if (File.Exists(direct)) list.Add(direct);

                // 搜索 apache-maven-* 子目录
                foreach (var dir in Directory.GetDirectories(baseDir))
                {
                    var cmd = Path.Combine(dir, "bin", "mvn.cmd");
                    if (File.Exists(cmd)) list.Add(cmd);
                }
            }
            catch { /* 忽略权限错误 */ }
        }

        // 3. IDEA 内置 Maven
        var ideaPaths = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "JetBrains"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JetBrains"),
        };
        foreach (var ideaBase in ideaPaths)
        {
            if (!Directory.Exists(ideaBase)) continue;
            try
            {
                foreach (var ideaDir in Directory.GetDirectories(ideaBase))
                {
                    var mvnPath = Path.Combine(ideaDir, "plugins", "maven", "lib", "maven3", "bin", "mvn.cmd");
                    if (File.Exists(mvnPath)) list.Add(mvnPath);
                }
            }
            catch { /* 忽略权限错误 */ }
        }

        return list.ToArray();
    }

    /// <summary>Gradle 候选路径：优先 GRADLE_HOME，再搜索常见安装目录。</summary>
    private static string[] GradleCandidates()
    {
        var list = new List<string>();

        var gradleHome = Environment.GetEnvironmentVariable("GRADLE_HOME");
        if (!string.IsNullOrEmpty(gradleHome))
            list.Add(Path.Combine(gradleHome, "bin", "gradle.bat"));

        var searchDirs = new[]
        {
            @"C:\Program Files\gradle",
            @"D:\ch\install",
        };

        foreach (var baseDir in searchDirs)
        {
            if (!Directory.Exists(baseDir)) continue;
            try
            {
                var direct = Path.Combine(baseDir, "bin", "gradle.bat");
                if (File.Exists(direct)) list.Add(direct);

                foreach (var dir in Directory.GetDirectories(baseDir))
                {
                    var bat = Path.Combine(dir, "bin", "gradle.bat");
                    if (File.Exists(bat)) list.Add(bat);
                }
            }
            catch { /* 忽略权限错误 */ }
        }

        return list.ToArray();
    }

    /// <summary>启动构建任务。</summary>
    public UniversalBuildJobDto StartBuild(UniversalBuildRequest request)
    {
        var missing = CheckEnvironmentForType(request.Type)
            .Where(x => !x.Installed)
            .Select(x => x.Name)
            .ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException($"缺少环境：{string.Join(", ", missing)}，请先安装");
        }

        var job = new UniversalBuildJob
        {
            Id = request.Id,
            Type = request.Type,
            Name = request.Name,
            ProjectDir = request.ProjectDir,
            OutputDir = !string.IsNullOrWhiteSpace(request.OutputDir)
                ? request.OutputDir
                : GetDefaultOutputDir(request.Type, request.Name),
            Status = UniversalBuildStatus.Waiting,
            StartTime = DateTime.Now,
            PrePull = request.PrePull,
        };

        if (!_jobs.TryAdd(job.Id, job))
        {
            throw new InvalidOperationException($"任务 {job.Id} 已存在");
        }

        _ = Task.Run(async () => await RunBuildAsync(job));
        return ToDto(job);
    }

    /// <summary>获取任务进度。</summary>
    public UniversalBuildJobDto? GetProgress(string id)
    {
        if (!_jobs.TryGetValue(id, out var job)) return null;
        var dto = ToDto(job);
        dto.QueuePosition = GetQueuePosition(job);
        return dto;
    }

    /// <summary>排队位置：Waiting 任务按启动顺序编号（1 起），其余状态为 null。</summary>
    private int? GetQueuePosition(UniversalBuildJob job)
    {
        if (job.Status != UniversalBuildStatus.Waiting) return null;
        var ordered = _jobs.Values
            .Where(j => j.Status == UniversalBuildStatus.Waiting)
            .OrderBy(j => j.StartTime)
            .ToList();
        return ordered.IndexOf(job) + 1;
    }

    /// <summary>获取所有任务。</summary>
    public IReadOnlyList<UniversalBuildJobDto> GetAllJobs()
    {
        return _jobs.Values.Select(ToDto).ToList();
    }

    /// <summary>取消任务。</summary>
    public bool Cancel(string id)
    {
        if (!_jobs.TryGetValue(id, out var job)) return false;
        job.Cts?.Cancel();
        job.Status = UniversalBuildStatus.Cancelled;
        return true;
    }

    private async Task RunBuildAsync(UniversalBuildJob job)
    {
        await _concurrency.WaitAsync();
        try
        {
            if (job.Status == UniversalBuildStatus.Cancelled) return;

            job.Status = UniversalBuildStatus.Running;
            job.Cts = new CancellationTokenSource();
            job.AnchorTime = DateTime.Now;
            AppendLog(job, $">> 任务开始：{job.Name}");
            AppendLog(job, $">> 项目目录：{job.ProjectDir}");
            AppendLog(job, $">> 输出目录：{job.OutputDir}");
            AppendLog(job, string.Empty);

            // 构建前拉取远端最新代码（--ff-only：本地有分叉提交时失败而非产生 merge）
            if (job.PrePull)
            {
                await RunGitPullAsync(job);
            }

            Directory.CreateDirectory(job.OutputDir);

            var (fileName, arguments, workingDir) = GetCommandInfo(job);
            var resolved = LocalBuildService.FindInPath(fileName);
            if (string.IsNullOrEmpty(resolved))
                resolved = GetCandidatePath(fileName) ?? fileName;

            var psi = new ProcessStartInfo(resolved, arguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                CreateNoWindow = true,
                WorkingDirectory = workingDir,
            };

            AppendLog(job, $">> 执行：{fileName} {arguments}");
            AppendLog(job, string.Empty);

            using var process = Process.Start(psi);
            if (process is null)
            {
                job.ExitCode = -1;
                job.Status = UniversalBuildStatus.Failed;
                AppendLog(job, ">> 启动进程失败");
                return;
            }

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is null) return;
                AppendLog(job, e.Data);
                UpdateProgress(job, e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is null) return;
                AppendLog(job, e.Data);
                UpdateProgress(job, e.Data);
            };

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            try
            {
                await process.WaitForExitAsync(job.Cts.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(); } catch { /* ignore */ }
                job.Status = UniversalBuildStatus.Cancelled;
                AppendLog(job, ">> 任务已取消");
                return;
            }

            job.ExitCode = process.ExitCode;
            job.Status = process.ExitCode == 0 ? UniversalBuildStatus.Success : UniversalBuildStatus.Failed;
            job.Progress = job.Status == UniversalBuildStatus.Success ? 100 : job.Progress;
            AppendLog(job, string.Empty);
            AppendLog(job, $">> 任务结束，退出码：{process.ExitCode}");

            // 构建成功后把产物归集到输出目录（Web/Node 与 Java 命令不接收输出目录，产物默认落在项目内）
            if (job.Status == UniversalBuildStatus.Success)
            {
                CollectArtifacts(job);
                // 统计产物总大小并写入日志（前端卡片展示用）
                try
                {
                    job.ArtifactSize = Directory.EnumerateFiles(job.OutputDir, "*", SearchOption.AllDirectories)
                        .Sum(f => new FileInfo(f).Length);
                    AppendLog(job, $">> 产物大小：{FormatSize(job.ArtifactSize.Value)}");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "统计产物大小失败: {OutputDir}", job.OutputDir);
                }
            }
        }
        catch (Exception ex)
        {
            job.ExitCode = -1;
            job.Status = UniversalBuildStatus.Failed;
            AppendLog(job, $">> 异常：{ex.Message}");
            _logger.LogError(ex, "通用构建任务 {JobId} 执行异常", job.Id);
        }
        finally
        {
            job.CompletedTime = DateTime.Now;
            _concurrency.Release();
        }
    }

    /// <summary>构建前执行 git pull --ff-only 拉取远端最新代码；失败则抛异常中止构建（避免打包旧代码）。</summary>
    private async Task RunGitPullAsync(UniversalBuildJob job)
    {
        AppendLog(job, ">> 拉取远端最新代码：git pull --ff-only");
        var psi = new ProcessStartInfo("git")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = job.ProjectDir,
        };
        // 中文文件名正常显示；凭证缺失时快速失败，绝不挂起等待终端输入
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("core.quotepath=false");
        psi.ArgumentList.Add("pull");
        psi.ArgumentList.Add("--ff-only");
        psi.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("无法启动 git，请确认已安装并加入 PATH");
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) AppendLog(job, e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) AppendLog(job, e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        // 不响应取消令牌：GIT_TERMINAL_PROMPT=0 保证不会挂起等输入，取消会在随后的构建命令生效
        await process.WaitForExitAsync(CancellationToken.None);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git pull 失败（退出码 {process.ExitCode}），已中止构建，请处理本地提交/冲突后重试");
        AppendLog(job, ">> 拉取完成");
        AppendLog(job, string.Empty);
    }

    private static (string fileName, string arguments, string workingDir) GetCommandInfo(UniversalBuildJob job)
    {
        var workingDir = job.ProjectDir;
        var output = job.OutputDir;

        return job.Type switch
        {
            UniversalBuildType.Web or UniversalBuildType.Node =>
                BuildWebCommand(job),

            UniversalBuildType.DotNet =>
                ("dotnet", $"publish -c Release -o \"{output}\" --self-contained false", workingDir),

            UniversalBuildType.JavaMaven =>
                ("mvn", $"clean package -DskipTests -f \"{FindProjectFile(workingDir, "pom.xml")}\"", workingDir),

            UniversalBuildType.JavaGradle =>
                ("gradle", $"clean build -x test -b \"{FindProjectFile(workingDir, "build.gradle")}\"", workingDir),

            UniversalBuildType.Installer =>
                ("iscc", $"\"{FindProjectFile(workingDir, "*.iss")}\" /O\"{output}\"", workingDir),

            _ => throw new NotSupportedException($"不支持的构建类型：{job.Type}"),
        };
    }

    /// <summary>Web/Node 构建命令：vite 项目把产物直接输出到配置目录（-- 由 npm 透传给 vite）；
    /// 其它构建工具（webpack/vue-cli 等）不认识 --outDir 会报错，改为构建后由 CollectArtifacts 从默认产物目录归集。</summary>
    private static (string fileName, string arguments, string workingDir) BuildWebCommand(UniversalBuildJob job)
    {
        var isVite = new[] { "vite.config.ts", "vite.config.js", "vite.config.mts", "vite.config.mjs" }
            .Any(f => File.Exists(Path.Combine(job.ProjectDir, f)));
        var outDirArgs = isVite
            ? $" -- --outDir \"{job.OutputDir}\" --emptyOutDir"
            : string.Empty;
        return ("cmd", $"/c chcp 65001 >nul & npm install && npm run build{outDirArgs}", job.ProjectDir);
    }

    /// <summary>
    /// 构建成功后把产物归集到输出目录：Web/Node 与 Java 的构建命令不接收输出目录参数，
    /// 产物默认落在项目内的 dist/target 等位置，这里探测后复制过去。
    /// 输出目录已有产物（DotNet/Installer 直接输出，或 vite --outDir 直接输出）时跳过。
    /// </summary>
    private static void CollectArtifacts(UniversalBuildJob job)
    {
        try
        {
            if (Directory.Exists(job.OutputDir) && Directory.EnumerateFileSystemEntries(job.OutputDir).Any())
                return;

            if (job.Type is UniversalBuildType.JavaMaven or UniversalBuildType.JavaGradle)
            {
                // Java：只收集构建产物 jar/war（target 里还有 classes 等中间目录，不能整目录复制）；
                // 多模块工程每个模块的产物都收集，同名时保留修改时间更新的
                foreach (var dir in GetArtifactCandidates(job).Where(d => Directory.Exists(d) && !IsPathOverlapping(d, job.OutputDir)))
                {
                    foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly))
                    {
                        var ext = Path.GetExtension(file).ToLowerInvariant();
                        if (ext != ".jar" && ext != ".war") continue;
                        var dest = Path.Combine(job.OutputDir, Path.GetFileName(file));
                        if (File.Exists(dest) && File.GetLastWriteTimeUtc(dest) >= File.GetLastWriteTimeUtc(file)) continue;
                        File.Copy(file, dest, true);
                    }
                }
                AppendLog(job, Directory.EnumerateFiles(job.OutputDir).Any()
                    ? $">> 产物（jar/war）已复制到输出目录：{job.OutputDir}"
                    : ">> 未在项目中探测到构建产物（jar/war），请检查项目结构");
                return;
            }

            if (job.Type is not (UniversalBuildType.Web or UniversalBuildType.Node)) return;

            // Web/Node：取修改时间最新的产物目录，复制全部内容（排除与输出目录重叠的候选，避免自我复制）
            var latest = GetArtifactCandidates(job)
                .Where(d => Directory.Exists(d) && !IsPathOverlapping(d, job.OutputDir)
                    && Directory.EnumerateFileSystemEntries(d).Any())
                .OrderByDescending(d => new DirectoryInfo(d).LastWriteTimeUtc)
                .FirstOrDefault();
            if (latest == null)
            {
                AppendLog(job, ">> 未在项目中探测到构建产物目录（dist/build/out 等），请检查构建配置");
                return;
            }
            CopyDirectory(latest, job.OutputDir);
            AppendLog(job, $">> 产物已从 {latest} 复制到输出目录");
        }
        catch (Exception ex)
        {
            AppendLog(job, $">> 产物归集失败：{ex.Message}");
        }
    }

    /// <summary>按构建类型枚举项目内的常见产物目录（项目目录与其直接子目录，兼容多模块工程；排除依赖/缓存目录）。</summary>
    private static IEnumerable<string> GetArtifactCandidates(UniversalBuildJob job)
    {
        var roots = new List<string> { job.ProjectDir };
        try
        {
            roots.AddRange(Directory.GetDirectories(job.ProjectDir)
                .Where(d => !IsExcludedDir(d)));
        }
        catch { /* 目录不可访问时仅探测项目根 */ }

        foreach (var root in roots)
        {
            switch (job.Type)
            {
                case UniversalBuildType.Web:
                case UniversalBuildType.Node:
                    yield return Path.Combine(root, "dist");
                    yield return Path.Combine(root, "build");
                    yield return Path.Combine(root, "out");
                    yield return Path.Combine(root, ".output");
                    yield return Path.Combine(root, "wwwroot");
                    break;
                case UniversalBuildType.JavaMaven:
                    yield return Path.Combine(root, "target");
                    break;
                case UniversalBuildType.JavaGradle:
                    yield return Path.Combine(root, "build", "libs");
                    break;
            }
        }
    }

    /// <summary>排除依赖与缓存目录（node_modules 里也有大量 dist/build，混入会误判且慢）。</summary>
    private static bool IsExcludedDir(string dir)
    {
        var name = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return name is "node_modules" or ".git" or ".vs" or ".idea" or "bin" or "obj";
    }

    /// <summary>两个路径是否互为祖先关系（输出目录配在产物目录内/外时直接复制会自我嵌套，需排除）。</summary>
    private static bool IsPathOverlapping(string pathA, string pathB)
    {
        var a = Path.GetFullPath(pathA).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        var b = Path.GetFullPath(pathB).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        return a.StartsWith(b, StringComparison.OrdinalIgnoreCase)
            || b.StartsWith(a, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>递归复制目录内容（同名覆盖）。</summary>
    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.EnumerateFiles(sourceDir))
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), true);
        foreach (var dir in Directory.EnumerateDirectories(sourceDir))
            CopyDirectory(dir, Path.Combine(destDir, Path.GetFileName(dir)));
    }

    private static string FindProjectFile(string projectDir, string pattern)
    {
        if (!Directory.Exists(projectDir))
            throw new DirectoryNotFoundException($"目录不存在：{projectDir}");

        // 当前目录
        var file = Directory.GetFiles(projectDir, pattern, SearchOption.TopDirectoryOnly).FirstOrDefault();
        if (!string.IsNullOrEmpty(file)) return file;

        // 直接子目录
        file = Directory.GetDirectories(projectDir)
            .SelectMany(d => SafeGetFiles(d, pattern))
            .FirstOrDefault();
        if (!string.IsNullOrEmpty(file)) return file;

        throw new FileNotFoundException($"未找到 {pattern}，请确认项目目录正确", pattern);
    }

    private static IEnumerable<string> SafeGetFiles(string dir, string pattern)
    {
        try
        {
            return Directory.GetFiles(dir, pattern, SearchOption.TopDirectoryOnly);
        }
        catch
        {
            return Enumerable.Empty<string>();
        }
    }

    private static void UpdateProgress(UniversalBuildJob job, string line)
    {
        var lower = line.ToLowerInvariant();
        var progress = job.Progress;

        switch (job.Type)
        {
            case UniversalBuildType.Web:
            case UniversalBuildType.Node:
                if (lower.Contains("idealTree") || lower.Contains("resolve")) progress = Math.Max(progress, 10);
                else if (lower.Contains("reify") || lower.Contains("fetch")) progress = Math.Max(progress, 30);
                else if (lower.Contains("build") || lower.Contains("transforming")) progress = Math.Max(progress, 50);
                else if (lower.Contains("rendering chunks") || lower.Contains("writing")) progress = Math.Max(progress, 80);
                break;

            case UniversalBuildType.DotNet:
                if (lower.Contains("restore")) progress = Math.Max(progress, 20);
                else if (lower.Contains("build")) progress = Math.Max(progress, 50);
                else if (lower.Contains("publish")) progress = Math.Max(progress, 80);
                break;

            case UniversalBuildType.JavaMaven:
                if (lower.Contains("downloading")) progress = Math.Max(progress, 20);
                else if (lower.Contains("compiling")) progress = Math.Max(progress, 50);
                else if (lower.Contains("building")) progress = Math.Max(progress, 80);
                break;

            case UniversalBuildType.JavaGradle:
                if (lower.Contains("resolve")) progress = Math.Max(progress, 20);
                else if (lower.Contains("compile")) progress = Math.Max(progress, 50);
                else if (lower.Contains("build")) progress = Math.Max(progress, 80);
                break;

            case UniversalBuildType.Installer:
                if (lower.Contains("compiling")) progress = Math.Max(progress, 50);
                else if (lower.Contains("compressing")) progress = Math.Max(progress, 80);
                break;
        }

        // 命中更高阶段锚点：记录值与时间，供 ToDto 按耗时平滑插值到下一锚点
        if (progress > job.ProgressAnchor)
        {
            job.ProgressAnchor = progress;
            job.AnchorTime = DateTime.Now;
        }
        job.Progress = progress;
    }

    private static void AppendLog(UniversalBuildJob job, string line)
    {
        lock (job.Log)
        {
            job.Log.AppendLine(line);
        }
    }

    // ============================ 产物占用统计与清理 ============================

    /// <summary>统计产物目录占用（大小/文件数/最后修改时间）。</summary>
    public IReadOnlyList<ArtifactUsageItem> GetArtifactUsage(IReadOnlyList<string> dirs)
    {
        var list = new List<ArtifactUsageItem>();
        foreach (var dir in dirs.Where(d => !string.IsNullOrWhiteSpace(d)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var item = new ArtifactUsageItem { Path = dir };
            try
            {
                if (!Directory.Exists(dir)) { list.Add(item); continue; }
                item.Exists = true;
                var files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).ToList();
                item.FileCount = files.Count;
                item.SizeBytes = files.Sum(f => new FileInfo(f).Length);
                item.LastWriteTime = files.Count > 0
                    ? files.Max(f => new FileInfo(f).LastWriteTime)
                    : Directory.GetLastWriteTime(dir);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "统计产物占用失败: {Dir}", dir);
            }
            list.Add(item);
        }
        return list;
    }

    /// <summary>清空产物目录内容（保留目录本身，删除不可恢复）。</summary>
    public void CleanArtifact(string dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) throw new ArgumentException("目录不能为空");
        if (!Directory.Exists(dir)) throw new InvalidOperationException($"目录不存在: {dir}");
        foreach (var entry in Directory.EnumerateFileSystemEntries(dir))
        {
            if (Directory.Exists(entry))
                Directory.Delete(entry, true);
            else
                File.Delete(entry);
        }
    }

    /// <summary>获取默认输出目录。</summary>
    public static string GetDefaultOutputDir(UniversalBuildType type, string name)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var safeName = string.IsNullOrWhiteSpace(name) ? "project" : name;
        foreach (var c in Path.GetInvalidFileNameChars())
            safeName = safeName.Replace(c, '_');
        var typeDir = type.ToString().ToLowerInvariant();
        return Path.Combine(desktop, "UniversalBuild", "publish", typeDir, safeName);
    }

    /// <summary>各构建类型的进度阶段锚点与典型耗时（秒）：日志命中锚点后按耗时向下一锚点平滑插值。</summary>
    private static readonly Dictionary<UniversalBuildType, (int Anchor, int Seconds)[]> StageTimelines = new()
    {
        [UniversalBuildType.Web] = new[] { (10, 90), (30, 150), (50, 120), (80, 60) },
        [UniversalBuildType.Node] = new[] { (10, 90), (30, 150), (50, 120), (80, 60) },
        [UniversalBuildType.DotNet] = new[] { (20, 40), (50, 150), (80, 60) },
        [UniversalBuildType.JavaMaven] = new[] { (20, 90), (50, 150), (80, 60) },
        [UniversalBuildType.JavaGradle] = new[] { (20, 60), (50, 150), (80, 60) },
        [UniversalBuildType.Installer] = new[] { (50, 150), (80, 60) },
    };

    /// <summary>
    /// 展示进度：Running 时在当前锚点与下一锚点间按耗时平滑推进（封顶下一锚点前 2，避免关键字未出现就越界），
    /// 其余状态返回实际值。解决原阶段式进度在长耗时步骤（npm install 等）期间长时间停滞的问题。
    /// </summary>
    private static int ComputeDisplayProgress(UniversalBuildJob job)
    {
        if (job.Status != UniversalBuildStatus.Running) return job.Progress;
        if (!StageTimelines.TryGetValue(job.Type, out var timeline) || timeline.Length == 0) return job.Progress;

        var anchor = job.ProgressAnchor;
        var since = job.AnchorTime == default ? job.StartTime : job.AnchorTime;
        var elapsed = Math.Max(0, (DateTime.Now - since).TotalSeconds);

        int from;
        int to;
        double seconds;
        if (anchor < timeline[0].Anchor)
        {
            // 尚未命中任何阶段关键字：从 0 向首锚点缓慢爬升（预留 1.5 倍首段时长）
            from = 0;
            to = timeline[0].Anchor;
            seconds = timeline[0].Seconds * 1.5;
        }
        else
        {
            var idx = 0;
            while (idx + 1 < timeline.Length && anchor >= timeline[idx + 1].Anchor) idx++;
            from = timeline[idx].Anchor;
            to = idx + 1 < timeline.Length ? timeline[idx + 1].Anchor : 100;
            seconds = timeline[idx].Seconds;
        }

        // 最多推进到下一锚点前 2，等真实关键字出现再跳锚
        var cap = to - 2 > from ? to - 2 : to;
        var ratio = Math.Min(1.0, elapsed / seconds);
        var value = from + (to - from) * ratio;
        return (int)Math.Clamp(value, from, cap);
    }

    private static UniversalBuildJobDto ToDto(UniversalBuildJob job)
    {
        string logText;
        lock (job.Log)
        {
            logText = job.Log.ToString();
        }

        return new UniversalBuildJobDto
        {
            Id = job.Id,
            Type = job.Type,
            Name = job.Name,
            Status = job.Status,
            ProjectDir = job.ProjectDir,
            OutputDir = job.OutputDir,
            Progress = ComputeDisplayProgress(job),
            Log = logText,
            ExitCode = job.ExitCode,
            ArtifactSize = job.ArtifactSize,
            StartTime = job.StartTime,
            CompletedTime = job.CompletedTime,
        };
    }

    /// <summary>字节数 → 可读大小（与 DeployService 同风格）。</summary>
    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / 1024.0 / 1024.0:F1} MB";
    }
}

using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using FreeSql;
using Microsoft.Extensions.Logging;

namespace ConvenientSystem.Desktop;

/// <summary>运行中的流水线实例：运行记录 + 汇总日志（日志只在内存，重启后不可查，与部署历史行为一致）。</summary>
internal sealed class RunningPipeline
{
    public PipelineRunRecord Record { get; } = new();
    public StringBuilder Log { get; } = new();
    public CancellationTokenSource Cts { get; } = new();
    /// <summary>当前阶段正在执行的子任务 id（构建/部署），取消时联动取消。</summary>
    public string? CurrentJobId { get; set; }
    public bool CurrentJobIsDeploy { get; set; }
}

/// <summary>流水线运行结果 DTO（含汇总日志）。</summary>
public sealed class PipelineRunDto : PipelineRunRecord
{
    public string Log { get; set; } = string.Empty;
}

/// <summary>
/// 流水线执行引擎：按定义中的阶段顺序执行“构建 → 部署”，
/// 复用 UniversalBuildService（含排队/取消）与 DeployService（7 步部署/取消/回滚），
/// 部署 SSH 密码运行时从 SshCredentialStore（DPAPI 本机加密）读取，不落流水线配置。
/// 任一阶段失败或取消，后续阶段标记 Skipped，流水线终止。
/// </summary>
public sealed class PipelineService
{
    /// <summary>内存保留的已完成运行条数上限（超出移除最旧，日志随内存记录一起丢失）。</summary>
    private const int MaxCompletedInMemory = 20;

    private readonly UniversalBuildService _buildService;
    private readonly DeployService _deployService;
    private readonly SshCredentialStore _credentialStore;
    private readonly PipelineStore _store;
    private readonly ILogger<PipelineService> _logger;

    private readonly ConcurrentDictionary<string, RunningPipeline> _runs = new();

    public PipelineService(
        UniversalBuildService buildService,
        DeployService deployService,
        SshCredentialStore credentialStore,
        PipelineStore store,
        ILogger<PipelineService> logger)
    {
        _buildService = buildService;
        _deployService = deployService;
        _credentialStore = credentialStore;
        _store = store;
        _logger = logger;
    }

    // ============================ 运行控制 ============================

    /// <summary>启动一次流水线运行（同一流水线运行中不允许重复启动）。</summary>
    public PipelineRunDto StartRun(string pipelineId)
    {
        var definition = _store.Get(pipelineId)
            ?? throw new InvalidOperationException("流水线不存在（可能已被删除），请刷新列表");
        if (definition.Stages.Count == 0)
            throw new InvalidOperationException("流水线没有配置任何阶段，请先编辑阶段");

        if (_runs.Values.Any(r => r.Record.PipelineId == pipelineId && r.Record.Status == PipelineRunStatus.Running))
            throw new InvalidOperationException("该流水线正在运行中，请等待完成后再启动");

        var run = new RunningPipeline();
        run.Record.Id = Guid.NewGuid().ToString("N")[..12];
        run.Record.PipelineId = definition.Id;
        run.Record.PipelineName = definition.Name;
        run.Record.Status = PipelineRunStatus.Running;
        run.Record.Trigger = "manual";
        run.Record.Stages = definition.Stages.Select(s => new PipelineRunStageRecord
        {
            StageId = s.Id,
            Name = s.Name,
            Status = PipelineStageRunStatus.Pending,
        }).ToList();
        _runs[run.Record.Id] = run;

        _ = Task.Run(() => RunAsync(run, definition));
        return ToDto(run);
    }

    /// <summary>取消运行：中断当前阶段子任务（部署取消会自动还原部署前环境），后续阶段跳过。</summary>
    public bool CancelRun(string runId)
    {
        if (!_runs.TryGetValue(runId, out var run)) return false;
        if (run.Record.Status != PipelineRunStatus.Running) return false;

        run.Cts.Cancel();
        // 联动取消当前阶段的子任务
        if (!string.IsNullOrEmpty(run.CurrentJobId))
        {
            if (run.CurrentJobIsDeploy) _deployService.Cancel(run.CurrentJobId);
            else _buildService.Cancel(run.CurrentJobId);
        }
        AppendLog(run, ">> 收到取消请求，正在中断...");
        return true;
    }

    /// <summary>查询单次运行（内存优先含日志；历史记录重启后无日志）。</summary>
    public PipelineRunDto? GetRun(string runId)
    {
        if (_runs.TryGetValue(runId, out var run)) return ToDto(run);
        var history = _store.GetRuns(null, int.MaxValue).FirstOrDefault(r => r.Id == runId);
        return history == null ? null : new PipelineRunDto
        {
            Id = history.Id,
            PipelineId = history.PipelineId,
            PipelineName = history.PipelineName,
            Status = history.Status,
            StartTime = history.StartTime,
            CompletedTime = history.CompletedTime,
            Stages = history.Stages,
            Trigger = history.Trigger,
            Log = string.Empty,
        };
    }

    /// <summary>查询运行历史（内存 + 持久化合并去重，按开始时间倒序）。</summary>
    public IReadOnlyList<PipelineRunDto> GetRuns(string? pipelineId, int limit)
    {
        var inMemory = _runs.Values
            .Where(r => string.IsNullOrWhiteSpace(pipelineId) || r.Record.PipelineId == pipelineId)
            .Select(ToDto)
            .ToList();
        var inMemoryIds = inMemory.Select(r => r.Id).ToHashSet();
        var persisted = _store.GetRuns(pipelineId, int.MaxValue)
            .Where(r => !inMemoryIds.Contains(r.Id))
            .Select(r => new PipelineRunDto
            {
                Id = r.Id,
                PipelineId = r.PipelineId,
                PipelineName = r.PipelineName,
                Status = r.Status,
                StartTime = r.StartTime,
                CompletedTime = r.CompletedTime,
                Stages = r.Stages,
                Trigger = r.Trigger,
                Log = string.Empty,
            });
        return inMemory.Concat(persisted)
            .OrderByDescending(r => r.StartTime)
            .Take(limit)
            .ToList();
    }

    /// <summary>指定流水线是否正在运行（前端列表状态标记用）。</summary>
    public bool IsRunning(string pipelineId)
        => _runs.Values.Any(r => r.Record.PipelineId == pipelineId && r.Record.Status == PipelineRunStatus.Running);

    // ============================ 执行引擎 ============================

    private async Task RunAsync(RunningPipeline run, PipelineDefinition definition)
    {
        var record = run.Record;
        AppendLog(run, $"════════ 流水线【{definition.Name}】开始（{record.StartTime:HH:mm:ss}）════════");

        var failed = false;
        var cancelled = false;
        string? lastBuildOutputDir = null;
        var ct = run.Cts.Token;

        for (var i = 0; i < definition.Stages.Count; i++)
        {
            var stage = definition.Stages[i];
            var rec = record.Stages[i];
            if (failed || cancelled)
            {
                rec.Status = PipelineStageRunStatus.Skipped;
                rec.Message = cancelled ? "流水线已取消" : "前置阶段失败，已跳过";
                continue;
            }

            rec.Status = PipelineStageRunStatus.Running;
            rec.StartTime = DateTime.Now;
            AppendLog(run, string.Empty);
            AppendLog(run, $"───── 阶段 {i + 1}/{definition.Stages.Count}【{stage.Name}】（{stage.Type}）─────");

            try
            {
                if (stage.Type == PipelineStageType.Build)
                {
                    var dto = await RunBuildStageAsync(run, definition, stage, ct);
                    lastBuildOutputDir = dto.OutputDir;
                    rec.Message = dto.ArtifactSize is { } size
                        ? $"产物 {FormatSize(size)}"
                        : "构建成功";
                    if (stage.PackArtifact && !string.IsNullOrEmpty(dto.ArtifactArchivePath))
                        rec.Message += $"，压缩包 {FormatSize(dto.ArtifactArchiveSize ?? 0)}";
                }
                else if (stage.Type == PipelineStageType.Sql)
                {
                    var (fileCount, batchCount, affected) = await RunSqlStageAsync(run, stage, lastBuildOutputDir, ct);
                    rec.Message = $"{stage.DbType} 执行 {fileCount} 文件/{batchCount} 批次，影响 {affected} 行";
                }
                else
                {
                    await RunDeployStageAsync(run, definition, stage, lastBuildOutputDir, ct);
                    rec.Message = $"部署至 {stage.Host}";
                }
                rec.Status = PipelineStageRunStatus.Success;
                AppendLog(run, $">> ✔ 阶段【{stage.Name}】成功（耗时 {FormatDuration(DateTime.Now - rec.StartTime.Value)}）");
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
                rec.Status = PipelineStageRunStatus.Failed;
                rec.Message = "已取消";
                AppendLog(run, $">> ✖ 阶段【{stage.Name}】已取消");
            }
            catch (Exception ex)
            {
                failed = true;
                rec.Status = PipelineStageRunStatus.Failed;
                rec.Message = ex.Message;
                AppendLog(run, $">> ✖ 阶段【{stage.Name}】失败：{ex.Message}");
                _logger.LogWarning(ex, "流水线阶段失败 Pipeline={Pipeline} Stage={Stage}", definition.Name, stage.Name);
            }
            finally
            {
                rec.CompletedTime = DateTime.Now;
                run.CurrentJobId = null;
            }
        }

        record.Status = cancelled ? PipelineRunStatus.Cancelled : failed ? PipelineRunStatus.Failed : PipelineRunStatus.Success;
        record.CompletedTime = DateTime.Now;
        var duration = record.CompletedTime.Value - record.StartTime;
        AppendLog(run, string.Empty);
        AppendLog(run, $"════════ 流水线【{definition.Name}】结束：{StatusText(record.Status)}（总耗时 {FormatDuration(duration)}）════════");

        _store.AddRun(CloneRecord(record));
        TrimCompletedInMemory();
    }

    /// <summary>执行构建阶段：启动构建任务并轮询到终态，返回终态 DTO（供取产物目录）。</summary>
    private async Task<UniversalBuildJobDto> RunBuildStageAsync(
        RunningPipeline run, PipelineDefinition definition, PipelineStage stage, CancellationToken ct)
    {
        var dto = _buildService.StartBuild(new UniversalBuildRequest
        {
            // 任务名带流水线前缀，与手动构建任务区分开
            Name = $"{definition.Name} · {stage.Name}",
            Type = stage.BuildType,
            ProjectDir = stage.ProjectDir,
            OutputDir = stage.OutputDir,
            PrePull = stage.PrePull,
            PackArtifact = stage.PackArtifact,
        });
        run.CurrentJobId = dto.Id;
        run.CurrentJobIsDeploy = false;

        var lastLogLength = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var current = _buildService.GetProgress(dto.Id)
                ?? throw new InvalidOperationException("构建任务丢失（服务内部错误）");
            AppendLogDelta(run, current.Log, ref lastLogLength);

            if (current.Status is UniversalBuildStatus.Success or UniversalBuildStatus.Failed or UniversalBuildStatus.Cancelled)
            {
                if (current.Status != UniversalBuildStatus.Success)
                {
                    var reason = current.Status == UniversalBuildStatus.Cancelled ? "被取消" : $"退出码 {current.ExitCode}";
                    throw new OperationCanceledException($"构建失败（{reason}），详见日志");
                }
                return current;
            }
            await Task.Delay(1000, ct);
        }
    }

    /// <summary>执行部署阶段：组装 DeployRequest 并轮询到终态。</summary>
    private async Task RunDeployStageAsync(
        RunningPipeline run, PipelineDefinition definition, PipelineStage stage,
        string? lastBuildOutputDir, CancellationToken ct)
    {
        var outputDir = !string.IsNullOrWhiteSpace(stage.ExplicitOutputDir)
            ? stage.ExplicitOutputDir.Trim()
            : lastBuildOutputDir;
        if (string.IsNullOrWhiteSpace(outputDir))
            throw new InvalidOperationException("未指定产物目录：流水线前面没有构建阶段，请在阶段配置中显式填写部署目录");

        if (string.IsNullOrWhiteSpace(stage.Host) || string.IsNullOrWhiteSpace(stage.UserName))
            throw new InvalidOperationException("部署阶段未配置服务器地址或用户名");

        // 密码不落流水线配置：运行时从本机 DPAPI 凭据库读取
        var password = _credentialStore.Get(stage.Host.Trim(), stage.UserName.Trim());
        if (string.IsNullOrEmpty(password))
            throw new InvalidOperationException($"本机未保存 {stage.UserName}@{stage.Host} 的 SSH 密码（先在通用构建页部署一次并勾选“记住密码”）");

        var jobId = _deployService.StartDeploy(new DeployRequest
        {
            OutputDir = outputDir,
            BuildName = $"{definition.Name} · {stage.Name}",
            BuildType = stage.DeployBuildType,
            ServiceName = stage.ServiceName,
            RemoteDir = stage.RemoteDir,
            ArchiveName = stage.ArchiveName,
            TargetOS = stage.TargetOS,
            SiteName = string.IsNullOrWhiteSpace(stage.SiteName) ? "convenient" : stage.SiteName,
            Host = stage.Host.Trim(),
            UserName = stage.UserName.Trim(),
            Password = password,
            DeployPath = stage.DeployPath,
            VerifyHealth = stage.VerifyHealth,
            KeepDatabase = stage.KeepDatabase,
        });
        run.CurrentJobId = jobId;
        run.CurrentJobIsDeploy = true;

        var lastLogLength = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var job = _deployService.GetJob(jobId)
                ?? throw new InvalidOperationException("部署任务丢失（服务内部错误）");
            AppendLogDelta(run, job.Log, ref lastLogLength);

            if (job.Status is DeployStatus.Success or DeployStatus.Failed or DeployStatus.Cancelled)
            {
                if (job.Status != DeployStatus.Success)
                {
                    var reason = job.Status == DeployStatus.Cancelled ? "已取消并还原部署前环境" : "部署失败";
                    throw new OperationCanceledException($"部署未成功：{reason}，详见日志");
                }
                return;
            }
            await Task.Delay(1000, ct);
        }
    }

    /// <summary>
    /// 执行数据库脚本阶段：SqlSource 为单文件或目录（空 = 上一个构建阶段的产物目录），
    /// 目录时执行其中全部 .sql（按文件名排序）；SQL Server 脚本按 GO 行切批次，
    /// 其他库整文件一批。临时 FreeSql 实例用完即释放，不动 DI。
    /// 返回（文件数，批次数，总影响行数）。
    /// </summary>
    private async Task<(int FileCount, int BatchCount, long Affected)> RunSqlStageAsync(
        RunningPipeline run, PipelineStage stage, string? lastBuildOutputDir, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(stage.ConnectionString))
            throw new InvalidOperationException("数据库阶段未配置连接串");
        if (!Enum.TryParse<DataType>(stage.DbType, true, out var dbType))
            throw new InvalidOperationException($"不支持的数据库类型：{stage.DbType}");

        var source = !string.IsNullOrWhiteSpace(stage.SqlSource)
            ? stage.SqlSource.Trim()
            : lastBuildOutputDir;
        if (string.IsNullOrWhiteSpace(source))
            throw new InvalidOperationException("未指定 SQL 文件/目录：流水线前面没有构建阶段，请在阶段配置中显式填写");

        // 收集 .sql 文件：单文件直接用；目录按文件名排序全执行（不含子目录，避免误执行）
        List<string> files;
        if (Directory.Exists(source))
        {
            files = Directory.EnumerateFiles(source, "*.sql", SearchOption.TopDirectoryOnly)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (files.Count == 0)
                throw new InvalidOperationException($"目录中没有 .sql 文件：{source}");
            AppendLog(run, $">> SQL 目录：{source}（{files.Count} 个文件）");
        }
        else if (File.Exists(source))
        {
            files = new List<string> { source };
            AppendLog(run, $">> SQL 文件：{source}");
        }
        else
        {
            throw new InvalidOperationException($"SQL 文件/目录不存在：{source}");
        }

        AppendLog(run, $">> 目标数据库：{dbType}");
        using var fsql = new FreeSqlBuilder()
            .UseConnectionString(dbType, stage.ConnectionString.Trim())
            .Build();

        var totalBatches = 0;
        long totalAffected = 0;
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            var fileName = Path.GetFileName(file);
            var content = await File.ReadAllTextAsync(file, ct);
            var batches = SplitSqlBatches(content);
            if (batches.Count == 0)
            {
                AppendLog(run, $">> [跳过] {fileName}（空文件）");
                continue;
            }
            AppendLog(run, $">> 执行 {fileName}（{batches.Count} 批次）...");

            var fileAffected = await ExecuteBatchesAsync(fsql, batches, stage.UseTransaction, ct);

            totalBatches += batches.Count;
            totalAffected += fileAffected;
            AppendLog(run, $">> ✔ {fileName} 完成，影响 {fileAffected} 行");
        }
        return (files.Count, totalBatches, totalAffected);
    }

    /// <summary>
    /// 逐批执行 SQL：原生 DbCommand（SqlExecuteService 同模式），返回总影响行数；
    /// 事务模式下任一批失败回滚整个文件，异常上抛（阶段失败）。
    /// </summary>
    private static async Task<long> ExecuteBatchesAsync(IFreeSql fsql, List<string> batches, bool useTransaction, CancellationToken ct)
    {
        long affected = 0;
        // FreeSql 池化连接（取到即已打开，using 归还连接池）
        using var pooled = await fsql.Ado.MasterPool.GetAsync();
        var conn = pooled.Value;
        await using var tx = useTransaction ? await conn.BeginTransactionAsync(ct) : null;
        try
        {
            for (var i = 0; i < batches.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = batches[i];
                if (tx != null) cmd.Transaction = tx;
                cmd.CommandTimeout = 0; // 脚本可能含大数据量操作，不设超时
                affected += await cmd.ExecuteNonQueryAsync(ct);
            }
            if (tx != null) await tx.CommitAsync(ct);
        }
        catch
        {
            if (tx != null)
            {
                try { await tx.RollbackAsync(CancellationToken.None); }
                catch { /* 回滚失败已无法挽回，上抛原始异常 */ }
            }
            throw;
        }
        return affected;
    }

    /// <summary>
    /// 按行首 GO（忽略大小写、允许前后空白）切分 SQL 批次，SQL Server 脚本惯例；
    /// MySQL/PostgreSQL 等脚本不含独立 GO 行，自然整体为一批。
    /// </summary>
    private static List<string> SplitSqlBatches(string content)
    {
        var batches = new List<string>();
        var current = new StringBuilder();
        foreach (var line in content.Split('\n'))
        {
            if (Regex.IsMatch(line, @"^\s*GO\s*$", RegexOptions.IgnoreCase))
            {
                var batch = current.ToString().Trim();
                if (batch.Length > 0) batches.Add(batch);
                current.Clear();
            }
            else
            {
                current.AppendLine(line.TrimEnd('\r'));
            }
        }
        var tail = current.ToString().Trim();
        if (tail.Length > 0) batches.Add(tail);
        return batches;
    }

    // ============================ 辅助 ============================

    private static void AppendLog(RunningPipeline run, string line)
    {
        lock (run.Log)
        {
            run.Log.AppendLine(line);
        }
    }

    /// <summary>把子任务日志的增量部分追加到汇总日志（轮询期间每次只补新增行）。</summary>
    private static void AppendLogDelta(RunningPipeline run, string? log, ref int lastLength)
    {
        if (string.IsNullOrEmpty(log) || log.Length <= lastLength) return;
        lock (run.Log)
        {
            run.Log.Append(log.AsSpan(lastLength));
            // 子任务日志末尾通常自带换行，缺失时补一个避免与下一段连行
            if (!log.EndsWith('\n')) run.Log.AppendLine();
        }
        lastLength = log.Length;
    }

    private static PipelineRunDto ToDto(RunningPipeline run)
    {
        string log;
        lock (run.Log) log = run.Log.ToString();
        var record = run.Record;
        return new PipelineRunDto
        {
            Id = record.Id,
            PipelineId = record.PipelineId,
            PipelineName = record.PipelineName,
            Status = record.Status,
            StartTime = record.StartTime,
            CompletedTime = record.CompletedTime,
            Stages = record.Stages,
            Trigger = record.Trigger,
            Log = log,
        };
    }

    /// <summary>克隆终态记录（历史文件不存日志，Stages 深拷贝避免与内存对象共享引用）。</summary>
    private static PipelineRunRecord CloneRecord(PipelineRunRecord record)
        => new()
        {
            Id = record.Id,
            PipelineId = record.PipelineId,
            PipelineName = record.PipelineName,
            Status = record.Status,
            StartTime = record.StartTime,
            CompletedTime = record.CompletedTime,
            Trigger = record.Trigger,
            Stages = record.Stages.Select(s => new PipelineRunStageRecord
            {
                StageId = s.StageId,
                Name = s.Name,
                Status = s.Status,
                JobId = s.JobId,
                StartTime = s.StartTime,
                CompletedTime = s.CompletedTime,
                Message = s.Message,
            }).ToList(),
        };

    /// <summary>内存中已完成运行超限时移除最旧的（日志随内存记录丢失，历史文件仍保留摘要）。</summary>
    private void TrimCompletedInMemory()
    {
        var completed = _runs.Values
            .Where(r => r.Record.Status != PipelineRunStatus.Running)
            .OrderBy(r => r.Record.StartTime)
            .ToList();
        for (var i = 0; i < completed.Count - MaxCompletedInMemory; i++)
            _runs.TryRemove(completed[i].Record.Id, out _);
    }

    private static string StatusText(PipelineRunStatus status) => status switch
    {
        PipelineRunStatus.Success => "成功",
        PipelineRunStatus.Failed => "失败",
        PipelineRunStatus.Cancelled => "已取消",
        _ => status.ToString(),
    };

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.##} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.##} MB",
        >= 1L << 10 => $"{bytes / (double)(1L << 10):0.##} KB",
        _ => $"{bytes} B",
    };

    private static string FormatDuration(TimeSpan span) => span.TotalMinutes >= 1
        ? $"{(int)span.TotalMinutes}m{span.Seconds:D2}s"
        : $"{span.TotalSeconds:0.#}s";
}

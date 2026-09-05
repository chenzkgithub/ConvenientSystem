using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace ConvenientSystem.Desktop;

// ============================ 流水线数据模型 ============================

/// <summary>流水线阶段类型：构建（含可选拉取/打包）/ 部署 / 数据库脚本。</summary>
public enum PipelineStageType
{
    Build,
    Deploy,
    Sql,
}

/// <summary>
/// 流水线阶段配置。Build 与 Deploy 的字段扁平共存（未用到的字段保持默认值），
/// 前端按 Type 渲染对应表单，持久化 JSON 保持简单。
/// </summary>
public sealed class PipelineStage
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = string.Empty;

    public PipelineStageType Type { get; set; }

    // —— 构建阶段配置 ——
    public UniversalBuildType BuildType { get; set; }
    public string ProjectDir { get; set; } = string.Empty;
    /// <summary>输出目录；空 = 按构建类型与阶段名自动推断。</summary>
    public string? OutputDir { get; set; }
    /// <summary>构建前先 git pull --ff-only 拉取远端最新代码。</summary>
    public bool PrePull { get; set; }
    /// <summary>构建成功后把产物目录打包成 zip。</summary>
    public bool PackArtifact { get; set; }

    // —— 部署阶段配置 ——
    /// <summary>部署产物目录；空 = 使用流水线中上一个构建阶段的产物目录。</summary>
    public string ExplicitOutputDir { get; set; } = string.Empty;
    /// <summary>部署关联的构建类型（用于默认服务名/远程目录推断，不影响部署逻辑）。</summary>
    public UniversalBuildType DeployBuildType { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public string RemoteDir { get; set; } = string.Empty;
    public string ArchiveName { get; set; } = string.Empty;
    public DeployTargetOS TargetOS { get; set; } = DeployTargetOS.Linux;
    public string SiteName { get; set; } = "convenient";
    public string Host { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string DeployPath { get; set; } = string.Empty;
    public bool VerifyHealth { get; set; } = true;
    public bool KeepDatabase { get; set; } = true;

    // —— 数据库脚本阶段配置 ——
    /// <summary>SQL 文件或目录；空 = 流水线上一个构建阶段的产物目录（执行其中全部 .sql，按文件名排序）。</summary>
    public string SqlSource { get; set; } = string.Empty;
    /// <summary>目标数据库类型（FreeSql.DataType 字符串：SqlServer/MySql/PostgreSQL/Sqlite/Oracle）。</summary>
    public string DbType { get; set; } = "SqlServer";
    /// <summary>目标库连接串（明文存本机 pipelines.json，与 appsettings.json 同级安全级别）。</summary>
    public string ConnectionString { get; set; } = string.Empty;
    /// <summary>是否用事务包裹每个文件的执行（脚本含 CREATE PROCEDURE/BACKUP 等不能进事务的语句时需关闭）。</summary>
    public bool UseTransaction { get; set; }
}

/// <summary>流水线定义（阶段序列）。</summary>
public sealed class PipelineDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string Name { get; set; } = string.Empty;
    public DateTime CreateTime { get; set; } = DateTime.Now;
    public DateTime UpdateTime { get; set; } = DateTime.Now;
    public List<PipelineStage> Stages { get; set; } = new();
}

/// <summary>流水线整体运行状态。</summary>
public enum PipelineRunStatus
{
    Running,
    Success,
    Failed,
    Cancelled,
}

/// <summary>单阶段运行状态（Skipped = 前置阶段失败/取消未执行）。</summary>
public enum PipelineStageRunStatus
{
    Pending,
    Running,
    Success,
    Failed,
    Skipped,
}

/// <summary>单阶段运行结果。</summary>
public sealed class PipelineRunStageRecord
{
    public string StageId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public PipelineStageRunStatus Status { get; set; } = PipelineStageRunStatus.Pending;
    /// <summary>关联的构建/部署任务 id（前端跳详情日志用）。</summary>
    public string? JobId { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? CompletedTime { get; set; }
    /// <summary>失败原因或结果摘要（如“产物 123MB”“部署至 1.2.3.4”）。</summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>一次流水线运行。历史记录不持久化 Log（构建/部署日志可能很大，重启后不可查）。非密封：PipelineRunDto 继承并追加日志字段。</summary>
public class PipelineRunRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string PipelineId { get; set; } = string.Empty;
    public string PipelineName { get; set; } = string.Empty;
    public PipelineRunStatus Status { get; set; } = PipelineRunStatus.Running;
    public DateTime StartTime { get; set; } = DateTime.Now;
    public DateTime? CompletedTime { get; set; }
    public List<PipelineRunStageRecord> Stages { get; set; } = new();
    public string Trigger { get; set; } = "manual";
}

// ============================ 持久化存储 ============================

/// <summary>
/// 流水线定义与运行历史本地持久化：
/// - 定义落盘 exe 同目录 pipelines.json（缩进格式，人类可读可备份）；
/// - 运行历史落盘 pipeline-history.json（最近 50 条，只存阶段摘要不存日志）；
/// - 卸载不清理（installer.iss 的 UninstallDelete 只删 wwwroot/logs），覆盖安装/重装不丢。
/// </summary>
public sealed class PipelineStore
{
    private sealed class DefinitionsFile
    {
        public List<PipelineDefinition> Pipelines { get; set; } = new();
    }

    private sealed class RunsFile
    {
        public List<PipelineRunRecord> Runs { get; set; } = new();
    }

    private const int MaxRunHistory = 50;

    private static string DefinitionsFilePath => Path.Combine(AppContext.BaseDirectory, "pipelines.json");
    private static string RunsFilePath => Path.Combine(AppContext.BaseDirectory, "pipeline-history.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ILogger<PipelineStore> _logger;
    private readonly object _definitionsLock = new();
    private readonly object _runsLock = new();
    private List<PipelineDefinition> _definitions = new();
    private List<PipelineRunRecord> _runs = new();

    public PipelineStore(ILogger<PipelineStore> logger)
    {
        _logger = logger;
        LoadDefinitions();
        LoadRuns();
    }

    private void LoadDefinitions()
    {
        try
        {
            if (!File.Exists(DefinitionsFilePath)) return;
            var file = JsonSerializer.Deserialize<DefinitionsFile>(File.ReadAllText(DefinitionsFilePath), JsonOptions);
            if (file?.Pipelines != null) _definitions = file.Pipelines;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "加载 pipelines.json 失败（文件损坏或无权限），将以空定义启动");
        }
    }

    private void PersistDefinitions()
    {
        lock (_definitionsLock)
        {
            try
            {
                var json = JsonSerializer.Serialize(new DefinitionsFile { Pipelines = _definitions }, JsonOptions);
                File.WriteAllText(DefinitionsFilePath, json);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "写入 pipelines.json 失败（磁盘满或无权限）");
            }
        }
    }

    private void LoadRuns()
    {
        try
        {
            if (!File.Exists(RunsFilePath)) return;
            var file = JsonSerializer.Deserialize<RunsFile>(File.ReadAllText(RunsFilePath), JsonOptions);
            if (file?.Runs != null) _runs = file.Runs;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "加载 pipeline-history.json 失败（文件损坏或无权限），将以空历史启动");
        }
    }

    private void PersistRuns()
    {
        lock (_runsLock)
        {
            try
            {
                var json = JsonSerializer.Serialize(new RunsFile { Runs = _runs }, JsonOptions);
                File.WriteAllText(RunsFilePath, json);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "写入 pipeline-history.json 失败（磁盘满或无权限）");
            }
        }
    }

    // ============================ 定义读写 ============================

    public IReadOnlyList<PipelineDefinition> GetAll()
    {
        lock (_definitionsLock) return _definitions.ToList();
    }

    public PipelineDefinition? Get(string id)
    {
        lock (_definitionsLock) return _definitions.FirstOrDefault(p => p.Id == id);
    }

    /// <summary>新增或更新（按 Id 匹配），并落盘。</summary>
    public PipelineDefinition Save(PipelineDefinition definition)
    {
        lock (_definitionsLock)
        {
            var existing = _definitions.FirstOrDefault(p => p.Id == definition.Id);
            if (existing == null)
            {
                definition.CreateTime = DateTime.Now;
                definition.UpdateTime = DateTime.Now;
                _definitions.Add(definition);
            }
            else
            {
                definition.CreateTime = existing.CreateTime;
                definition.UpdateTime = DateTime.Now;
                _definitions.Remove(existing);
                _definitions.Add(definition);
            }
            PersistDefinitions();
            return definition;
        }
    }

    /// <summary>删除定义；不存在返回 false。</summary>
    public bool Remove(string id)
    {
        lock (_definitionsLock)
        {
            var removed = _definitions.RemoveAll(p => p.Id == id) > 0;
            if (removed) PersistDefinitions();
            return removed;
        }
    }

    // ============================ 运行历史读写 ============================

    /// <summary>追加一条已完成/已取消的运行记录（超限截断最旧的），并落盘。</summary>
    public void AddRun(PipelineRunRecord record)
    {
        lock (_runsLock)
        {
            _runs.Insert(0, record);
            if (_runs.Count > MaxRunHistory)
                _runs.RemoveRange(MaxRunHistory, _runs.Count - MaxRunHistory);
            PersistRuns();
        }
    }

    /// <summary>查询运行历史（按开始时间倒序）。pipelineId 为空时查全部。</summary>
    public IReadOnlyList<PipelineRunRecord> GetRuns(string? pipelineId, int limit)
    {
        lock (_runsLock)
        {
            IEnumerable<PipelineRunRecord> query = _runs;
            if (!string.IsNullOrWhiteSpace(pipelineId))
                query = query.Where(r => r.PipelineId == pipelineId);
            return query.OrderByDescending(r => r.StartTime).Take(limit).ToList();
        }
    }
}

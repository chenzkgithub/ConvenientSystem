using System.Text.Json;
using System.Text.Json.Serialization;

namespace ConvenientSystem;

/// <summary>部署/回滚任务持久化记录（摘要，不存日志）。</summary>
public sealed class DeployJobRecord
{
    public string Id { get; set; } = string.Empty;
    public string BuildName { get; set; } = string.Empty;
    public UniversalBuildType BuildType { get; set; }
    public DeployTargetOS TargetOS { get; set; }
    public string SiteName { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public DeployStatus Status { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? CompletedTime { get; set; }
    public int Progress { get; set; }
    public int CurrentStep { get; set; }
    public int TotalSteps { get; set; }
    public string StepTitle { get; set; } = string.Empty;
}

/// <summary>
/// 部署任务本地持久化存储：
/// - 任务启动即落盘，页面刷新后可恢复运行中/历史部署卡片；
/// - 只存摘要不存日志（日志可能很大，重启后不可查）；
/// - 最多保留 100 条，运行中任务优先保留，已完成任务按时间倒序截断。
/// </summary>
public sealed class DeployStore
{
    private const int MaxRecords = 100;
    private static string FilePath => Path.Combine(AppContext.BaseDirectory, "deploy-jobs.json");
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ILogger<DeployStore> _logger;
    private readonly object _lock = new();
    private List<DeployJobRecord> _records = new();

    public DeployStore(ILogger<DeployStore> logger)
    {
        _logger = logger;
        Load();
        RecoverCrashedJobs();
    }

    /// <summary>程序重启后，之前标记为运行中的任务已失去实际进程，改为失败。</summary>
    private void RecoverCrashedJobs()
    {
        var now = DateTime.Now;
        var changed = false;
        foreach (var record in _records.Where(r => r.Status == DeployStatus.Running).ToList())
        {
            record.Status = DeployStatus.Failed;
            record.CompletedTime = now;
            changed = true;
        }
        if (changed) Persist();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var file = JsonSerializer.Deserialize<RecordsFile>(File.ReadAllText(FilePath), JsonOptions);
            if (file?.Records != null) _records = file.Records;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "加载 deploy-jobs.json 失败（文件损坏或无权限），将以空记录启动");
        }
    }

    private void Persist()
    {
        lock (_lock)
        {
            try
            {
                var json = JsonSerializer.Serialize(new RecordsFile { Records = _records }, JsonOptions);
                File.WriteAllText(FilePath, json);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "写入 deploy-jobs.json 失败（磁盘满或无权限）");
            }
        }
    }

    /// <summary>新增或更新记录，并自动裁剪与落盘。</summary>
    public void AddOrUpdate(DeployJobRecord record)
    {
        lock (_lock)
        {
            var idx = _records.FindIndex(r => r.Id == record.Id);
            if (idx >= 0) _records[idx] = record;
            else _records.Insert(0, record);
            Prune();
            Persist();
        }
    }

    private void Prune()
    {
        var running = _records.Where(r => r.Status == DeployStatus.Running).ToList();
        var completed = _records
            .Where(r => r.Status != DeployStatus.Running)
            .OrderByDescending(r => r.CompletedTime ?? r.StartTime)
            .Take(Math.Max(0, MaxRecords - running.Count))
            .ToList();
        _records = running.Concat(completed).OrderByDescending(r => r.StartTime).ToList();
    }

    public DeployJobRecord? Get(string id)
    {
        lock (_lock) return _records.FirstOrDefault(r => r.Id == id);
    }

    public IReadOnlyList<DeployJobRecord> GetAll()
    {
        lock (_lock) return _records.ToList();
    }

    private sealed class RecordsFile
    {
        public List<DeployJobRecord> Records { get; set; } = new();
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;

namespace ConvenientSystem;

/// <summary>通用构建任务持久化记录（摘要，不存日志）。</summary>
public sealed class UniversalBuildJobRecord
{
    public string Id { get; set; } = string.Empty;
    public UniversalBuildType Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public UniversalBuildStatus Status { get; set; }
    public string ProjectDir { get; set; } = string.Empty;
    public string OutputDir { get; set; } = string.Empty;
    public int Progress { get; set; }
    public int? QueuePosition { get; set; }
    public int? ExitCode { get; set; }
    public long? ArtifactSize { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? CompletedTime { get; set; }
}

/// <summary>
/// 通用构建任务本地持久化存储：
/// - 任务启动即落盘，页面刷新后可恢复运行中/历史任务卡片；
/// - 只存摘要不存日志（日志可能很大，重启后不可查）；
/// - 最多保留 100 条，运行中任务优先保留，已完成任务按时间倒序截断。
/// </summary>
public sealed class UniversalBuildStore
{
    private const int MaxRecords = 100;
    private static string FilePath => Path.Combine(AppContext.BaseDirectory, "universal-build-jobs.json");
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ILogger<UniversalBuildStore> _logger;
    private readonly object _lock = new();
    private List<UniversalBuildJobRecord> _records = new();

    public UniversalBuildStore(ILogger<UniversalBuildStore> logger)
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
        foreach (var record in _records
            .Where(r => r.Status is UniversalBuildStatus.Pending or UniversalBuildStatus.Waiting or UniversalBuildStatus.Running)
            .ToList())
        {
            record.Status = UniversalBuildStatus.Failed;
            record.CompletedTime = now;
            record.ExitCode ??= -1;
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
            _logger.LogWarning(ex, "加载 universal-build-jobs.json 失败（文件损坏或无权限），将以空记录启动");
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
                _logger.LogWarning(ex, "写入 universal-build-jobs.json 失败（磁盘满或无权限）");
            }
        }
    }

    /// <summary>新增或更新记录，并自动裁剪与落盘。</summary>
    public void AddOrUpdate(UniversalBuildJobRecord record)
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
        var running = _records
            .Where(r => r.Status is UniversalBuildStatus.Pending or UniversalBuildStatus.Waiting or UniversalBuildStatus.Running)
            .ToList();
        var completed = _records
            .Where(r => r.Status is not (UniversalBuildStatus.Pending or UniversalBuildStatus.Waiting or UniversalBuildStatus.Running))
            .OrderByDescending(r => r.CompletedTime ?? r.StartTime)
            .Take(Math.Max(0, MaxRecords - running.Count))
            .ToList();
        _records = running.Concat(completed).OrderByDescending(r => r.StartTime).ToList();
    }

    public UniversalBuildJobRecord? Get(string id)
    {
        lock (_lock) return _records.FirstOrDefault(r => r.Id == id);
    }

    public IReadOnlyList<UniversalBuildJobRecord> GetAll()
    {
        lock (_lock) return _records.ToList();
    }

    private sealed class RecordsFile
    {
        public List<UniversalBuildJobRecord> Records { get; set; } = new();
    }
}

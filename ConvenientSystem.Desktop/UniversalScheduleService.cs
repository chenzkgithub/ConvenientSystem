using System.Text.Json;
using System.Text.Json.Serialization;

namespace ConvenientSystem.Desktop;

/// <summary>定时构建调度项（JSON 持久化）。</summary>
public sealed class ScheduleItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    /// <summary>关联的前端卡片 id（仅用于界面关联展示）。</summary>
    public string CardId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public UniversalBuildType Type { get; set; }
    public string ProjectDir { get; set; } = string.Empty;
    public string OutputDir { get; set; } = string.Empty;
    /// <summary>触发间隔（分钟）。</summary>
    public int IntervalMinutes { get; set; } = 60;
    public bool Enabled { get; set; } = true;
    /// <summary>上次触发时间（本地时间）。</summary>
    public DateTime? LastRunAt { get; set; }
    /// <summary>下次触发时间（本地时间）。</summary>
    public DateTime NextRunAt { get; set; } = DateTime.Now.AddHours(1);
    /// <summary>上次触发的构建任务号。</summary>
    public string? LastJobId { get; set; }
    /// <summary>上次触发失败原因（成功后清空）。</summary>
    public string? LastError { get; set; }
}

/// <summary>
/// 定时构建调度服务：按配置间隔自动触发通用构建任务。
/// 调度项保存为程序目录下的 universal-schedule.json，程序重启后继续生效。
/// </summary>
public sealed class UniversalScheduleService : IDisposable
{
    private readonly UniversalBuildService _buildService;
    private readonly ILogger<UniversalScheduleService> _logger;
    private readonly object _lock = new();
    private List<ScheduleItem> _items = new();
    // 全限定名：项目全局 using 了 System.Windows.Forms，裸 Timer 会与其控件 Timer 二义
    private readonly System.Threading.Timer _timer;
    private volatile bool _disposed;

    private static string StorePath => Path.Combine(AppContext.BaseDirectory, "universal-schedule.json");
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public UniversalScheduleService(UniversalBuildService buildService, ILogger<UniversalScheduleService> logger)
    {
        _buildService = buildService;
        _logger = logger;
        Load();
        // 每 30 秒巡检一次到期任务（最小间隔 30 分钟，巡检粒度足够）
        _timer = new System.Threading.Timer(_ => CheckAndRun(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    /// <summary>查询全部调度项。</summary>
    public IReadOnlyList<ScheduleItem> GetSchedules()
    {
        lock (_lock) return _items.ToList();
    }

    /// <summary>新增或更新调度项（按 Id 匹配；无 Id 视为新增）。有值入参返回保存后的最新项。</summary>
    public ScheduleItem UpsertSchedule(ScheduleItem item)
    {
        if (string.IsNullOrWhiteSpace(item.ProjectDir))
            throw new ArgumentException("项目目录不能为空");
        if (item.IntervalMinutes < 30)
            throw new ArgumentException("触发间隔不能小于 30 分钟");
        item.Name = string.IsNullOrWhiteSpace(item.Name) ? "未命名任务" : item.Name.Trim();
        item.ProjectDir = item.ProjectDir.Trim();
        item.OutputDir = item.OutputDir?.Trim() ?? string.Empty;

        lock (_lock)
        {
            var existing = string.IsNullOrEmpty(item.Id)
                ? null
                : _items.FirstOrDefault(x => x.Id.Equals(item.Id, StringComparison.OrdinalIgnoreCase));
            if (existing == null && !string.IsNullOrEmpty(item.CardId))
            {
                // 同一卡片重复保存时覆盖旧项（一个卡片一个定时项）
                existing = _items.FirstOrDefault(x => x.CardId.Equals(item.CardId, StringComparison.OrdinalIgnoreCase));
            }
            if (existing == null)
            {
                item.NextRunAt = DateTime.Now.AddMinutes(item.IntervalMinutes);
                _items.Add(item);
            }
            else
            {
                existing.CardId = item.CardId;
                existing.Name = item.Name;
                existing.Type = item.Type;
                existing.ProjectDir = item.ProjectDir;
                existing.OutputDir = item.OutputDir;
                existing.IntervalMinutes = item.IntervalMinutes;
                existing.Enabled = item.Enabled;
                // 修改配置后重新计算下次触发时间，避免旧间隔残留
                existing.NextRunAt = DateTime.Now.AddMinutes(item.IntervalMinutes);
                item = existing;
            }
            SaveLocked();
            return item;
        }
    }

    /// <summary>删除调度项。</summary>
    public bool RemoveSchedule(string id)
    {
        lock (_lock)
        {
            var item = _items.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (item == null) return false;
            _items.Remove(item);
            SaveLocked();
            return true;
        }
    }

    /// <summary>巡检到期任务并触发构建（失败退避至少 5 分钟，避免死循环刷日志）。</summary>
    private void CheckAndRun()
    {
        if (_disposed) return;
        ScheduleItem[] due;
        lock (_lock)
        {
            due = _items.Where(x => x.Enabled && DateTime.Now >= x.NextRunAt).ToArray();
        }
        foreach (var item in due)
        {
            try
            {
                var job = _buildService.StartBuild(new UniversalBuildRequest
                {
                    Type = item.Type,
                    ProjectDir = item.ProjectDir,
                    OutputDir = item.OutputDir,
                    Name = item.Name,
                });
                lock (_lock)
                {
                    item.LastRunAt = DateTime.Now;
                    item.LastJobId = job.Id;
                    item.LastError = null;
                    item.NextRunAt = DateTime.Now.AddMinutes(item.IntervalMinutes);
                    SaveLocked();
                }
                _logger.LogInformation("定时构建已触发: {Name} (job {JobId})", item.Name, job.Id);
            }
            catch (Exception ex)
            {
                lock (_lock)
                {
                    item.LastError = ex.Message;
                    item.NextRunAt = DateTime.Now.AddMinutes(Math.Max(5, item.IntervalMinutes));
                    SaveLocked();
                }
                _logger.LogError(ex, "定时构建触发失败: {Name}", item.Name);
            }
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(StorePath)) return;
            _items = JsonSerializer.Deserialize<List<ScheduleItem>>(File.ReadAllText(StorePath), JsonOpts) ?? new();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读取定时构建配置失败");
        }
    }

    /// <summary>保存到磁盘（调用方需持有 _lock）。</summary>
    private void SaveLocked()
    {
        File.WriteAllText(StorePath, JsonSerializer.Serialize(_items, JsonOpts));
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Dispose();
    }
}

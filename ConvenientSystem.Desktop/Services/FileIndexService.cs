namespace ConvenientSystem;

/// <summary>
/// 本地文件索引服务：后台全盘扫描固定驱动器，跳过系统目录，
/// 索引持久化到 exe 目录\file-index-v2.txt，融入启动器搜索。
/// </summary>
internal sealed class FileIndexService
{
    private const int MaxFiles = 2_000_000;

    private readonly List<string> _paths = new();
    private readonly List<string> _names = new(); // 预计算的小写文件名，加速搜索
    private readonly object _lock = new();
    private bool _loaded;
    private volatile bool _scanning;
    private volatile int _scannedCount;
    private readonly string _indexFile;
    private DateTime _indexTime;
    private HashSet<string>? _indexedDirs;
    private System.Threading.Timer? _rebuildTimer;

    private static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "windows", "$recycle.bin", "system volume information",
        "program files", "program files (x86)", "programdata",
        "node_modules", ".git", ".svn", ".vs", ".idea", "__pycache__",
        "$winreagent", "config.msi", "msocache",
    };

    public bool IsLoaded { get { lock (_lock) return _loaded; } }
    public int Count { get { lock (_lock) return _paths.Count; } }
    public bool IsScanning => _scanning;
    public int ScannedCount => _scannedCount;

    public FileIndexService()
    {
        _indexFile = Path.Combine(
            Application.StartupPath, "file-index-v2.txt");
    }

    /// <summary>启动索引：优先从文件加载（即时可用）；无文件或文件过期时，延迟 2 分钟再后台全盘重建，避开启动高峰。</summary>
    public void StartIndexAsync(Action? onCompleted = null)
    {
        if (_loaded) { onCompleted?.Invoke(); StartPeriodicRebuild(); return; }

        if (TryLoadFromFile())
        {
            _loaded = true;
            onCompleted?.Invoke();
            StartPeriodicRebuild();
            if (IsStale())
                ScheduleDelayedRebuild(null);
            return;
        }

        // 首次使用（无索引文件）：同样延迟重建，期间启动器只显示菜单与自定义条目
        StartPeriodicRebuild();
        ScheduleDelayedRebuild(onCompleted);
    }

    /// <summary>启动 10 分钟周期增量刷新定时器（替代原全量重建，只扫有变化的目录）。</summary>
    private void StartPeriodicRebuild()
    {
        _rebuildTimer?.Dispose();
        _rebuildTimer = new System.Threading.Timer(
            _ => PeriodicRefresh(), null,
            TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));
    }

    /// <summary>周期性增量刷新索引；若正在扫描则跳过本轮。</summary>
    private void PeriodicRefresh()
    {
        if (_scanning || !_loaded) return;
        QuickRefresh();
    }

    /// <summary>延迟 2 分钟后执行一次全盘重建，避免程序刚启动就全盘扫描拖慢系统。</summary>
    private void ScheduleDelayedRebuild(Action? onCompleted)
    {
        Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromMinutes(2));
            if (_scanning) return;
            RebuildAndSave(onCompleted);
        });
    }

    /// <summary>按文件名包含匹配搜索，返回最多 maxResults 条。</summary>
    public List<LauncherItem> Search(string query, int maxResults)
    {
        var results = new List<LauncherItem>();
        if (string.IsNullOrEmpty(query)) return results;

        var q = query.ToLowerInvariant();

        lock (_lock)
        {
            // 先搜文件名
            for (int i = 0; i < _names.Count; i++)
            {
                if (_names[i].Contains(q))
                {
                    var path = _paths[i];
                    results.Add(new LauncherItem
                    {
                        Type = LauncherItemType.File,
                        Title = Path.GetFileName(path),
                        Subtitle = Path.GetDirectoryName(path) ?? "",
                        Target = path,
                    });
                    if (results.Count >= maxResults) break;
                }
            }

            // 文件名没搜到则搜完整路径（兼容中文编码歧义）
            if (results.Count == 0)
            {
                for (int i = 0; i < _paths.Count; i++)
                {
                    if (_paths[i].ToLowerInvariant().Contains(q))
                    {
                        var path = _paths[i];
                        results.Add(new LauncherItem
                        {
                            Type = LauncherItemType.File,
                            Title = Path.GetFileName(path),
                            Subtitle = Path.GetDirectoryName(path) ?? "",
                            Target = path,
                        });
                        if (results.Count >= maxResults) break;
                    }
                }
            }
        }

        return results;
    }

    private bool TryLoadFromFile()
    {
        try
        {
            if (!File.Exists(_indexFile)) return false;
            var lines = File.ReadAllLines(_indexFile, System.Text.Encoding.UTF8);
            lock (_lock)
            {
                _paths.Clear();
                _names.Clear();
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    _paths.Add(line);
                    _names.Add(Path.GetFileName(line).ToLowerInvariant());
                }
            }
            EnsureIndexFileInIndex();
            return true;
        }
        catch { return false; }
    }

    /// <summary>将索引文件本身加入内存索引（扫描时它还不存在，写盘后才可搜索）。</summary>
    private void EnsureIndexFileInIndex()
    {
        try
        {
            if (!File.Exists(_indexFile)) return;
            lock (_lock)
            {
                _paths.Add(_indexFile);
                _names.Add(Path.GetFileName(_indexFile).ToLowerInvariant());
            }
        }
        catch { }
    }

    private void RebuildAndSave(Action? onCompleted)
    {
        _scanning = true;
        _scannedCount = 0;
        var found = new List<string>(MaxFiles);
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (found.Count >= MaxFiles) break;
            if (drive.DriveType != DriveType.Fixed) continue;
            if (!drive.IsReady) continue;
            ScanDirectory(drive.Name, found);
        }

        lock (_lock)
        {
            _paths.Clear();
            _paths.AddRange(found);
            _names.Clear();
            foreach (var p in found)
                _names.Add(Path.GetFileName(p).ToLowerInvariant());
            _loaded = true;
        }

        _scanning = false;
        _indexedDirs = null; // 全量重建后目录增量缓存失效，下次 QuickRefresh 重新收集

        try { File.WriteAllLines(_indexFile, found, System.Text.Encoding.UTF8); } catch { }
        EnsureIndexFileInIndex();

        onCompleted?.Invoke();
    }

    private void ScanDirectory(string dir, List<string> results)
    {
        if (results.Count >= MaxFiles) return;

        var name = Path.GetFileName(dir).ToLowerInvariant();
        if (SkipDirs.Contains(name)) return;

        // IgnoreInaccessible：无权限目录在枚举层被跳过，异常不会延迟到 foreach 消费时才抛出
        IEnumerable<string> files;
        IEnumerable<string> dirs;
        try
        {
            var options = new EnumerationOptions { IgnoreInaccessible = true };
            files = Directory.EnumerateFiles(dir, "*", options);
            dirs = Directory.EnumerateDirectories(dir, "*", options);
        }
        catch { return; } // 访问被拒绝等

        foreach (var file in files)
        {
            try { results.Add(file); } catch { }
            if (results.Count >= MaxFiles) return;
        }

        _scannedCount = results.Count;

        foreach (var sub in dirs)
        {
            if (results.Count >= MaxFiles) return;
            ScanDirectory(sub, results);
        }
    }

    /// <summary>
    /// 增量刷新：检查索引中已有目录的最后修改时间，只重新扫描有变化的目录，
    /// 将新文件增量加入索引。比全盘重建快得多，在启动器打开时自动调用。
    /// </summary>
    public void QuickRefresh()
    {
        if (!_loaded || _scanning) return;

        try
        {
            // 首次调用时缓存索引构建时间和所有唯一目录
            if (_indexedDirs is null)
            {
                _indexTime = File.GetLastWriteTime(_indexFile);
                _indexedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                List<string> pathsCopy;
                lock (_lock) { pathsCopy = new List<string>(_paths); }
                foreach (var p in pathsCopy)
                {
                    var dir = Path.GetDirectoryName(p);
                    if (!string.IsNullOrEmpty(dir))
                        _indexedDirs.Add(dir);
                }
            }

            // 在锁外构建已有文件集合，避免阻塞搜索
            List<string> existingCopy;
            lock (_lock) { existingCopy = new List<string>(_paths); }
            var existing = new HashSet<string>(existingCopy, StringComparer.OrdinalIgnoreCase);

            int newCount = 0;
            foreach (var dir in _indexedDirs)
            {
                try
                {
                    // 跳过自索引构建以来未修改的目录
                    if (Directory.GetLastWriteTime(dir) < _indexTime) continue;

                    // 重新扫描该目录（含子目录，复用 ScanDirectory 继承 SkipDirs 剪枝，避免扫进 node_modules 等大目录）
                    var rescan = new List<string>();
                    ScanDirectory(dir, rescan);
                    foreach (var file in rescan)
                    {
                        if (!existing.Contains(file))
                        {
                            lock (_lock)
                            {
                                _paths.Add(file);
                                _names.Add(Path.GetFileName(file).ToLowerInvariant());
                            }
                            existing.Add(file);
                            newCount++;
                        }
                    }
                }
                catch { }
            }

            if (newCount > 0)
            {
                List<string> toSave;
                lock (_lock) { toSave = new List<string>(_paths); }
                File.WriteAllLines(_indexFile, toSave, System.Text.Encoding.UTF8);
                _indexTime = File.GetLastWriteTime(_indexFile);
            }
        }
        catch { }
    }

    /// <summary>强制重建索引（手动触发），立即标记为未加载让 UI 显示进度。</summary>
    public void ForceRebuild(Action? onCompleted = null)
    {
        lock (_lock) { _loaded = false; }
        Task.Run(() => RebuildAndSave(onCompleted));
    }

    private bool IsStale()
    {
        try
        {
            var info = new FileInfo(_indexFile);
            return DateTime.Now - info.LastWriteTime > TimeSpan.FromHours(2);
        }
        catch { return true; }
    }
}

namespace ConvenientSystem;

/// <summary>
/// 本机程序索引：扫描开始菜单快捷方式（.lnk）与桌面，构建标题/路径/图标。
/// 不解析 .lnk 内部目标路径——直接用文件名作标题、.lnk 路径作启动目标，
/// Process.Start(.lnk) 由系统自动解析为对应 exe。
/// 后台线程首次扫描，避免阻塞 UI。
/// </summary>
internal sealed class AppIndexService
{
    private readonly List<LauncherItem> _apps = new();
    private readonly object _lock = new();
    private bool _loaded;

    /// <summary>所有已索引的程序项（快照副本）。</summary>
    public IReadOnlyList<LauncherItem> Apps
    {
        get { lock (_lock) return _apps.ToList(); }
    }

    public bool IsLoaded => _loaded;

    /// <summary>启动后台索引扫描。立即返回，扫描完成后回调（用于刷新已打开的启动器）。</summary>
    public void StartIndexAsync(Action? onCompleted = null)
    {
        if (_loaded) { onCompleted?.Invoke(); return; }
        Task.Run(() =>
        {
            Scan();
            _loaded = true;
            onCompleted?.Invoke();
        });
    }

    private void Scan()
    {
        var dirs = GetStartMenuDirs();
        var found = new List<LauncherItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in dirs)
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var lnk in EnumerateLnk(dir))
            {
                try
                {
                    var name = Path.GetFileNameWithoutExtension(lnk);
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    var key = name.ToLowerInvariant();
                    if (!seen.Add(key)) continue;

                    Image? icon = null;
                    try { icon = Icon.ExtractAssociatedIcon(lnk)?.ToBitmap(); }
                    catch { /* 图标提取失败忽略 */ }

                    found.Add(new LauncherItem
                    {
                        Type = LauncherItemType.App,
                        Title = name,
                        Subtitle = lnk,
                        Target = lnk,
                        Icon = icon
                    });
                }
                catch { /* 单条失败忽略 */ }
            }
        }

        lock (_lock)
        {
            _apps.Clear();
            _apps.AddRange(found);
        }
    }

    private static IEnumerable<string> EnumerateLnk(string dir)
    {
        try
        {
            return Directory.EnumerateFiles(dir, "*.lnk", SearchOption.AllDirectories);
        }
        catch { return Array.Empty<string>(); }
    }

    /// <summary>开始菜单目录（当前用户 + 所有用户）+ 桌面快捷方式。</summary>
    private static IEnumerable<string> GetStartMenuDirs()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    }
}

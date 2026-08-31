using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Text.Json;

namespace ConvenientSystem;

/// <summary>
/// 快速启动器：全局热键（Ctrl+Alt+Space）呼出的搜索式启动器，
/// 聚合本机程序、自定义条目和系统页面，回车执行。失焦自动隐藏。
/// </summary>
internal sealed class QuickLauncher : Form
{
    // 品牌色（coding-standards 2.10: #3b82f6）
    private static readonly Color Brand = Color.FromArgb(59, 130, 246);
    private static readonly Color Bg = Color.White;
    private static readonly Color HoverBg = Color.FromArgb(239, 246, 255);
    private static readonly Color SelectedBg = Color.FromArgb(219, 234, 254);
    private static readonly Color TextColor = Color.FromArgb(30, 41, 59);
    private static readonly Color SubColor = Color.FromArgb(100, 116, 139);
    private static readonly Color BorderColor = Color.FromArgb(226, 232, 240);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern void AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    /// <summary>强制将窗口激活到前台，绕过 Windows 前台锁定（后台线程无法直接抢焦点）。</summary>
    private void ForceActivate()
    {
        var foreHwnd = GetForegroundWindow();
        var curThread = GetCurrentThreadId();

        if (foreHwnd != IntPtr.Zero)
        {
            var foreThread = GetWindowThreadProcessId(foreHwnd, out _);
            if (foreThread != curThread)
            {
                AttachThreadInput(curThread, foreThread, true);
                SetForegroundWindow(Handle);
                BringWindowToTop(Handle);
                _search.Focus();
                AttachThreadInput(curThread, foreThread, false);
                return;
            }
        }

        SetForegroundWindow(Handle);
        _search.Focus();
    }

    /// <summary>检查指定窗口是否为 IME 输入法窗口（中文输入时不隐藏启动器）。</summary>
    private static bool IsImeWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        var sb = new System.Text.StringBuilder(256);
        GetClassName(hwnd, sb, sb.Capacity);
        var name = sb.ToString();
        return name.IndexOf("IME", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("msctfime", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private readonly float _dpi;
    private int S(int v) => Math.Max(1, (int)Math.Round(v * _dpi));

    private const int BaseWidth = 560;
    private const int BaseSearchH = 48;
    private const int BaseItemH = 48;
    private const int BaseMaxItems = 12;

    private readonly AppIndexService _appIndex;
    private readonly LauncherStore _store;
    private readonly FileIndexService _fileIndex;
    private readonly Action<string, string, bool> _openPage;
    private readonly Action<string> _openUrl;
    private readonly System.Windows.Forms.Timer _deactivateTimer;
    private readonly System.Windows.Forms.Timer _fileSearchTimer;
    private readonly System.Windows.Forms.Timer _indexTimer;

    private readonly TextBox _search;
    private readonly ListBox _list;
    private readonly ProgressBar _indexBar;
    private readonly Label _indexLabel;
    private readonly Font _titleFont;
    private readonly Font _subFont;
    private readonly Font _iconFont;

    private readonly List<LauncherItem> _all = new();
    private readonly List<LauncherItem> _filtered = new();
    private JsonElement? _menuTree;
    private int _hoverIndex = -1;
    private bool _ready;

    public QuickLauncher(AppIndexService appIndex, LauncherStore store,
        FileIndexService fileIndex,
        Action<string, string, bool> openPage, Action<string> openUrl)
    {
        _appIndex = appIndex;
        _store = store;
        _fileIndex = fileIndex;
        _openPage = openPage;
        _openUrl = openUrl;

        _dpi = DeviceDpi > 0 ? DeviceDpi / 96f : 1f;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        DoubleBuffered = true;
        BackColor = Bg;
        Width = S(BaseWidth);

        _titleFont = new Font("Microsoft YaHei UI", 10f);
        _subFont = new Font("Microsoft YaHei UI", 8f);
        _iconFont = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);

        _search = new TextBox
        {
            BorderStyle = BorderStyle.None,
            Font = new Font("Microsoft YaHei UI", 13f),
            ForeColor = TextColor,
            Location = new Point(S(12), S(14)),
            Width = S(BaseWidth) - S(24),
            ImeMode = ImeMode.On,
        };
        _search.TextChanged += (_, _) => DoSearch();
        _search.KeyDown += OnSearchKeyDown;
        Controls.Add(_search);

        _list = new ListBox
        {
            BorderStyle = BorderStyle.None,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = S(BaseItemH),
            Location = new Point(0, S(BaseSearchH)),
            Width = S(BaseWidth),
            BackColor = Bg,
        };
        // 启用 ListBox 双缓冲：WinForms ListBox 默认不开 DoubleBuffered，
        // OwnerDraw + 每次悬停全量 Invalidate 会导致擦背景→画内容的闪烁循环。
        typeof(ListBox).InvokeMember("DoubleBuffered",
            System.Reflection.BindingFlags.SetProperty | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            null, _list, new object[] { true });
        typeof(ListBox).InvokeMember("SetStyle",
            System.Reflection.BindingFlags.InvokeMethod | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            null, _list, new object[] { ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true });
        _list.DrawItem += OnDrawItem;
        _list.MouseDoubleClick += (_, _) => ExecuteSelected();
        _list.MouseMove += (_, e) =>
        {
            int idx = _list.IndexFromPoint(e.Location);
            if (idx != _hoverIndex)
            {
                int old = _hoverIndex;
                _hoverIndex = idx;
                // 只重绘变化的两个条目，不全量 Invalidate
                if (old >= 0 && old < _list.Items.Count)
                    _list.Invalidate(_list.GetItemRectangle(old));
                if (idx >= 0 && idx < _list.Items.Count)
                    _list.Invalidate(_list.GetItemRectangle(idx));
            }
        };
        _list.MouseLeave += (_, _) =>
        {
            if (_hoverIndex >= 0 && _hoverIndex < _list.Items.Count)
                _list.Invalidate(_list.GetItemRectangle(_hoverIndex));
            _hoverIndex = -1;
        };
        Controls.Add(_list);

        _indexBar = new ProgressBar
        {
            Style = ProgressBarStyle.Marquee,
            Height = S(3),
            Width = S(BaseWidth),
            Visible = false,
        };
        Controls.Add(_indexBar);

        _indexLabel = new Label
        {
            ForeColor = SubColor,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = _subFont,
            Width = S(BaseWidth),
            Height = S(20),
            Visible = false,
        };
        Controls.Add(_indexLabel);

        _fileSearchTimer = new System.Windows.Forms.Timer { Interval = 200 };
        _fileSearchTimer.Tick += (_, _) => { _fileSearchTimer.Stop(); SearchFiles(); };

        _indexTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _indexTimer.Tick += (_, _) => DoSearch();

        _deactivateTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _deactivateTimer.Tick += (_, _) =>
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == Handle) { _deactivateTimer.Stop(); return; }
            if (IsImeWindow(hwnd)) return;
            // 鼠标仍在启动器范围内则保持显示（用户可能正在交互）
            if (Bounds.Contains(MousePosition)) return;
            _deactivateTimer.Stop();
            Hide();
        };

        RebuildItems();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW：不出现在 Alt+Tab
            return cp;
        }
    }

    /// <summary>注入前端菜单树，提取所有页面项。</summary>
    public void SetMenuTree(JsonElement? tree)
    {
        _menuTree = tree;
        RebuildItems();
    }

    /// <summary>呼出启动器：重建索引、清空搜索、定位到屏幕顶部居中。</summary>
    public void Popup()
    {
        if (Visible) { Hide(); return; }
        _ready = false;
        _deactivateTimer.Stop();
        RebuildItems();
        _search.Text = "";
        PositionWindow();
        Show();
        ForceActivate();
        _ready = true;

        // 后台从 API 拉取最新条目并刷新，实现网页端与桌面端双向同步。
        // 同步失败时继续使用本地缓存，不影响本次呼出。
        Task.Run(() =>
        {
            try { _store.ReloadFromApi(); }
            catch { /* 忽略同步失败 */ }
            BeginInvoke(RefreshResults);
        });
    }

    /// <summary>程序索引完成后刷新结果。</summary>
    public void RefreshResults() => RebuildItems();

    private void PositionWindow()
    {
        var screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        Location = new Point(
            screen.Left + (screen.Width - Width) / 2,
            screen.Top + (int)(screen.Height * 0.2));
    }

    /// <summary>重建全部可搜索项：页面 + 自定义条目 + 本机程序。</summary>
    private void RebuildItems()
    {
        _all.Clear();

        if (_menuTree is { ValueKind: JsonValueKind.Array } arr)
            foreach (var node in arr.EnumerateArray())
                CollectPages(node, _all);

        foreach (var entry in _store.Entries)
            _all.Add(new LauncherItem
            {
                Type = LauncherItemType.Custom,
                Title = entry.Title,
                Subtitle = entry.Target,
                Target = entry.Target,
                Kind = entry.Kind,
            });

        foreach (var app in _appIndex.Apps)
            _all.Add(app);

        DoSearch();
    }

    /// <summary>递归提取菜单树中所有叶子页面（不限于 float=true）。</summary>
    private static void CollectPages(JsonElement node, List<LauncherItem> list)
    {
        string title = node.TryGetProperty("title", out var t) ? (t.GetString() ?? "") : "";
        string page = node.TryGetProperty("page", out var p) && p.ValueKind == JsonValueKind.String
            ? (p.GetString() ?? "") : "";
        bool external = node.TryGetProperty("external", out var ext) && ext.ValueKind == JsonValueKind.True;

        if (!string.IsNullOrEmpty(page))
            list.Add(new LauncherItem
            {
                Type = LauncherItemType.Page,
                Title = title,
                Subtitle = page,
                Target = page,
                External = external,
            });

        if (node.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
            foreach (var child in children.EnumerateArray())
                CollectPages(child, list);
    }

    /// <summary>按搜索词过滤并排序：标题开头匹配 &gt; 标题包含 &gt; 子标题包含。</summary>
    private void DoSearch()
    {
        var q = _search.Text.Trim();
        _filtered.Clear();

        if (string.IsNullOrEmpty(q))
        {
            _filtered.AddRange(_all);
        }
        else
        {
            var startsWith = new List<LauncherItem>();
            var titleContains = new List<LauncherItem>();
            var subContains = new List<LauncherItem>();

            foreach (var item in _all)
            {
                if (item.Title.StartsWith(q, StringComparison.OrdinalIgnoreCase))
                    startsWith.Add(item);
                else if (item.Title.Contains(q, StringComparison.OrdinalIgnoreCase))
                    titleContains.Add(item);
                else if (item.Subtitle.Contains(q, StringComparison.OrdinalIgnoreCase))
                    subContains.Add(item);
            }

            _filtered.AddRange(startsWith);
            _filtered.AddRange(titleContains);
            _filtered.AddRange(subContains);
        }

        // 有搜索词时为文件结果预留 4 个名额（文件索引服务 200ms 后异步补充）
        int cap = string.IsNullOrEmpty(q) ? BaseMaxItems : BaseMaxItems - 4;
        if (_filtered.Count > cap)
            _filtered.RemoveRange(cap, _filtered.Count - cap);

        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var item in _filtered)
            _list.Items.Add(item);
        _list.EndUpdate();

        if (_list.Items.Count > 0)
            _list.SelectedIndex = 0;

        // 状态提示：索引中 / 搜索中 / 无
        if (!_fileIndex.IsLoaded)
        {
            _indexBar.Visible = true;
            _indexLabel.Visible = true;
            _indexLabel.Text = $"正在索引文件... 已扫描 {_fileIndex.ScannedCount:N0} 个";
            _indexTimer.Start();
        }
        else
        {
            _indexTimer.Stop();
            _indexBar.Visible = false;
            if (_filtered.Count == 0 && !string.IsNullOrEmpty(q))
            {
                _indexLabel.Visible = true;
                _indexLabel.Text = "正在搜索文件...";
            }
            else
            {
                _indexLabel.Visible = false;
            }
        }

        AdjustHeight();

        // 延迟搜索文件索引（全盘搜索较慢，debounce 避免每次按键都全扫）
        _fileSearchTimer.Stop();
        _fileSearchTimer.Start();
    }

    /// <summary>延迟搜索本地文件索引，补充文件结果到列表。</summary>
    private void SearchFiles()
    {
        if (!_fileIndex.IsLoaded) return;
        var q = _search.Text.Trim();
        if (string.IsNullOrEmpty(q)) return;

        int budget = BaseMaxItems - _filtered.Count;
        if (budget > 0)
        {
            var files = _fileIndex.Search(q, budget);
            if (files.Count > 0)
            {
                _filtered.AddRange(files);
                if (_filtered.Count > BaseMaxItems)
                    _filtered.RemoveRange(BaseMaxItems, _filtered.Count - BaseMaxItems);

                _list.BeginUpdate();
                _list.Items.Clear();
                foreach (var item in _filtered)
                    _list.Items.Add(item);
                _list.EndUpdate();

                if (_list.Items.Count > 0 && _list.SelectedIndex < 0)
                    _list.SelectedIndex = 0;
            }
        }

        // 搜索完成后：有结果隐藏提示，无结果显示未找到
        if (_filtered.Count == 0)
        {
            _indexBar.Visible = false;
            _indexLabel.Visible = true;
            _indexLabel.Text = "未找到相关项目";
        }
        else
        {
            _indexLabel.Visible = false;
        }

        AdjustHeight();
    }

    private void AdjustHeight()
    {
        int count = _list.Items.Count;
        int listH = count * S(BaseItemH);
        bool showBar = _indexBar.Visible;
        bool showLabel = _indexLabel.Visible;
        int barH = showBar ? S(3) : 0;
        int labelH = showLabel ? S(20) : 0;
        int statusH = barH + labelH;

        SuspendLayout();
        Height = S(BaseSearchH) + listH + statusH;
        _list.Height = listH;
        _list.Visible = count > 0;
        if (showLabel)
        {
            int y = S(BaseSearchH) + listH;
            _indexBar.Location = new Point(0, y);
            _indexLabel.Location = new Point(0, y + barH);
        }
        ResumeLayout();
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Down:
                if (_list.Items.Count > 0)
                {
                    _list.SelectedIndex = Math.Min(_list.Items.Count - 1, _list.SelectedIndex + 1);
                    e.SuppressKeyPress = true;
                }
                break;
            case Keys.Up:
                if (_list.Items.Count > 0)
                {
                    _list.SelectedIndex = Math.Max(0, _list.SelectedIndex - 1);
                    e.SuppressKeyPress = true;
                }
                break;
            case Keys.Enter:
                ExecuteSelected();
                e.SuppressKeyPress = true;
                break;
            case Keys.Escape:
                Hide();
                e.SuppressKeyPress = true;
                break;
        }
    }

    private void ExecuteSelected()
    {
        if (_list.SelectedIndex < 0 || _list.SelectedIndex >= _filtered.Count) return;
        Execute(_filtered[_list.SelectedIndex]);
        Hide();
    }

    /// <summary>按类型执行启动项：App→Process.Start(.lnk)、Custom(url)→浏览器、Page→OpenPageWindow。</summary>
    private void Execute(LauncherItem item)
    {
        try
        {
            switch (item.Type)
            {
                case LauncherItemType.App:
                    Process.Start(new ProcessStartInfo(item.Target) { UseShellExecute = true });
                    break;
                case LauncherItemType.Custom:
                    if (item.Kind == "url")
                        _openUrl(item.Target);
                    else
                        Process.Start(new ProcessStartInfo(item.Target) { UseShellExecute = true });
                    break;
                case LauncherItemType.Page:
                    _openPage(item.Target, item.Title, item.External);
                    break;
                case LauncherItemType.File:
                    Process.Start(new ProcessStartInfo(item.Target) { UseShellExecute = true });
                    break;
            }
        }
        catch { /* 启动失败忽略 */ }
    }

    private void OnDrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _filtered.Count) return;
        var item = _filtered[e.Index];

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        // 背景：选中 &gt; 悬停 &gt; 默认
        bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        bool hovered = e.Index == _hoverIndex && !selected;
        Color bg = selected ? SelectedBg : (hovered ? HoverBg : Bg);
        using (var bgBrush = new SolidBrush(bg))
            e.Graphics.FillRectangle(bgBrush, e.Bounds);

        // 图标：有图标用图标，无图标画主题色首字母圆
        int iconSize = S(24);
        int iconX = e.Bounds.X + S(12);
        int iconY = e.Bounds.Y + (e.Bounds.Height - iconSize) / 2;

        if (item.Icon is not null)
        {
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.DrawImage(item.Icon, iconX, iconY, iconSize, iconSize);
        }
        else
        {
            using var circleBrush = new SolidBrush(Brand);
            e.Graphics.FillEllipse(circleBrush, iconX, iconY, iconSize, iconSize);
            var initial = !string.IsNullOrEmpty(item.Title) ? item.Title[0].ToString() : "?";
            TextRenderer.DrawText(e.Graphics, initial, _iconFont,
                new Rectangle(iconX, iconY, iconSize, iconSize), Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        // 标题 + 子标题
        int textX = iconX + iconSize + S(8);
        int textW = e.Bounds.Width - textX - S(12);
        TextRenderer.DrawText(e.Graphics, item.Title, _titleFont,
            new Rectangle(textX, e.Bounds.Y + S(7), textW, S(20)),
            TextColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(e.Graphics, item.Subtitle, _subFont,
            new Rectangle(textX, e.Bounds.Y + S(26), textW, S(16)),
            SubColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        // 搜索区与结果区之间的分隔线
        using var pen = new Pen(BorderColor, 1f);
        int sepY = S(BaseSearchH) - 1;
        e.Graphics.DrawLine(pen, 0, sepY, Width, sepY);
    }

    /// <summary>失焦时启动延迟隐藏检查：前台是 IME 则继续等待，是启动器自身则取消，否则隐藏。</summary>
    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (!_ready) return;
        _deactivateTimer.Stop();
        _deactivateTimer.Start();
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        _deactivateTimer.Stop();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (!Visible) _indexTimer.Stop();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _titleFont?.Dispose();
            _subFont?.Dispose();
            _iconFont?.Dispose();
            _fileSearchTimer?.Dispose();
            _deactivateTimer?.Dispose();
            _indexTimer?.Dispose();
        }
        base.Dispose(disposing);
    }
}

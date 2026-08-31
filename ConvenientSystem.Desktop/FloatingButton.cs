using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace ConvenientSystem;

/// <summary>
/// 全局悬浮按钮窗口：只显示程序图标，可自由拖动。
/// 鼠标悬停/左键单击弹出“平铺全部展开”的卡片网格菜单（FloatingPanel），
/// 双击打开主窗口，右键仅弹出“关闭悬浮按钮”。使用 Win32 API 强制全局置顶。
/// </summary>
public sealed class FloatingButton : Form
{
    // 拖动状态
    private bool _dragging;
    private Point _dragStart;
    private bool _moved;
    private bool _hovered;
    private bool _panelVisible;
    // 面板关闭后的冷却期：防止面板刚关闭就因鼠标仍在按钮上而立即重新弹出
    private bool _inPanelCooldown;

    /// <summary>双击按钮时触发的回调（如显示主窗口）。</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [Browsable(false)]
    public Action? DoubleClickAction { get; set; }

    /// <summary>打开页面回调（由 MainForm 注入，参数：page, title, external）。</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [Browsable(false)]
    public Action<string, string, bool>? OpenPageAction { get; set; }

    /// <summary>菜单树 JSON（前端上报的首页菜单树，悬浮按钮仅显示 float=true 的项）。</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [Browsable(false)]
    public JsonElement? MenuTree { get; set; }

    /// <summary>呼出快速启动器回调。</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [Browsable(false)]
    public Action? LauncherPopupAction { get; set; }

    /// <summary>管理启动器条目回调。</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [Browsable(false)]
    public Action? EntryEditorAction { get; set; }

    /// <summary>刷新文件索引回调。</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [Browsable(false)]
    public Action? RefreshIndexAction { get; set; }

    /// <summary>关闭所有弹窗回调。</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [Browsable(false)]
    public Action? CloseAllWindowsAction { get; set; }

    /// <summary>重启应用回调。</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [Browsable(false)]
    public Action? RestartAction { get; set; }

    /// <summary>退出登录回调。</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [Browsable(false)]
    public Action? LogoutAction { get; set; }

    /// <summary>退出程序回调。</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [Browsable(false)]
    public Action? ExitAction { get; set; }

    /// <summary>查询当前是否有已打开的弹窗（供菜单 Enabled 动态判断）。</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [Browsable(false)]
    public Func<bool>? HasOpenWindowsFunc { get; set; }

    // 程序图标
    private readonly Image? _appIcon;
    private readonly int _iconSize;

    /// <summary>窗体在图标四周多留的透明余量，供悬停放大使用。</summary>
    private const int IconPad = 2;

    // 是否处于逐像素 alpha 的分层窗口模式。
    // UpdateLayeredWindow 一旦失败就退回 TransparencyKey 抠色，保证按钮不会变成完全看不见。
    private bool _layered = true;

    // 悬停定时器
    private readonly System.Windows.Forms.Timer _hoverTimer;

    // 单击/双击区分定时器
    private readonly System.Windows.Forms.Timer _clickTimer;
    private bool _waitingForDblClick;

    // 定时强制置顶定时器（每 2 秒重新声明，防止其他程序抢占）
    private readonly System.Windows.Forms.Timer _topMostTimer;
    // 面板关闭冷却定时器
    private readonly System.Windows.Forms.Timer _panelCooldownTimer;

    // 右键菜单（关闭悬浮按钮）
    private readonly ContextMenuStrip _selfMenu = new();

    // 自定义菜单渲染器
    private static readonly ModernMenuRenderer SharedRenderer = new();

    // 当前显示的悬浮面板（同时只允许一个）
    private FloatingPanel? _currentPanel;

    // Win32 API
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;

    // 逐像素 alpha 分层窗口所需的 GDI / GDI32 接口
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(IntPtr hWnd, IntPtr hdcDst, ref Point pptDst, ref Size psize,
        IntPtr hdcSrc, ref Point pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    private const byte AC_SRC_OVER = 0x00;
    private const byte AC_SRC_ALPHA = 0x01;
    private const int ULW_ALPHA = 0x02;

    // 扩展窗口样式
    private const int WS_EX_TOPMOST = 0x00000008;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_LAYERED = 0x00080000;

    public FloatingButton(int iconSize = 40)
    {
        _iconSize = iconSize;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        // 窗体比图标大出一圈 IconPad 的透明余量，悬停放大 2px 时不会被窗体边界裁掉
        Size = new Size(iconSize + IconPad * 2, iconSize + IconPad * 2);

        DoubleBuffered = true;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.SupportsTransparentBackColor |
            ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;

        // 透明靠逐像素 alpha（UpdateLayeredWindow）实现，所以不设 BackColor / TransparencyKey。
        // 抠色透明只能剔除颜色精确等于键色的像素；图标边缘那圈半透明像素会先与
        // 近黑底色混合成深色实色再被留下，就是之前看到的黑色虚影。
        // 图标按 48px 取帧（ico 的标准尺寸之一），再缩到 40/42 都是小幅缩小，比从 64 缩更清楚。
        _appIcon = LoadAppIcon(48);

        // 悬停定时器：50ms 后弹出面板（几乎即刻响应）
        _hoverTimer = new System.Windows.Forms.Timer { Interval = 50 };
        _hoverTimer.Tick += (_, _) =>
        {
            _hoverTimer.Stop();
            if (_hovered && !_dragging && !_panelVisible && !_waitingForDblClick && !_inPanelCooldown) ShowPanel();
        };

        // 单击/双击区分定时器
        _clickTimer = new System.Windows.Forms.Timer { Interval = SystemInformation.DoubleClickTime };
        _clickTimer.Tick += (_, _) =>
        {
            _clickTimer.Stop();
            if (_waitingForDblClick)
            {
                _waitingForDblClick = false;
                if (!_panelVisible) ShowPanel();
            }
        };

        // 定时强制置顶：每 2 秒用 Win32 API 重新声明
        // 同时兼任看门狗：检测面板声称可见但实际已关闭的异常状态并自动恢复。
        _topMostTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        _topMostTimer.Tick += (_, _) =>
        {
            ForceTopMost();
            // 看门狗：面板声称可见但实际已关闭/disposed 时，恢复按钮状态。
            // 防止面板关闭回调异常失败导致按钮永远卡在 _panelVisible=true、TopMost 未恢复。
            if (_panelVisible)
            {
                var panel = _currentPanel;
                if (panel is null || panel.IsDisposed || !panel.Visible)
                    OnPanelClosed();
            }
        };

        // 面板关闭冷却：面板关闭后 300ms 内不重新弹出，防止抖动
        _panelCooldownTimer = new System.Windows.Forms.Timer { Interval = 300 };
        _panelCooldownTimer.Tick += (_, _) => { _panelCooldownTimer.Stop(); _inPanelCooldown = false; };

        // 右键菜单
        _selfMenu.Renderer = SharedRenderer;
        var launcherItem = new ToolStripMenuItem("快捷启动器  Ctrl+Alt+Space");
        launcherItem.Click += (_, _) => LauncherPopupAction?.Invoke();
        _selfMenu.Items.Add(launcherItem);

        var entryItem = new ToolStripMenuItem("管理启动器条目");
        entryItem.Click += (_, _) => EntryEditorAction?.Invoke();
        _selfMenu.Items.Add(entryItem);

        var refreshItem = new ToolStripMenuItem("刷新文件索引");
        refreshItem.Click += (_, _) => RefreshIndexAction?.Invoke();
        _selfMenu.Items.Add(refreshItem);

        _selfMenu.Items.Add(new ToolStripSeparator());

        var closeItem = new ToolStripMenuItem("关闭悬浮按钮");
        closeItem.Click += (_, _) => Hide();
        _selfMenu.Items.Add(closeItem);

        _selfMenu.Items.Add(new ToolStripSeparator());

        var closeAllItem = new ToolStripMenuItem("关闭所有弹窗");
        closeAllItem.Click += (_, _) => CloseAllWindowsAction?.Invoke();
        _selfMenu.Opening += (_, _) =>
        {
            closeAllItem.Enabled = HasOpenWindowsFunc?.Invoke() ?? false;
        };
        _selfMenu.Items.Add(closeAllItem);

        var restartItem = new ToolStripMenuItem("重启");
        restartItem.Click += (_, _) => RestartAction?.Invoke();
        _selfMenu.Items.Add(restartItem);

        var logoutItem = new ToolStripMenuItem("退出登录");
        logoutItem.Click += (_, _) => LogoutAction?.Invoke();
        _selfMenu.Items.Add(logoutItem);

        var exitItem = new ToolStripMenuItem("退出程序");
        exitItem.Click += (_, _) => ExitAction?.Invoke();
        _selfMenu.Items.Add(exitItem);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            // WS_EX_LAYERED 必预先带上：UpdateLayeredWindow 只对已是分层窗口的窗口生效，
            // 而这里不再设 TransparencyKey（以前是它附带把这个样式加上的）。
            cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_LAYERED | WS_EX_TOPMOST;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ForceTopMost();
        _topMostTimer.Start();
        // 分层窗口的内容不经 WM_PAINT，必须在句柄建好后主动推一次，否则窗口是空的
        RenderLayered();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        // 分层模式下窗口内容由 UpdateLayeredWindow 提供，WM_PAINT 里画的东西不会显示；
        // 保留这条路径是为了抠色回退模式仍能正常出图。
        if (_layered) return;
        DrawContent(e.Graphics);
    }

    /// <summary>
    /// 把图标画到画布正中。悬停时放大 IconPad，四周预留的余量刚好接住，不会被窗体边界裁掉。
    /// </summary>
    private void DrawContent(Graphics g)
    {
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        int drawSize = (_hovered && !_dragging) ? _iconSize + IconPad : _iconSize;
        int x = (Width - drawSize) / 2;
        int y = (Height - drawSize) / 2;

        if (_appIcon is not null)
        {
            g.DrawImage(_appIcon, x, y, drawSize, drawSize);
            return;
        }

        // 图标资源缺失时的保底方案：画个主题色圆，至少让按钮可见可点
        using var brush = new SolidBrush(Color.FromArgb(47, 169, 143));
        g.FillEllipse(brush, x, y, drawSize - 1, drawSize - 1);
    }

    /// <summary>重绘按钮：分层模式走 UpdateLayeredWindow，回退模式走常规 Invalidate。</summary>
    private void RefreshVisual()
    {
        if (_layered) RenderLayered();
        else Invalidate();
    }

    /// <summary>
    /// 把当前外观渲染成一张带 alpha 的位图并推给分层窗口。
    /// 逐像素 alpha 下，图标边缘的半透明像素由系统与真实桌面背景合成，
    /// 不存在抠色方案那种“先和键色混合、再抠不掉”的黑边问题。
    /// </summary>
    private void RenderLayered()
    {
        if (!_layered || !IsHandleCreated || IsDisposed || Width <= 0 || Height <= 0) return;

        using var bmp = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            DrawContent(g);
        }

        // 推送失败（极少见，一般是 DC 资源不足）：退回抠色透明，至少按钮还在
        if (!PushLayeredBitmap(bmp)) EnableColorKeyFallback();
    }

    /// <summary>走 GDI 把位图交给 UpdateLayeredWindow，返回是否成功。</summary>
    private bool PushLayeredBitmap(Bitmap bmp)
    {
        IntPtr screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero) return false;

        IntPtr memDc = IntPtr.Zero;
        IntPtr hBitmap = IntPtr.Zero;
        IntPtr oldBitmap = IntPtr.Zero;
        try
        {
            memDc = CreateCompatibleDC(screenDc);
            if (memDc == IntPtr.Zero) return false;

            // GetHbitmap 输出的是预乘 alpha 的 DIB，正是 ULW_ALPHA 要求的格式
            hBitmap = bmp.GetHbitmap(Color.FromArgb(0));
            if (hBitmap == IntPtr.Zero) return false;
            oldBitmap = SelectObject(memDc, hBitmap);

            var size = new Size(bmp.Width, bmp.Height);
            var srcPoint = new Point(0, 0);
            var destPoint = new Point(Left, Top);
            var blend = new BLENDFUNCTION
            {
                BlendOp = AC_SRC_OVER,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = AC_SRC_ALPHA,
            };
            return UpdateLayeredWindow(Handle, screenDc, ref destPoint, ref size,
                memDc, ref srcPoint, 0, ref blend, ULW_ALPHA);
        }
        catch
        {
            return false;
        }
        finally
        {
            if (memDc != IntPtr.Zero)
            {
                if (oldBitmap != IntPtr.Zero) SelectObject(memDc, oldBitmap);
                DeleteDC(memDc);
            }
            if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    /// <summary>
    /// 分层窗口不可用时的退路：改回 TransparencyKey 抠色（会有细黑边，但按钮可见可用）。
    /// </summary>
    private void EnableColorKeyFallback()
    {
        _layered = false;
        BackColor = Color.FromArgb(1, 1, 1);
        TransparencyKey = Color.FromArgb(1, 1, 1);
        Invalidate();
    }

    /// <summary>
    /// 按目标尺寸加载内嵌的程序图标。
    /// 传目标尺寸让 Icon 直接挑 ico 内最接近的一帧，避开先解码大图再大幅缩放。
    /// </summary>
    private static Image? LoadAppIcon(int targetSize)
    {
        try
        {
            using var stream = typeof(FloatingButton).Assembly.GetManifestResourceStream("appicon.ico");
            if (stream is null) return null;
            using var icon = new Icon(stream, targetSize, targetSize);
            return icon.ToBitmap();
        }
        catch { return null; }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _currentPanel?.Close();
        _currentPanel = null;
        _hoverTimer.Dispose();
        _clickTimer.Dispose();
        _topMostTimer.Dispose();
        _panelCooldownTimer.Dispose();
        base.OnFormClosed(e);
    }

    // ==================== 鼠标交互 ====================

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left)
        {
            _dragging = true;
            _moved = false;
            _dragStart = Control.MousePosition;
            // 拖动中不做悬停放大，状态翻转后得刷一次
            RefreshVisual();
            // 注意：这里不要停 _hoverTimer，否则鼠标按下瞬间就取消悬停检测，
            // 用户稍微手抖就会让悬浮面板永远弹不出来。
            // 真正开始拖动时（_moved = true）再停。
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging) return;

        var current = Control.MousePosition;
        int dx = current.X - _dragStart.X;
        int dy = current.Y - _dragStart.Y;

        if (!_moved && (Math.Abs(dx) > 4 || Math.Abs(dy) > 4))
        {
            _moved = true;
            // 真正开始拖动时才取消悬停检测
            _hoverTimer.Stop();
            _waitingForDblClick = false;
            _clickTimer.Stop();
        }

        if (_moved)
        {
            var screen = Screen.FromControl(this).WorkingArea;
            int newX = Math.Max(screen.Left, Math.Min(Location.X + dx, screen.Right - Width));
            int newY = Math.Max(screen.Top, Math.Min(Location.Y + dy, screen.Bottom - Height));
            Location = new Point(newX, newY);
            _dragStart = current;
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left)
        {
            _dragging = false;
            // 同上：拖动结束后指针若仍在按钮上，要恢复悬停放大
            RefreshVisual();
            if (_moved) return;

            if (_waitingForDblClick)
            {
                _waitingForDblClick = false;
                _clickTimer.Stop();
                if (_panelVisible)
                {
                    _currentPanel?.Close();
                    _currentPanel = null;
                    _panelVisible = false;
                }
                DoubleClickAction?.Invoke();
            }
            else
            {
                _waitingForDblClick = true;
                _clickTimer.Start();
            }
        }
        else if (e.Button == MouseButtons.Right && !_moved)
        {
            // 右键菜单出现时关闭已展开的悬浮面板，避免菜单被面板遮挡或互相干扰
            if (_panelVisible)
            {
                _currentPanel?.Close();
                _currentPanel = null;
                _panelVisible = false;
            }
            _selfMenu.Show(PointToScreen(new Point(0, Height + 4)));
        }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hovered = true;
        _hoverTimer.Start();
        // 原来这里没有重绘，悬停放大要等到下一次偶发的重绘才生效
        RefreshVisual();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hovered = false;
        _hoverTimer.Stop();
        _waitingForDblClick = false;
        _clickTimer.Stop();
        RefreshVisual();
    }

    public void PositionAtScreenCorner(int margin = 20)
    {
        var screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        // 最右侧、垂直居中。窗体比图标大出一圈 IconPad 的透明余量，
        // 定位时把这圈余量补偿回去，保证图标本身的视觉边距仍等于 margin。
        Location = new Point(
            screen.Right - Width - margin + IconPad,
            screen.Top + (screen.Height - Height) / 2);
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        // 每次显示时重新定位到最右侧垂直居中
        if (!Visible) return;
        PositionAtScreenCorner();
        // 定位完再推一次分层内容：句柄刚建好那次推送时窗口还在默认位置，
        // 而 UpdateLayeredWindow 的目标点跟窗口位置相关，这里补上保险。
        RenderLayered();
    }

    // ==================== 面板控制 ====================

    private void ShowPanel()
    {
        // 已有面板时先关闭
        if (_currentPanel is not null)
        {
            _currentPanel.Close();
            _currentPanel = null;
        }

        _panelVisible = true;

        // 面板与按钮位置不重叠（面板在按钮左侧），两者同为 TopMost 不会互相遮挡。
        // 不停止 _topMostTimer、不改 TopMost：原来的做法在面板关闭回调失败时
        // 会导致按钮永远失去 TopMost 被其他窗口遮挡，表现为"悬浮按钮突然不见"。
        var panel = new FloatingPanel(MenuTree, OpenPageAction, OnPanelClosed);
        _currentPanel = panel;
        panel.Show();
    }

    private void OnPanelClosed()
    {
        // 防重入：面板正常关闭回调与看门狗可能并发触发
        if (!_panelVisible) return;
        _panelVisible = false;
        _currentPanel = null;
        _inPanelCooldown = true;
        _panelCooldownTimer.Start();
        if (!IsDisposed) ForceTopMost();
    }

    /// <summary>使用 Win32 API 强制置顶窗口。</summary>
    private void ForceTopMost()
    {
        if (IsHandleCreated && !IsDisposed)
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }
}

// ===================== 自定义菜单渲染器 =====================

internal sealed class ModernMenuRenderer : ToolStripProfessionalRenderer
{
    private static readonly Color MenuBg = Color.White;
    private static readonly Color ItemHover = Color.FromArgb(234, 250, 246);
    private static readonly Color ItemSelected = Color.FromArgb(47, 169, 143);
    private static readonly Color SelectedText = Color.White;
    private static readonly Color BorderColor = Color.FromArgb(220, 225, 230);

    public ModernMenuRenderer() : base(new ModernColorTable()) { }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = e.AffectedBounds;

        using var path = RoundedRect(rect, 8);
        using var brush = new SolidBrush(MenuBg);
        g.FillPath(brush, path);

        using var pen = new Pen(BorderColor, 1f);
        g.DrawPath(pen, path);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var item = e.Item;
        var rect = new Rectangle(4, 0, e.ToolStrip!.Width - 8, item.Height);

        if (item.Selected || item.Pressed)
        {
            using var path = RoundedRect(rect, 5);
            using var brush = new SolidBrush(ItemSelected);
            g.FillPath(brush, path);
        }
        else if (item.Bounds.Contains(e.ToolStrip.PointToClient(Control.MousePosition)))
        {
            using var path = RoundedRect(rect, 5);
            using var brush = new SolidBrush(ItemHover);
            g.FillPath(brush, path);
        }
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = (e.Item.Selected || e.Item.Pressed)
            ? SelectedText
            : Color.FromArgb(40, 50, 60);
        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        int y = e.Item.Height / 2;
        using var pen = new Pen(Color.FromArgb(235, 238, 241), 1f);
        e.Graphics.DrawLine(pen, 12, y, e.Item.Width - 12, y);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }

    private static GraphicsPath RoundedRect(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        int d = radius * 2;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class ModernColorTable : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground => Color.White;
    public override Color ImageMarginGradientBegin => Color.White;
    public override Color ImageMarginGradientMiddle => Color.White;
    public override Color ImageMarginGradientEnd => Color.White;
    public override Color MenuBorder => Color.FromArgb(220, 225, 230);
    public override Color MenuItemBorder => Color.Transparent;
    public override Color MenuItemSelected => Color.FromArgb(47, 169, 143);
    public override Color MenuItemSelectedGradientBegin => Color.FromArgb(234, 250, 246);
    public override Color MenuItemSelectedGradientEnd => Color.FromArgb(220, 245, 238);
    public override Color SeparatorDark => Color.FromArgb(235, 238, 241);
    public override Color SeparatorLight => Color.Transparent;
}

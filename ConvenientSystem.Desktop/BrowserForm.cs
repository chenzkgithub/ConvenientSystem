using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ConvenientSystem;

/// <summary>
/// 应用内浏览器窗口：承载独立的 WebView2，用于打开第三方链接。
/// 与主窗口共享同一 WebView2 环境（相同用户数据目录），因此 Cookie / 登录态一致，
/// 且第三方页面作为顶层文档打开（第一方 Cookie），可正常登录、跳转，
/// 避免了 iframe 内嵌导致的第三方 Cookie 被拦截、X-Frame-Options 拒绝等问题。
///
/// 使用无边框 + 自定义标题栏：
/// - 标题栏包含固定、最小化、最大化、关闭按钮（固定按钮在左侧）
/// - WndProc 处理拖拽与边缘调整大小
/// - 避免 NC paint 在 DWM 合成下不显示的问题
/// </summary>
public sealed class BrowserForm : Form, ILockable
{
    // 自定义标题栏高度（像素）
    private const int TITLEBAR_HEIGHT = 32;
    // 边缘拖拽检测区域宽度
    private const int GRIP = 6;

    // Windows 消息常量
    private const int WM_NCCALCSIZE = 0x0083;
    private const int WM_NCHITTEST  = 0x0084;
    private const int WM_GETMINMAXINFO = 0x0024;
    private const int WM_NCACTIVATE = 0x0086;

    [DllImport("user32.dll")]
    private static extern int ReleaseCapture();
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private readonly WebView2 _webView;

    // 锁屏遮罩：使用原生 LockOverlayControl 代替 WebView2，
    // 避免锁屏页加载 Vue 应用触发 API 调用（共享 localStorage 导致 401 清除全局登录态）。
    private LockOverlayControl _lockOverlay = null!;

    // 固定标题：由调用方指定（如托盘菜单名）。非空时标题保持不变，不随网页 DocumentTitle 变化。
    private string? _fixedTitle;

    // 置顶状态
    private bool _pinned;
    private readonly ToolTip _pinToolTip = new();


    // 自定义标题栏控件
    private readonly Panel _titleBar;
    private readonly Label _titleLabel;
    private readonly Panel _pinBtn;

    public BrowserForm()
    {
        Text = "加载中…";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1200, 820);
        MinimumSize = new Size(640, 480);

        // 与主程序图标保持一致
        try
        {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        catch
        {
            // 提取失败时忽略
        }

        // ── 无边框 + 自定义标题栏（避免 DWM 下 NC paint 不显示） ──
        FormBorderStyle = FormBorderStyle.None;

        // 标题栏面板（停靠在顶部，WebView2 填充下方剩余空间）
        _titleBar = new Panel
        {
            Dock = DockStyle.Top,
            Height = TITLEBAR_HEIGHT,
            BackColor = Color.White,
        };

        // 标题文字
        _titleLabel = new Label
        {
            AutoSize = false,
            Height = TITLEBAR_HEIGHT,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(10, 0, 0, 0),
            Text = "加载中…",
            ForeColor = Color.FromArgb(51, 51, 51),
            Font = new Font("Microsoft YaHei UI", 9f),
        };
        _titleLabel.MouseDown += TitleBar_MouseDown;
        _titleBar.Controls.Add(_titleLabel);

        // 窗口按钮：用 Panel 代替 Button，天然无边框，无 FlatStyle 焦点/失活边框问题。
        // Tag="winbtn" 标记按钮，供 LayoutTitleBarButtons 和 WM_NCHITTEST 识别。
        var defaultFg = Color.FromArgb(51, 51, 51);
        var defaultHover = Color.FromArgb(229, 229, 229);
        var btnFont = new Font("Segoe UI", 10f);

        Panel MakeWinBtn(string text, int width, Action onClick,
            Color? hoverBg = null, Color? hoverFg = null,
            Action<Graphics, Color>? paintIcon = null)
        {
            var p = new Panel
            {
                Width = width,
                Height = TITLEBAR_HEIGHT,
                BackColor = Color.Transparent,
                Tag = "winbtn",
            };
            var fg = defaultFg;
            var bg = hoverBg ?? defaultHover;
            p.Paint += (_, e) =>
            {
                var g = e.Graphics;
                if (p.ClientRectangle.Contains(p.PointToClient(Cursor.Position)))
                    using (var b = new SolidBrush(bg))
                        g.FillRectangle(b, p.ClientRectangle);
                if (paintIcon is not null)
                    paintIcon(g, fg);
                else
                    TextRenderer.DrawText(g, text, btnFont, p.ClientRectangle, fg,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            };
            p.MouseEnter += (_, _) => { fg = hoverFg ?? defaultFg; p.BackColor = bg; p.Invalidate(); };
            p.MouseLeave += (_, _) => { fg = defaultFg; p.BackColor = Color.Transparent; p.Invalidate(); };
            p.Click += (_, _) => onClick();
            _titleBar.Controls.Add(p);
            return p;
        }

        // 关闭按钮（GDI+ 绘制 X 图标，悬停变红）
        MakeWinBtn("", 46, Close,
            hoverBg: Color.FromArgb(232, 17, 35), hoverFg: Color.White,
            paintIcon: (g, color) =>
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using var pen = new Pen(color, 1.6f)
                {
                    StartCap = System.Drawing.Drawing2D.LineCap.Round,
                    EndCap = System.Drawing.Drawing2D.LineCap.Round,
                };
                // 14×14 X 居中于 46×32 按钮
                g.DrawLine(pen, 16, 9, 30, 23);
                g.DrawLine(pen, 30, 9, 16, 23);
            });

        // 最大化 / 还原按钮（GDI+ 绘制方框图标，最大化时显示双框还原图标）
        var maxBtn = MakeWinBtn("", 46, () =>
        {
            WindowState = WindowState == FormWindowState.Maximized
                ? FormWindowState.Normal
                : FormWindowState.Maximized;
        }, paintIcon: (g, color) =>
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var pen = new Pen(color, 1.5f);
            if (WindowState == FormWindowState.Maximized)
            {
                // 还原图标：两个错位重叠的方框（Windows 标准还原图标）
                g.DrawRectangle(pen, 19, 9, 10, 10);  // 后方
                g.DrawRectangle(pen, 16, 12, 10, 10); // 前方（覆盖后方）
            }
            else
            {
                // 最大化图标：单个方框
                g.DrawRectangle(pen, 16, 9, 14, 14);
            }
        });
        // 窗口状态变化时重绘最大化按钮（切换方框/双框图标）
        Resize += (_, _) => maxBtn.Invalidate();

        // 最小化按钮（GDI+ 绘制横线图标，圆角线帽）
        MakeWinBtn("", 46, () => WindowState = FormWindowState.Minimized,
            paintIcon: (g, color) =>
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using var pen = new Pen(color, 1.6f)
                {
                    StartCap = System.Drawing.Drawing2D.LineCap.Round,
                    EndCap = System.Drawing.Drawing2D.LineCap.Round,
                };
                // 14px 宽横线，垂直居中
                g.DrawLine(pen, 16, 16, 30, 16);
            });

        // 置顶钉子按钮（GDI+ 自绘小钉子图标）
        _pinBtn = new Panel
        {
            Width = 46,
            Height = TITLEBAR_HEIGHT,
            BackColor = Color.Transparent,
            Tag = "winbtn",
        };
        var pinFg = Color.FromArgb(120, 120, 120);
        _pinBtn.Paint += (_, e) =>
        {
            var g = e.Graphics;
            if (_pinBtn.ClientRectangle.Contains(_pinBtn.PointToClient(Cursor.Position)))
                using (var b = new SolidBrush(defaultHover))
                    g.FillRectangle(b, _pinBtn.ClientRectangle);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var cx = _pinBtn.ClientSize.Width / 2f;
            var cy = _pinBtn.ClientSize.Height / 2f;
            using var brush = new SolidBrush(pinFg);
            g.FillEllipse(brush, cx - 5, cy - 6, 10, 10);
            g.FillPolygon(brush, new PointF[]
            {
                new(cx - 2.5f, cy + 4),
                new(cx + 2.5f, cy + 4),
                new(cx, cy + 11),
            });
        };
        _pinBtn.MouseEnter += (_, _) => { _pinBtn.BackColor = defaultHover; _pinBtn.Invalidate(); };
        _pinBtn.MouseLeave += (_, _) => { _pinBtn.BackColor = Color.Transparent; _pinBtn.Invalidate(); };
        _pinBtn.Click += (_, _) =>
        {
            _pinned = !_pinned;
            TopMost = _pinned;
            pinFg = _pinned
                ? Color.FromArgb(59, 130, 246)   // 蓝色：已置顶
                : Color.FromArgb(120, 120, 120); // 灰色：未置顶
            _pinBtn.Invalidate();
            _pinToolTip.SetToolTip(_pinBtn, _pinned ? "取消置顶" : "置顶窗口");
        };
        _titleBar.Controls.Add(_pinBtn);
        _pinToolTip.SetToolTip(_pinBtn, "置顶窗口");

        Controls.Add(_titleBar);

        // WebView2 填充标题栏下方的剩余空间
        _webView = new WebView2 { Dock = DockStyle.Fill };
        Controls.Add(_webView);

        // 标题栏拖拽与双击最大化
        _titleBar.MouseMove += TitleBar_MouseMove;
        _titleBar.MouseDoubleClick += (_, e) =>
        {
            // 仅在空白区域双击时切换最大化（不覆盖按钮）
            var hit = _titleBar.GetChildAtPoint(e.Location);
            if (e.Button == MouseButtons.Left && hit?.Tag?.ToString() != "winbtn")
                WindowState = WindowState == FormWindowState.Maximized
                    ? FormWindowState.Normal
                    : FormWindowState.Maximized;
        };

        // 布局标题栏内按钮位置
        LayoutTitleBarButtons();
        _titleBar.Resize += (_, _) => LayoutTitleBarButtons();

        // 窗口关闭时从锁屏协调器注销，避免协调器持有已释放窗口。
        FormClosed += (_, _) => LockCoordinator.Unregister(this);
    }

    /// <summary>排列标题栏内按钮位置：从左到右为钉子→(空白)→最小化→最大化→关闭</summary>
    private void LayoutTitleBarButtons()
    {
        // 按钮从右侧开始排列（关闭→最大化→最小化）
        var x = _titleBar.ClientSize.Width;
        foreach (Control c in _titleBar.Controls)
        {
            if (c.Tag?.ToString() == "winbtn")
            {
                x -= c.Width;
                c.Location = new Point(x, 0);
            }
        }
        // 标题文字区域：左侧边距 到 最左侧按钮左边
        _titleLabel.Width = Math.Max(0, x - 8);
        _titleLabel.Location = new Point(8, 0);
    }

    /// <summary>标题栏拖拽：点击空白区域可拖动窗口</summary>
    private void TitleBar_MouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        ReleaseCapture();
        SendMessage(Handle, 0x0112, (IntPtr)0xF012, IntPtr.Zero); // WM_SYSCOMMAND + SC_MOVE
    }

    /// <summary>标题栏鼠标移动：根据位置显示调整大小光标</summary>
    private void TitleBar_MouseMove(object? sender, MouseEventArgs e)
    {
        if (WindowState == FormWindowState.Maximized) return;
        Cursor = e.X <= GRIP ? Cursors.SizeNWSE : Cursors.Default;
    }

    /// <summary>添加 WS_THICKFRAME 样式使无边框窗口可调整大小</summary>
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.Style |= 0x00040000; // WS_THICKFRAME
            return cp;
        }
    }

    protected override void WndProc(ref Message m)
    {
        // 无边框自定义标题栏：阻止 Windows 绘制非活跃外观。
        // 窗口失去焦点时（如点击其他窗口、钉子置顶后切换窗口），
        // WM_NCACTIVATE 会让整个窗口色调变灰，影响自定义标题栏背景色。
        // 返回 1 表示"已处理"，阻止默认的非活跃视觉变化，标题栏始终保持活跃外观。
        if (m.Msg == WM_NCACTIVATE)
        {
            m.Result = (IntPtr)1;
            return;
        }

        // WS_THICKFRAME 提供 resize 能力，但会产生可见粗边框；
        // 将 rgrc[0] 向内缩进 GRIP 像素，保留一圈不可见的 NC 区域供 Windows 做 resize 检测，
        // 同时视觉上无边框。直接返回 0 会消除整个 NC 区域，导致边缘拖拽缩放失效。
        if (m.Msg == WM_NCCALCSIZE && m.WParam != IntPtr.Zero)
        {
            var rc = Marshal.PtrToStructure<RECT>(m.LParam);
            rc.left   += GRIP;
            rc.top    += GRIP;
            rc.right  -= GRIP;
            rc.bottom -= GRIP;
            Marshal.StructureToPtr(rc, m.LParam, false);
            m.Result = IntPtr.Zero;
            return;
        }

        // 边缘与标题栏拖拽区域检测（非最大化时启用调整大小）
        if (m.Msg == WM_NCHITTEST && WindowState != FormWindowState.Maximized)
        {
            base.WndProc(ref m);
            var lp = (int)m.LParam;
            var sx = (short)(lp & 0xFFFF);
            var sy = (short)((lp >> 16) & 0xFFFF);
            var pt = PointToClient(new Point(sx, sy));
            var w = ClientSize.Width;
            var h = ClientSize.Height;

            // 四角优先（对角线调整）
            if (pt.X <= GRIP && pt.Y <= GRIP) { m.Result = (IntPtr)13; return; } // HTTOPLEFT
            if (pt.X >= w - GRIP && pt.Y <= GRIP) { m.Result = (IntPtr)14; return; } // HTTOPRIGHT
            if (pt.X <= GRIP && pt.Y >= h - GRIP) { m.Result = (IntPtr)16; return; } // HTBOTTOMLEFT
            if (pt.X >= w - GRIP && pt.Y >= h - GRIP) { m.Result = (IntPtr)17; return; } // HTBOTTOMRIGHT

            // 四边
            if (pt.X <= GRIP) { m.Result = (IntPtr)10; return; } // HTLEFT
            if (pt.X >= w - GRIP) { m.Result = (IntPtr)11; return; } // HTRIGHT
            if (pt.Y <= GRIP) { m.Result = (IntPtr)12; return; } // HTTOP
            if (pt.Y >= h - GRIP) { m.Result = (IntPtr)15; return; } // HTBOTTOM

            // 标题栏空白区域（非按钮区域）允许拖拽窗口
            if (pt.Y <= TITLEBAR_HEIGHT)
            {
                var hit = _titleBar.GetChildAtPoint(new Point(pt.X, pt.Y));
                if (hit?.Tag?.ToString() != "winbtn")
                {
                    m.Result = (IntPtr)2; // HTCAPTION
                    return;
                }
            }
            return;
        }

        // 强制窗口最小尺寸（无边框时系统不自动处理）
        if (m.Msg == WM_GETMINMAXINFO)
        {
            base.WndProc(ref m);
            var mmi = Marshal.PtrToStructure<MINMAXINFO>(m.LParam);
            mmi.ptMinTrackSize = new Point(MinimumSize.Width, MinimumSize.Height);
            Marshal.StructureToPtr(mmi, m.LParam, true);
            return;
        }

        base.WndProc(ref m);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left, top, right, bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public Point ptReserved;
        public Point ptMaxSize;
        public Point ptMaxPosition;
        public Point ptMinTrackSize;
        public Point ptMaxTrackSize;
    }

    /// <summary>最大化时限制在工作区域（避免无边框窗口覆盖任务栏）</summary>
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState == FormWindowState.Maximized)
        {
            var wa = Screen.FromHandle(Handle).WorkingArea;
            Bounds = wa;
        }
    }

    /// <summary>内核实例，供调用方把新窗口交给引擎自动加载目标地址（赋值给 e.NewWindow）。</summary>
    public CoreWebView2 Core => _webView.CoreWebView2;

    /// <summary>
    /// 按当前屏幕工作区的 80% 居中摆放窗口（默认打开尺寸，不最大化）。
    /// 随分辨率自适应：大屏更大、小屏更小，且始终不超出工作区。
    /// </summary>
    public void SizeToWorkingArea()
    {
        var wa = Screen.FromHandle(Handle).WorkingArea;
        int w = (int)(wa.Width * 0.8);
        int h = (int)(wa.Height * 0.8);
        StartPosition = FormStartPosition.Manual;
        Bounds = new Rectangle(
            wa.X + (wa.Width - w) / 2,
            wa.Y + (wa.Height - h) / 2,
            w, h);
    }

    /// <summary>设置固定窗口标题：设置后标题保持为该值，不再跟随网页标题。</summary>
    public void SetFixedTitle(string title)
    {
        _fixedTitle = title;
        _titleLabel.Text = Text = title;
    }

    /// <summary>
    /// 使用共享环境初始化内核。必须在把本窗口交给 e.NewWindow 之前完成。
    /// </summary>
    public async Task InitializeAsync(CoreWebView2Environment env)
    {
        await _webView.EnsureCoreWebView2Async(env);

        var core = _webView.CoreWebView2;

        // 浏览器化体验：保留右键菜单（含前进/后退/刷新）与状态栏
        core.Settings.AreDefaultContextMenusEnabled = true;
        core.Settings.IsStatusBarEnabled = true;
#if DEBUG
        core.Settings.AreDevToolsEnabled = true;
#else
        core.Settings.AreDevToolsEnabled = false;
#endif

        // 窗口标题跟随网页标题（若已设置固定标题则保持不变）
        core.DocumentTitleChanged += (_, _) =>
        {
            if (!string.IsNullOrEmpty(_fixedTitle)) return;
            var title = string.IsNullOrWhiteSpace(core.DocumentTitle) ? "浏览器" : core.DocumentTitle;
            Text = title;
            _titleLabel.Text = title;
        };

        // 页面内再次弹窗（window.open / target=_blank）继续在应用内新窗口打开
        core.NewWindowRequested += OnNewWindowRequested;

        // 放行 dingtalk:// 协议，支持钉钉客户端一键授权登录。
        ExternalUriSchemePolicy.Attach(core);

        // 网页请求关闭窗口（window.close）时关闭本窗体
        core.WindowCloseRequested += (_, _) =>
        {
            if (!IsDisposed) Close();
        };

        // 用户在本窗口内的操作不会传到主页面，为避免其“正在使用却被自动锁屏”，
        // 注入活动探测脚本，检测到输入后经协调器转发给主页面重置空闲计时。
        // 同时接收文件操作消息（代码编辑器等独立窗口功能），共享 MainForm 的处理逻辑。
        core.WebMessageReceived += OnMessageReceived;
        await core.AddScriptToExecuteOnDocumentCreatedAsync(ActivityScript);

        // 同步初始化锁屏遮罩：使用原生控件，不加载 Vue 应用，避免任何 API 调用。
        InitializeLockOverlay();

        // 注册到锁屏协调器；若此刻已处于锁屏，会立即对本窗口上锁。
        LockCoordinator.Register(this);
    }

    /// <summary>
    /// 从 WebView2 的 localStorage 读取 JWT，供锁屏密码校验接口携带认证信息。
    /// </summary>
    private async Task<string?> ReadJwtFromWebViewAsync()
    {
        try
        {
            var json = await _webView.CoreWebView2.ExecuteScriptAsync(
                "(() => { try { const s = localStorage.getItem('auth_state_v1'); if (!s) return null; const o = JSON.parse(s); return o?.token || null; } catch { return null; } })()");
            // ExecuteScriptAsync 返回 JSON 字符串（带引号），需要反序列化
            return System.Text.Json.JsonSerializer.Deserialize<string>(json);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 锁屏密码校验：从 WebView2 读取 JWT 后调用 LockCoordinator.VerifyAsync，
    /// 使请求携带认证信息，避免后端策略收紧时解锁失败。
    /// </summary>
    private async Task<bool> VerifyLockPasswordAsync(string password)
    {
        var jwt = await ReadJwtFromWebViewAsync();
        return await LockCoordinator.VerifyAsync(password, jwt);
    }

    /// <summary>
    /// 初始化锁屏遮罩：使用原生 LockOverlayControl，密码校验通过 LockCoordinator 完成。
    /// 不加载 Vue 应用，不发起任何 API 调用，彻底避免锁屏页清除全局登录态。
    /// </summary>
    private void InitializeLockOverlay()
    {
        _lockOverlay = new LockOverlayControl
        {
            Dock = DockStyle.Fill,
            Visible = false, // 平时隐藏，ShowLock 时显示并 BringToFront
        };
        _lockOverlay.VerifyAsync = VerifyLockPasswordAsync;
        _lockOverlay.Unlocked += () => LockCoordinator.UnlockAll(notifyWeb: true);
        Controls.Add(_lockOverlay);
        // 初始状态：锁屏遮罩不可见，主页面在最前面
        _lockOverlay.SendToBack();
        _webView.BringToFront();
    }

    // 注入到网页的活动探测脚本：监听输入事件（节流 1s），通过 chrome.webview.postMessage 上报给宿主。
    private const string ActivityScript = """
        (function () {
          try {
            var last = 0;
            function ping() {
              var now = Date.now();
              if (now - last < 1000) return;
              last = now;
              if (window.chrome && window.chrome.webview) {
                window.chrome.webview.postMessage({ type: 'embed:activity' });
              }
            }
            var evts = ['mousemove', 'keydown', 'mousedown', 'wheel', 'scroll', 'touchstart'];
            for (var i = 0; i < evts.length; i++) {
              window.addEventListener(evts[i], ping, { capture: true, passive: true });
            }
          } catch (e) {}
        })();
        """;

    /// <summary>处理本窗口的网页消息：活动上报（防锁屏）+ 文件操作（代码编辑器等独立窗口功能）。</summary>
    private void OnMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;

            // 活动上报：直接转发给协调器（原生锁屏遮罩不会产生活动消息）
            if (root.TryGetProperty("type", out var t) && t.GetString() == "embed:activity")
            {
                LockCoordinator.NotifyActivity();
                return;
            }

            // 文件操作：与 MainForm 共享同一处理逻辑，确保独立窗口中也能正常保存
            HostFileService.TryHandleMessage(root, _webView.CoreWebView2!, this);
        }
        catch
        {
            // 消息异常时忽略
        }
    }

    /// <summary>
    /// 显示锁屏遮罩：通过 z-order 将原生锁屏控件提到最前。
    /// </summary>
    public void ShowLock()
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(ShowLock); return; }

        _lockOverlay.Visible = true;
        _lockOverlay.BringToFront();
        _lockOverlay.ResetAndFocus();
    }

    /// <summary>隐藏锁屏遮罩：通过 z-order 将主页面恢复到最前。</summary>
    public void HideLock()
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(HideLock); return; }

        _webView.BringToFront();
        _lockOverlay.Visible = false;
        _lockOverlay.SendToBack();
    }



    /// <summary>窗口重新可见时强制刷新布局，避免隐藏/显示周期后标题栏与 WebView2 尺寸异常。</summary>
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible) ForceLayoutRefresh();
    }

    /// <summary>强制重新计算标题栏按钮位置与 WebView2 尺寸。</summary>
    private void ForceLayoutRefresh()
    {
        if (IsDisposed || !IsHandleCreated) return;
        LayoutTitleBarButtons();
        if (WindowState == FormWindowState.Maximized)
        {
            // 最大化时直接修正 Bounds（会触发 OnResize 重新布局），跳过 PerformLayout 避免重复计算
            var wa = Screen.FromHandle(Handle).WorkingArea;
            Bounds = wa;
        }
        else
        {
            PerformLayout();
        }
        _webView.Invalidate();
        Invalidate();
    }

    /// <summary>页面内新弹窗：继续在应用内浏览器窗口打开（共享同一环境）。</summary>
    private async void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        var env = _webView.CoreWebView2.Environment;
        var deferral = e.GetDeferral();
        try
        {
            var child = new BrowserForm();
            await child.InitializeAsync(env);
            e.NewWindow = child.Core;
            e.Handled = true;
            child.Show();
            child.SizeToWorkingArea();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[BrowserForm] 打开新窗口失败: {ex.Message}");
        }
        finally
        {
            deferral.Complete();
        }
    }
}

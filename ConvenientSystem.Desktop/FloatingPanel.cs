using System.Drawing.Drawing2D;
using System.Text.Json;

namespace ConvenientSystem;

/// <summary>
/// 悬浮按钮的"平铺全部展开"菜单面板：仿首页卡片网格布局，
/// 按顶层菜单分组展示所有页面（float=true 的菜单项），点击卡片打开独立窗口。
/// </summary>
internal sealed class FloatingPanel : Form
{
    // 颜色主题（与 ModernMenuRenderer 一致）
    private static readonly Color PanelBg = Color.White;
    private static readonly Color GroupTitleColor = Color.FromArgb(47, 169, 143);
    private static readonly Color CardBg = Color.FromArgb(252, 253, 254);
    private static readonly Color CardHoverBg = Color.FromArgb(240, 251, 248);
    private static readonly Color CardBorderColor = Color.FromArgb(230, 235, 242);
    private static readonly Color CardText = Color.FromArgb(40, 50, 60);
    private static readonly Color DividerColor = Color.FromArgb(238, 242, 246);

    // 96 DPI 下的设计基准尺寸；实际用的是构造时按 DPI 换算过的实例字段。
    // 面板全程自绘、尺寸都是写死的像素，而字体用的是磅值（随 DPI 自动放大），
    // 若尺寸不跟着换算，125%/150% 缩放下文字会挤出卡片、标题栏被压扁。
    private const int BaseCardWidth = 124;
    private const int BaseCardHeight = 44;
    private const int BaseCardGap = 5;
    private const int BaseColumnGap = 10;
    private const int BaseIconSize = 22;
    private const int BaseHeaderHeight = 28;
    private const int BasePadding = 10;
    private const int BaseContentTop = 8;
    private const int BaseCardRadius = 6;
    private const int BaseScrollBarWidth = 6;
    private const int BasePanelMinWidth = 320;

    private readonly float _dpiScale;
    private readonly int _cardWidth;
    private readonly int _cardHeight;
    private readonly int _cardGap;
    private readonly int _columnGap;
    private readonly int _iconSize;
    private readonly int _headerHeight;
    private readonly int _padding;
    private readonly int _contentTop;
    private readonly int _cardRadius;
    private readonly int _scrollBarWidth;
    private readonly int _scrollBarMargin;

    private readonly Panel _fixedHeader;
    private readonly Panel _scroll;
    private readonly Panel _content;
    private readonly Action<string, string, bool>? _openPage;
    private readonly Action? _onClose;

    // 列与列之间分隔线的 X 坐标（相对 _content），由 BuildCards 填充
    private readonly List<int> _dividerX = new();

    // 鼠标离开检测：轮询方式比 OnMouseLeave 事件更可靠（子控件多时事件易丢失）
    private readonly System.Windows.Forms.Timer _hoverCheckTimer;
    // 延迟关闭定时器：鼠标短暂离开（穿过面板与按钮之间的间隙）时不立即关闭
    private readonly System.Windows.Forms.Timer _closeDelayTimer;
    private Form? _ownerButton;

    // 卡片悬停高亮动画：只做颜色过渡，不做缩放也不做位移。
    // 用 ScaleTransform 放大卡片会让文字与图标被 GDI+ 重采样而发虚，
    // 改成纯颜色插值后文字全程 1:1 渲染，任何状态下都是锐利的。
    private const float CardAnimSpeed = 0.35f; // 动画速度（越大越快）
    private const float CardAnimThreshold = 0.01f; // 动画结束判定阈值
    private readonly Dictionary<Panel, float> _cardHover = new();
    private readonly Dictionary<Panel, float> _cardHoverTarget = new();
    private readonly System.Windows.Forms.Timer _animTimer;

    // 自绘细滚动条（替代 WinForms 原生粗滚动条，约 17px 宽会破坏视觉风格）
    private int _scrollValue;
    private int _scrollMax;
    private bool _sbDragging;
    private int _sbDragOffsetY;

    // 防止 ClosePanel 被重复调用（OnDeactivate 与 BeginInvoke 可能并发）
    private bool _closing;
    // 面板是否已就绪（OnShown 后才允许因失焦而关闭，避免刚显示就被误判关闭）
    private bool _ready;
    // 关闭回调是否已调用（OnFormClosed 保底 + ClosePanel 异常保底，防止重复）
    private bool _closeCallbackInvoked;

    /// <summary>把 96 DPI 基准值换算成当前 DPI 下的实际像素。</summary>
    private int S(int baseValue) => Math.Max(1, (int)Math.Round(baseValue * _dpiScale));

    /// <summary>系统菜单字体族，与托盘菜单保持一致的字形。</summary>
    private static FontFamily MenuFontFamily =>
        SystemFonts.MenuFont?.FontFamily ?? SystemFonts.DefaultFont.FontFamily;

    public FloatingPanel(JsonElement? menuTree, Action<string, string, bool>? openPage, Action? onClose)
    {
        _openPage = openPage;
        _onClose = onClose;

        // DPI 换算基准。窗口 DPI 在进程内是固定的（HighDpiMode 为默认的 SystemAware），
        // 面板又是每次弹出时新建的，所以一次性算好即可，无需处理 DpiChanged。
        _dpiScale = DeviceDpi > 0 ? DeviceDpi / 96f : 1f;
        _cardWidth = S(BaseCardWidth);
        _cardHeight = S(BaseCardHeight);
        _cardGap = S(BaseCardGap);
        _columnGap = S(BaseColumnGap);
        _iconSize = S(BaseIconSize);
        _headerHeight = S(BaseHeaderHeight);
        _padding = S(BasePadding);
        _contentTop = S(BaseContentTop);
        _cardRadius = S(BaseCardRadius);
        _scrollBarWidth = S(BaseScrollBarWidth);
        _scrollBarMargin = S(3);

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        // 不用 TransparencyKey 抠色透明：抠色透明的像素是 click-through 的，
        // 鼠标穿过透明区域会命中底层窗口导致面板误判失焦关闭再重开，表现为抖动。
        // 改用 Region 圆角裁剪 + 不透明背景，圆角外区域不属于窗口，不会收到鼠标事件。
        BackColor = PanelBg;

        // 顶部固定分组标题栏（不随滚动移动）
        _fixedHeader = new Panel
        {
            Dock = DockStyle.Top,
            Height = _headerHeight,
            BackColor = PanelBg,
        };
        _fixedHeader.Paint += (s, e) =>
        {
            // 底部分割线
            using var pen = new Pen(DividerColor, 1f);
            e.Graphics.DrawLine(pen, 0, _fixedHeader.Height - 1, _fixedHeader.Width, _fixedHeader.Height - 1);
        };

        // 下方滚动容器（只有卡片区域）
        // 注意：不使用 AutoScroll，改用手动的 _scrollValue + 自绘细滚动条，
        // 避免 WinForms 原生粗滚动条（约 17px 宽）破坏视觉风格。
        // 内边距不用 Padding 属性：手动设 Location 的子控件不受父 Padding 影响，
        // 留白改由 _content 的初始 Location（_padding, _contentTop）实现。
        _scroll = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = PanelBg,
        };
        _content = new Panel
        {
            BackColor = PanelBg,
            Location = new Point(_padding, _contentTop),
        };
        _content.Paint += (_, e) => PaintColumnDividers(e.Graphics);
        _scroll.Controls.Add(_content);

        Controls.Add(_scroll);
        Controls.Add(_fixedHeader);

        // 自绘细滚动条相关事件
        MouseWheel += OnMouseWheelScroll;
        _fixedHeader.MouseWheel += OnMouseWheelScroll;
        _scroll.MouseWheel += OnMouseWheelScroll;
        _content.MouseWheel += OnMouseWheelScroll;

        // 卡片悬停高亮动画定时器（约 60fps）
        _animTimer = new System.Windows.Forms.Timer { Interval = 16 };
        _animTimer.Tick += (_, _) => DoAnimationTick();

        // 鼠标离开检测定时器（100ms 轮询一次，比 OnMouseLeave 事件更可靠）
        _hoverCheckTimer = new System.Windows.Forms.Timer { Interval = 100 };
        _hoverCheckTimer.Tick += (_, _) => CheckMouseAndCloseIfOutside();

        // 延迟关闭定时器：鼠标离开面板+按钮区域后等待 200ms 再关闭，
        // 给鼠标穿过两者之间间隙的时间，避免面板反复弹出/关闭导致抖动。
        _closeDelayTimer = new System.Windows.Forms.Timer { Interval = 200 };
        _closeDelayTimer.Tick += (_, _) =>
        {
            _closeDelayTimer.Stop();
            if (!_ready || _closing) return;
            var sp = MousePosition;
            if (Bounds.Contains(sp)) return;
            if (_ownerButton is not null && !_ownerButton.IsDisposed && _ownerButton.Bounds.Contains(sp)) return;
            ClosePanel();
        };

        var layout = BuildCards(menuTree);
        ResizeToFit(layout);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            // WS_EX_TOPMOST (0x8) + WS_EX_TOOLWINDOW (0x80) + WS_EX_NOACTIVATE (0x08000000)；
            // WS_EX_TOOLWINDOW 让面板不出现在任务栏/Alt+Tab 列表。
            // WS_EX_NOACTIVATE 让面板不抢焦点，避免与悬浮按钮/其他窗口的焦点争夺导致抖动；
            // 面板关闭改由 _hoverCheckTimer + _closeDelayTimer 的鼠标位置轮询驱动，不依赖 OnDeactivate。
            cp.ExStyle |= 0x00000008 | 0x00000080 | 0x08000000;
            return cp;
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // 不调用 Activate()：面板带 WS_EX_NOACTIVATE 不抢焦点，
        // 避免与悬浮按钮/其他窗口的焦点争夺导致抖动。
        // 布局引擎跑完后标记就绪，并启动鼠标离开检测
        BeginInvoke(new Action(() =>
        {
            _ready = true;
            _ownerButton = FindFloatingButtonOwner();
            _hoverCheckTimer.Start();
        }));
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        // WS_EX_NOACTIVATE 下面板通常不会被激活也不会触发 OnDeactivate，
        // 但作为兜底：如果确实触发了，走延迟关闭而非立即关闭。
        if (_ready && !_closing && !_closeDelayTimer.Enabled) _closeDelayTimer.Start();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        // 立即检查一次（定时器还会继续轮询作为兑底）
        CheckMouseAndCloseIfOutside();
    }

    /// <summary>
    /// 检查鼠标是否仍在面板或悬浮按钮范围内，若已离开则关闭面板。
    /// 用轮询代替纯事件，避免子控件多时 MouseLeave 丢失。
    /// </summary>
    private void CheckMouseAndCloseIfOutside()
    {
        if (!_ready || _closing) return;
        var screenPos = MousePosition;
        var panelRect = Bounds;
        // 在面板内：取消延迟关闭
        if (panelRect.Contains(screenPos)) { _closeDelayTimer.Stop(); return; }
        // 在悬浮按钮内：取消延迟关闭（用户可能正从面板移回按钮）
        if (_ownerButton is not null && !_ownerButton.IsDisposed && _ownerButton.Bounds.Contains(screenPos))
        {
            _closeDelayTimer.Stop();
            return;
        }
        // 已离开两者范围：启动延迟关闭（不立即关闭，给鼠标穿过间隙的时间）
        if (!_closeDelayTimer.Enabled) _closeDelayTimer.Start();
    }

    private void ClosePanel()
    {
        if (_closing || IsDisposed || !IsHandleCreated) return;
        _closing = true;
        try
        {
            Close(); // Close() 同步触发 OnFormClosed，在其中保底调用 _onClose
        }
        catch
        {
            // Close() 抛异常时 OnFormClosed 不会触发，finally 中保底回调
        }
        finally
        {
            InvokeCloseCallback();
        }
    }

    /// <summary>保底调用关闭回调，防止重复调用。</summary>
    private void InvokeCloseCallback()
    {
        if (_closeCallbackInvoked) return;
        _closeCallbackInvoked = true;
        try { _onClose?.Invoke(); } catch { }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _hoverCheckTimer.Stop();
        _hoverCheckTimer.Dispose();
        _closeDelayTimer.Stop();
        _closeDelayTimer.Dispose();
        _animTimer.Stop();
        _animTimer.Dispose();
        // 保底：无论面板通过什么路径关闭，都通知按钮恢复状态。
        // ClosePanel() 中 Close() 抛异常时这条路径是唯一的回调机会。
        InvokeCloseCallback();
        base.OnFormClosed(e);
    }

    // ============ 卡片构建 ============

    /// <summary>
    /// 构建分组标题与卡片网格，返回卡片区域的实测尺寸供 ResizeToFit 直接用。
    /// 返回尺寸而不让 ResizeToFit 自己重算一遍，是为了避免两处统计口径不一致。
    /// </summary>
    private (int width, int height) BuildCards(JsonElement? itemsEl)
    {
        _content.Controls.Clear();
        _fixedHeader.Controls.Clear();
        _dividerX.Clear();

        if (itemsEl is not { ValueKind: JsonValueKind.Array } arr)
        {
            AddGroupHeader(_content, "（无可用菜单）");
            return (_cardWidth, _cardHeight);
        }

        // 收集所有分组数据
        var groupsData = new List<(string title, List<(string t, string p, bool e)> leaves)>();
        // 没有子菜单的顶级项统一放入“一级菜单”分组
        var topLevelLeaves = new List<(string, string, bool)>();

        foreach (var group in arr.EnumerateArray())
        {
            string groupTitle = group.TryGetProperty("title", out var t) ? (t.GetString() ?? "(未命名)") : "(未命名)";

            bool hasChildren = group.TryGetProperty("children", out var childrenEl)
                && childrenEl.ValueKind == JsonValueKind.Array
                && childrenEl.GetArrayLength() > 0;

            if (hasChildren)
            {
                // 有子菜单：递归收集该分组下所有悬浮叶子
                var leaves = new List<(string, string, bool)>();
                CollectLeaves(group, leaves);
                if (leaves.Count > 0)
                {
                    groupsData.Add((groupTitle, leaves));
                }
            }
            else
            {
                // 无子菜单：顶级项本身作为悬浮卡片，放入“一级菜单”分组
                bool isFloat = group.TryGetProperty("float", out var f) && f.ValueKind == JsonValueKind.True;
                string page = group.TryGetProperty("page", out var p) && p.ValueKind == JsonValueKind.String
                    ? (p.GetString() ?? string.Empty) : string.Empty;
                bool external = group.TryGetProperty("external", out var ext) && ext.ValueKind == JsonValueKind.True;
                if (isFloat && !string.IsNullOrEmpty(page))
                {
                    topLevelLeaves.Add((groupTitle, page, external));
                }
            }
        }

        // “一级菜单”分组插入到最前（用户最常用）
        if (topLevelLeaves.Count > 0)
        {
            groupsData.Insert(0, ("一级菜单", topLevelLeaves));
        }

        if (groupsData.Count == 0)
        {
            AddGroupHeader(_content, "（无悬浮菜单项）");
            return (_cardWidth, _cardHeight);
        }

        int groupCount = groupsData.Count;
        int columnWidth = _cardWidth + _columnGap;
        int maxRows = groupsData.Max(g => g.leaves.Count);

        // 顶部固定标题栏：每个分组标题对齐到自己那一列的左边缘
        foreach (int i in Enumerable.Range(0, groupCount))
        {
            var header = AddGroupHeader(_fixedHeader, groupsData[i].title);
            header.Location = new Point(_padding + i * columnWidth, 0);
        }

        // 下方卡片网格：所有卡片平铺在 _content 中，按 (groupIndex, rowIndex) 定位
        foreach (int i in Enumerable.Range(0, groupCount))
        {
            var (title, leaves) = groupsData[i];
            int colX = i * columnWidth;
            // 分隔线落在两列间隙正中间
            if (i > 0) _dividerX.Add(colX - _columnGap / 2);
            for (int r = 0; r < leaves.Count; r++)
            {
                var (t, p, ext) = leaves[r];
                var card = new Panel
                {
                    Width = _cardWidth,
                    Height = _cardHeight,
                    Location = new Point(colX, r * (_cardHeight + _cardGap)),
                    Cursor = Cursors.Hand,
                    // 卡片底色给面板白：圆角之外的四个角露出的是面板背景而不是灰块
                    BackColor = PanelBg,
                };
                // 卡片双缓冲：防止悬停颜色过渡时闪烁
                typeof(Panel).InvokeMember("DoubleBuffered",
                    System.Reflection.BindingFlags.SetProperty | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                    null, card, new object[] { true });
                typeof(Panel).InvokeMember("SetStyle",
                    System.Reflection.BindingFlags.InvokeMethod | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                    null, card, new object[] { ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true });

                _cardHover[card] = 0f;
                _cardHoverTarget[card] = 0f;
                card.Paint += (s, e) => PaintCard(card, e.Graphics, t);
                card.MouseEnter += (_, _) =>
                {
                    _cardHoverTarget[card] = 1f;
                    _animTimer.Start();
                };
                card.MouseLeave += (_, _) =>
                {
                    _cardHoverTarget[card] = 0f;
                    _animTimer.Start();
                };
                card.Click += (_, _) =>
                {
                    try { _openPage?.Invoke(p, t, ext); } catch { /* ignore */ }
                    ClosePanel();
                };
                _content.Controls.Add(card);
            }
        }

        // 末列不留列间距、末行不留行间距，避免面板右下多出一圈空白
        int width = groupCount * columnWidth - _columnGap;
        int height = maxRows * (_cardHeight + _cardGap) - _cardGap;
        _content.Size = new Size(width, height);
        return (width, height);
    }

    /// <summary>
    /// 画列与列之间的 1px 竖分隔线。
    /// 画在 _content 而不是 _scroll 上：背景透明的子控件只会继承父控件的背景填充，
    /// 拿不到父控件 Paint 里的绘制结果。竖线均匀且贯通全高，随内容一起垂直平移也看不出差别。
    /// </summary>
    private void PaintColumnDividers(Graphics g)
    {
        if (_dividerX.Count == 0) return;
        int h = Math.Max(_content.Height, _scroll.ClientSize.Height);
        using var pen = new Pen(DividerColor, 1f);
        foreach (int x in _dividerX)
            g.DrawLine(pen, x, 0, x, h);
    }

    private static void CollectLeaves(JsonElement node, List<(string title, string page, bool external)> leaves)
    {
        bool isFloat = node.TryGetProperty("float", out var f) && f.ValueKind == JsonValueKind.True;
        string title = node.TryGetProperty("title", out var t) ? (t.GetString() ?? string.Empty) : string.Empty;
        string page = node.TryGetProperty("page", out var p) && p.ValueKind == JsonValueKind.String
            ? (p.GetString() ?? string.Empty) : string.Empty;
        bool external = node.TryGetProperty("external", out var ext) && ext.ValueKind == JsonValueKind.True;

        if (node.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array && children.GetArrayLength() > 0)
        {
            foreach (var child in children.EnumerateArray())
                CollectLeaves(child, leaves);
        }
        else if (!string.IsNullOrEmpty(page) && isFloat)
        {
            leaves.Add((title, page, external));
        }
    }

    /// <summary>
    /// 分组标题：左侧一根主题色小竖条 + 标题文字，整体作为一列的表头。
    /// 用自绘 Panel 而不是 Label，是为了让竖条与文字用同一套按 DPI 换算过的坐标。
    /// </summary>
    private Panel AddGroupHeader(Control container, string text)
    {
        var host = new Panel
        {
            Width = _cardWidth,
            Height = _headerHeight,
            BackColor = PanelBg,
        };
        host.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int barW = S(2);
            int barH = S(12);
            int barX = S(2);
            int barY = (host.Height - barH) / 2;
            using (var barBrush = new SolidBrush(GroupTitleColor))
            using (var barPath = RoundedRect(new Rectangle(barX, barY, barW, barH), Math.Max(1, barW / 2)))
            {
                g.FillPath(barBrush, barPath);
            }

            int textLeft = barX + barW + S(5);
            var textRect = new Rectangle(textLeft, 0, host.Width - textLeft, host.Height);
            using var font = new Font(MenuFontFamily, 9.5f, FontStyle.Bold);
            TextRenderer.DrawText(g, text, font, textRect, GroupTitleColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        };
        container.Controls.Add(host);
        return host;
    }

    /// <summary>两个颜色之间线性插值，t 为 0~1 的过渡进度。</summary>
    private static Color Lerp(Color from, Color to, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return Color.FromArgb(
            from.R + (int)((to.R - from.R) * t),
            from.G + (int)((to.G - from.G) * t),
            from.B + (int)((to.B - from.B) * t));
    }

    /// <summary>
    /// 卡片绘制：背景与边框在默认色/悬停色之间插值，尺寸与位置始终不变。
    /// 不用 ScaleTransform：非整数倍缩放会让 GDI+ 重采样文字与圆形图标，必然发虚。
    /// </summary>
    private void PaintCard(Panel card, Graphics g, string title)
    {
        float progress = _cardHover.TryGetValue(card, out var v) ? v : 0f;
        var bg = Lerp(CardBg, CardHoverBg, progress);
        var border = Lerp(CardBorderColor, GroupTitleColor, progress);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        // 卡片底（减 1 避免右/下边缘被相邻像素干扰）
        var cardRect = new Rectangle(0, 0, card.Width - 1, card.Height - 1);
        using (var path = RoundedRect(cardRect, _cardRadius))
        using (var brush = new SolidBrush(bg))
        {
            g.FillPath(brush, path);
        }
        // 边框：先关掉平滑处理，让 1px 线落在整像素上，不会出现发虚的灰边
        using (var path = RoundedRect(cardRect, _cardRadius))
        using (var pen = new Pen(border, 1f))
        {
            g.SmoothingMode = SmoothingMode.None;
            g.DrawPath(pen, path);
            g.SmoothingMode = SmoothingMode.AntiAlias;
        }

        // 左侧首字母图标圆：主题色垂直渐变
        var initial = (title ?? "?").Trim();
        if (initial.Length > 0) initial = initial[0].ToString();
        int cx = S(8);
        int cy = (card.Height - _iconSize) / 2;
        var iconRect = new Rectangle(cx, cy, _iconSize, _iconSize);
        using (var iconBrush = new LinearGradientBrush(
            new Rectangle(iconRect.X, iconRect.Y, iconRect.Width, iconRect.Height + 1),
            Color.FromArgb(63, 191, 163), GroupTitleColor, LinearGradientMode.Vertical))
        {
            g.FillEllipse(iconBrush, iconRect);
        }
        using (var font = new Font(MenuFontFamily, 9f, FontStyle.Bold))
        {
            TextRenderer.DrawText(g, initial, font, iconRect, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        // 右侧标题（允许自动换行，完整展示长标题）
        // 用 TextRenderer（GDI）而不是 g.DrawString（GDI+）：小字号下前者的 ClearType 更锐利。
        int titleLeft = cx + _iconSize + S(6);
        var titleRect = new Rectangle(titleLeft, 0, card.Width - titleLeft - S(6), card.Height);
        using (var titleFont = new Font(MenuFontFamily, 9f))
        {
            TextRenderer.DrawText(g, title, titleFont, titleRect, CardText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
        }
    }

    // ============ 自绘细滚动条 ============

    /// <summary>
    /// 根据当前 _content 与 _scroll 尺寸重新计算滚动范围 _scrollMax。
    /// 在 ResizeToFit 或布局变化后调用，保证滚动范围先于滚动位置更新。
    /// </summary>
    private void CalculateScrollRange()
    {
        if (_scroll is null || _content is null) return;
        _scrollMax = Math.Max(0, ContentTotalHeight - _scroll.ClientSize.Height);
    }

    /// <summary>
    /// 卡片区域连同上下留白的总高度。_content 是手动定位的，父容器的 Padding 对它无效，
    /// 留白由它自己的 Location 提供，因此滚动范围必须把这两段留白算进来，否则最后一行会被裁掉。
    /// </summary>
    private int ContentTotalHeight => _contentTop + _content.Height + _padding;

    /// <summary>
    /// 更新滚动位置：限定范围、移动 _content、刷新细滚动条绘制。
    /// </summary>
    private void UpdateScrollPosition(int newValue)
    {
        _scrollValue = Math.Clamp(newValue, 0, _scrollMax);
        if (_content is not null)
        {
            _content.Location = new Point(_padding, _contentTop - _scrollValue);
        }
        Invalidate();
    }

    /// <summary>
    /// 悬停高亮动画 tick：驱动所有卡片的高亮进度向目标值平滑过渡。
    /// </summary>
    private void DoAnimationTick()
    {
        bool anyActive = false;
        foreach (var card in _cardHover.Keys.ToList())
        {
            float cur = _cardHover[card];
            float target = _cardHoverTarget[card];
            float diff = target - cur;

            // 动画结束判定：差值小于阈值时直接落到目标值
            if (Math.Abs(diff) < CardAnimThreshold)
            {
                if (Math.Abs(diff) > 0f)
                {
                    _cardHover[card] = target;
                    card.Invalidate();
                }
                continue;
            }

            // 指数平滑：progress += (target - progress) * speed
            _cardHover[card] = cur + diff * CardAnimSpeed;
            card.Invalidate();
            anyActive = true;
        }
        if (!anyActive) _animTimer.Stop();
    }

    /// <summary>
    /// 在面板右侧绘制 6px 宽的细滚动条（轨道 + 圆角滑块）。
    /// </summary>
    private void DrawThinScrollBar(Graphics g)
    {
        int visibleH = _scroll.ClientSize.Height;
        int contentH = ContentTotalHeight;
        if (contentH <= visibleH || contentH <= 0 || visibleH <= 0) return;

        // 滑块尺寸与位置（按可视区/内容比例）
        double ratio = Math.Min(1.0, (double)visibleH / contentH);
        int thumbH = Math.Max(S(30), (int)(visibleH * ratio));
        int trackH = visibleH;
        int thumbY = _scrollMax > 0
            ? (int)((double)_scrollValue / _scrollMax * (trackH - thumbH))
            : 0;

        int x = ClientSize.Width - _scrollBarWidth - _scrollBarMargin;
        var trackRect = new Rectangle(x, _scroll.Top, _scrollBarWidth, visibleH);
        var thumbRect = new Rectangle(x, _scroll.Top + thumbY, _scrollBarWidth, thumbH);

        // 轨道（半透明浅色）
        using (var trackBrush = new SolidBrush(Color.FromArgb(40, 160, 170, 180)))
        {
            using var trackPath = RoundedRect(trackRect, _scrollBarWidth / 2);
            g.FillPath(trackBrush, trackPath);
        }
        // 滑块（主题绿色）
        using (var thumbBrush = new SolidBrush(Color.FromArgb(180, GroupTitleColor)))
        {
            using var thumbPath = RoundedRect(thumbRect, _scrollBarWidth / 2);
            g.FillPath(thumbBrush, thumbPath);
        }
    }

    /// <summary>
    /// 计算当前滑块的位置与高度（供鼠标交互使用）。
    /// </summary>
    private (int top, int height) GetThumbMetrics()
    {
        int visibleH = _scroll.ClientSize.Height;
        int contentH = ContentTotalHeight;
        if (contentH <= visibleH || contentH <= 0 || visibleH <= 0)
            return (0, 0);
        double ratio = Math.Min(1.0, (double)visibleH / contentH);
        int thumbH = Math.Max(S(30), (int)(visibleH * ratio));
        int thumbY = _scrollMax > 0
            ? (int)((double)_scrollValue / _scrollMax * (visibleH - thumbH))
            : 0;
        return (thumbY, thumbH);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        OnMouseWheelScroll(null, e);
    }

    private void OnMouseWheelScroll(object? sender, MouseEventArgs e)
    {
        int step = _cardHeight + _cardGap;
        int delta = e.Delta > 0 ? -step : step;
        UpdateScrollPosition(_scrollValue + delta);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        if (_scrollMax <= 0) return;

        int x = ClientSize.Width - _scrollBarWidth - _scrollBarMargin;
        var sbRect = new Rectangle(x, _scroll.Top, _scrollBarWidth, _scroll.ClientSize.Height);
        if (!sbRect.Contains(e.Location)) return;

        var (thumbY, thumbH) = GetThumbMetrics();
        var thumbRect = new Rectangle(x, _scroll.Top + thumbY, _scrollBarWidth, thumbH);
        if (thumbRect.Contains(e.Location))
        {
            // 点中滑块：开始拖动
            _sbDragging = true;
            _sbDragOffsetY = e.Y - thumbRect.Y;
        }
        else
        {
            // 点击轨道：按一页滚动
            int page = _scroll.ClientSize.Height - thumbH;
            int dir = e.Y < (_scroll.Top + thumbY) ? -1 : 1;
            UpdateScrollPosition(_scrollValue + dir * Math.Max(S(30), page));
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_sbDragging) return;

        var (_, thumbH) = GetThumbMetrics();
        int trackH = _scroll.ClientSize.Height;
        int newThumbY = Math.Clamp(e.Y - _scroll.Top - _sbDragOffsetY, 0, trackH - thumbH);
        int newScroll = trackH == thumbH
            ? 0
            : (int)((double)newThumbY / (trackH - thumbH) * _scrollMax);
        UpdateScrollPosition(newScroll);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _sbDragging = false;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = RoundedRect(rect, S(8));
        using var brush = new SolidBrush(PanelBg);
        g.FillPath(brush, path);
        // 不再绘制外边框（用户要求去掉）
        // 绘制细滚动条（在圆角背景之后）
        DrawThinScrollBar(g);
    }

    // ============ 尺寸与定位 ============

    /// <summary>
    /// 按 BuildCards 实测出的卡片区域尺寸调整窗体大小，并定位到悬浮按钮左侧。
    /// 直接消费 BuildCards 的返回值，不再自行遍历菜单树统计列数：
    /// 旧实现只统计有子菜单的分组，漏掉了插在最前面的“一级菜单”列，窗体因此比内容窄一整列。
    /// </summary>
    private void ResizeToFit((int width, int height) layout)
    {
        int contentW = layout.width;
        int contentH = layout.height;

        // 无菜单项时给个保守尺寸
        if (contentW <= 0 || contentH <= 0)
        {
            contentW = S(BasePanelMinWidth) - _padding * 2;
            contentH = S(120);
        }

        // 左右各留一个 _padding；高度为标题栏 + 上留白 + 卡片区 + 下留白
        int desiredWidth = Math.Max(S(BasePanelMinWidth), contentW + _padding * 2);
        int desiredHeight = _headerHeight + _contentTop + contentH + _padding;

        // 屏幕边界保护：宽度按所有分组累加（确保所有分组都能展示），高度不超过工作区
        var screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        int maxHeight = Math.Max(S(240), screen.Height - S(40));
        int width = desiredWidth;
        int height = Math.Min(desiredHeight, maxHeight);

        Size = new Size(width, height);
        UpdateRegion();

        // 布局完成后：先算滚动范围，再复位滚动位置
        CalculateScrollRange();
        UpdateScrollPosition(0);

        // 定位：默认屏幕居中；若有悬浮按钮则显示在按钮左侧
        int gap = S(6);
        int edge = S(10);
        var owner = FindFloatingButtonOwner();
        if (owner is not null)
        {
            var ownerScreen = Screen.FromControl(owner).WorkingArea;
            int x = owner.Left - width - gap;
            int y = owner.Top + (owner.Height - height) / 2;
            // 左侧放不下则切换到屏幕居中
            if (x < ownerScreen.Left + edge)
            {
                x = ownerScreen.Left + (ownerScreen.Width - width) / 2;
                y = ownerScreen.Top + (ownerScreen.Height - height) / 2;
            }
            // 宽度超过屏幕时，贴左边显示（确保左侧分组可见）
            if (x + width > ownerScreen.Right - edge) x = ownerScreen.Left + edge;
            if (x < ownerScreen.Left + edge) x = ownerScreen.Left + edge;
            if (y < ownerScreen.Top + edge) y = ownerScreen.Top + edge;
            if (y + height > ownerScreen.Bottom - edge) y = ownerScreen.Bottom - height - edge;
            Location = new Point(x, y);
        }
        else
        {
            // 宽度超过屏幕时，贴左边显示
            int x = screen.Left + Math.Max(edge, (screen.Width - width) / 2);
            int y = screen.Top + Math.Max(edge, (screen.Height - height) / 2);
            Location = new Point(x, y);
        }
    }

    private static Form? FindFloatingButtonOwner()
    {
        foreach (var form in Application.OpenForms)
        {
            if (form is FloatingButton) return form as Form;
        }
        return null;
    }

    /// <summary>
    /// 设置圆角区域裁剪：替代 TransparencyKey 抠色透明。
    /// 抠色透明的像素是 click-through 的，鼠标穿过透明区域会命中底层窗口，
    /// 导致面板误判失焦而关闭、再被悬浮按钮重新弹出，表现为面板抖动。
    /// 改用 Region 裁剪后，圆角以外的区域不属于窗口，不会收到鼠标事件。
    /// </summary>
    private void UpdateRegion()
    {
        var rect = new Rectangle(0, 0, Width, Height);
        if (rect.Width <= 0 || rect.Height <= 0) return;
        using var path = RoundedRect(rect, S(8));
        var old = Region;
        Region = new Region(path);
        old?.Dispose();
    }

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

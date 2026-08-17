using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace ConvenientSystem;

/// <summary>
/// 应用内浏览器窗口使用的原生锁屏遮罩，视觉对齐前端 LockOverlay.vue：
/// 铺满窗口的背景图（与 Vue 共用同一张 lock-bg.jpg）+ 深色薄遮罩 + 居中毛玻璃密码卡片。
/// 输入密码后通过 <see cref="VerifyAsync"/> 校验，通过则触发 <see cref="Unlocked"/>。
/// 不加载 Vue 应用、不发起额外 API 调用，因此不会影响全局登录态。
/// </summary>
public sealed class LockOverlayControl : Panel
{
    private readonly Panel _card;
    private readonly TextBox _password;
    private readonly GradientButton _unlock;

    /// <summary>校验解锁密码的委托（返回是否通过）。</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Func<string, Task<bool>>? VerifyAsync { get; set; }

    /// <summary>解锁成功回调。</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Action? Unlocked { get; set; }

    // ── 颜色（逐项对应 LockOverlay.vue 的 CSS）────────────────────────────
    // 深色遮罩三段：linear-gradient(135deg, rgba(20,15,10,.18), rgba(30,20,10,.12) 50%, rgba(15,10,5,.2))
    private static readonly Color ScrimStart = Color.FromArgb(46, 20, 15, 10);
    private static readonly Color ScrimMid = Color.FromArgb(31, 30, 20, 10);
    private static readonly Color ScrimEnd = Color.FromArgb(51, 15, 10, 5);
    private static readonly Color CardBg = Color.FromArgb(10, 255, 255, 255);      // rgba(255,255,255,.04)
    private static readonly Color CardBorder = Color.FromArgb(31, 255, 255, 255);  // rgba(255,255,255,.12)
    private static readonly Color CardTopGloss = Color.FromArgb(20, 255, 255, 255);// inset 0 1px 0 rgba(255,255,255,.08)
    private static readonly Color LogoFrom = Color.FromArgb(0x63, 0x66, 0xF1);     // #6366f1
    private static readonly Color LogoTo = Color.FromArgb(0x8B, 0x5C, 0xF6);       // #8b5cf6
    private static readonly Color LogoHalo = Color.FromArgb(51, 99, 102, 241);     // rgba(99,102,241,.2)
    private static readonly Color SubColor = Color.FromArgb(128, 255, 255, 255);   // rgba(255,255,255,.5)
    private static readonly Color TipColor = Color.FromArgb(140, 255, 255, 255);   // rgba(255,255,255,.55)
    private static readonly Color ErrorColor = Color.FromArgb(0xFF, 0x87, 0x87);   // #ff8787
    private static readonly Color InputBg = Color.FromArgb(26, 255, 255, 255);     // rgba(255,255,255,.1)
    private static readonly Color InputBorder = Color.FromArgb(46, 255, 255, 255); // rgba(255,255,255,.18)
    private static readonly Color InputFocusBorder = LogoTo;                       // is-focus: #8b5cf6

    // ── 布局基准（逻辑像素 @96dpi，实际用时经 S() 按 DPI 换算）─────────────
    // 对应 .lock-card: min-width 380 + padding 40 左右 = 460 宽；纵向各段之和 = 372 高
    private const int BaseCardWidth = 460;
    private const int BaseCardHeight = 372;
    private const int BaseCardRadius = 24;
    private const int BaseContentLeft = 40;
    private const int BaseContentWidth = 380;
    private const int BaseLogoSize = 64;
    private const int BaseInputHeight = 40;
    private const int BaseButtonHeight = 40;
    private const int BaseFieldRadius = 12;
    // 背景模糊近似 CSS backdrop-filter: blur(6px) 的降采样倍率
    private const int BlurDownscale = 8;

    private float _scale = 1f;
    private Rectangle _logoRect, _titleRect, _subRect, _inputRect, _tipRect, _buttonRect;

    // 字号取 Vue 的 px 值换算成磅（pt = px * 0.75）。磅值字体本身会随 DPI 放大，
    // 所以只需创建一次，不参与按 DPI 的重排。
    private readonly Font _titleFont = new("Microsoft YaHei UI", 16.5F, FontStyle.Bold);
    private readonly Font _subFont = new("Microsoft YaHei UI", 9.75F);
    private readonly Font _tipFont = new("Microsoft YaHei UI", 10.5F);
    private readonly Font _inputFont = new("Microsoft YaHei UI", 10.5F);
    private readonly Font _buttonFont = new("Microsoft YaHei UI", 10.5F, FontStyle.Bold);

    // 背景缓存：_bg 是"背景图 + 深色遮罩"的完整合成图，_cardBlur 是卡片区域的模糊版本。
    // 两者只在尺寸变化时重建，OnPaint 里只做贴图，锁屏挂着时没有任何持续开销。
    private Bitmap? _bg;
    private Bitmap? _cardBlur;
    private readonly Image? _bgImage;

    private string _tipText = "输入密码解锁页面";
    private bool _tipError;
    private bool _inputFocused;

    public LockOverlayControl()
    {
        Dock = DockStyle.Fill;
        DoubleBuffered = true;
        // 背景图加载失败时的兜底底色（深紫），保证锁屏内容始终可读
        BackColor = Color.FromArgb(46, 33, 74);
        _bgImage = LoadBackgroundImage();

        _card = new Panel { BackColor = Color.Transparent };
        _card.Paint += PaintCard;

        _password = new TextBox
        {
            UseSystemPasswordChar = true,
            BorderStyle = BorderStyle.None,
            ForeColor = Color.White,
            TextAlign = HorizontalAlignment.Left,
            Font = _inputFont,
        };
        _password.KeyDown += async (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            await DoUnlockAsync();
        };
        // 输入框获得/失去焦点时重画卡片，复刻 Vue 的 is-focus 紫色边框
        _password.GotFocus += (_, _) => { _inputFocused = true; _card.Invalidate(); };
        _password.LostFocus += (_, _) => { _inputFocused = false; _card.Invalidate(); };

        _unlock = new GradientButton
        {
            Text = "解 锁",
            ColorFrom = LogoFrom,
            ColorTo = LogoTo,
            Font = _buttonFont,
        };
        _unlock.Click += async (_, _) => await DoUnlockAsync();

        _card.Controls.Add(_password);
        _card.Controls.Add(_unlock);
        Controls.Add(_card);

        ApplyLayout();
    }

    /// <summary>
    /// 句柄创建后再算一次布局：构造时控件还没挂到窗口上，DeviceDpi 拿到的可能是默认 96，
    /// 高分屏下会按 1 倍缩放排版，卡片偏小、文字挤出容器。
    /// </summary>
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyLayout();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        ApplyLayout();
    }

    /// <summary>按当前 DPI 换算逻辑像素。字体用磅值（本身随 DPI 放大），因此不参与这里的换算。</summary>
    private int S(int value) => (int)Math.Round(value * _scale);

    /// <summary>
    /// 按当前 DPI 重排卡片内所有元素。
    /// </summary>
    private void ApplyLayout()
    {
        _scale = DeviceDpi / 96f;

        _card.Size = new Size(S(BaseCardWidth), S(BaseCardHeight));

        int left = S(BaseContentLeft);
        int width = S(BaseContentWidth);

        // 纵向依次排布，各段间距与 .lock-card 的 margin 对应
        _logoRect = new Rectangle((_card.Width - S(BaseLogoSize)) / 2, S(44), S(BaseLogoSize), S(BaseLogoSize));
        _titleRect = new Rectangle(left, _logoRect.Bottom + S(20), width, S(30));
        _subRect = new Rectangle(left, _titleRect.Bottom + S(6), width, S(18));
        _inputRect = new Rectangle(left, _subRect.Bottom + S(24), width, S(BaseInputHeight));
        _tipRect = new Rectangle(left, _inputRect.Bottom + S(12), width, S(22));
        _buttonRect = new Rectangle(left, _tipRect.Bottom + S(16), width, S(BaseButtonHeight));

        // TextBox 只能是真控件（无法自绘输入光标与选区），放进画出来的圆角容器里居中
        int padX = S(14);
        _password.Bounds = new Rectangle(
            _inputRect.X + padX,
            _inputRect.Y + (_inputRect.Height - _password.PreferredHeight) / 2,
            _inputRect.Width - padX * 2,
            _password.PreferredHeight);

        _unlock.Bounds = _buttonRect;
        _unlock.CornerRadius = S(BaseFieldRadius);

        CenterCard();
        InvalidateCache();
        Invalidate();
    }

    private void CenterCard()
    {
        _card.Location = new Point(
            Math.Max(0, (ClientSize.Width - _card.Width) / 2),
            Math.Max(0, (ClientSize.Height - _card.Height) / 2));
    }

    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        CenterCard();
        InvalidateCache();
        Invalidate();
    }

    // ── 背景合成与缓存 ─────────────────────────────────────────────────────

    private void InvalidateCache()
    {
        _bg?.Dispose();
        _bg = null;
        _cardBlur?.Dispose();
        _cardBlur = null;
    }

    /// <summary>确保背景与卡片模糊图可用。尺寸不符时重建，其余情况直接复用。</summary>
    private void EnsureCache()
    {
        int w = ClientSize.Width, h = ClientSize.Height;
        if (w <= 0 || h <= 0) return;

        if (_bg is null || _bg.Width != w || _bg.Height != h)
        {
            _bg?.Dispose();
            _bg = BuildBackground(w, h);
            _cardBlur?.Dispose();
            _cardBlur = null;
        }

        if (_cardBlur is null && _bg is not null)
        {
            _cardBlur = BuildCardBlur(_bg, _card.Bounds);
            // 输入框底色跟着背景一起算：TextBox 不支持半透明背景，
            // 只能取它所在位置的背景平均色再叠加卡片与输入框那两层白，得到近似值。
            // 只在缓存重建时执行一次，不会在每次重绘里改控件属性。
            UpdateInputBackColor();
            // 卡片投影直接烘到背景图上，顺序必须在 _cardBlur 取样之后，
            // 否则卡片自己的毛玻璃背景会把投影也装进去。
            using var g = Graphics.FromImage(_bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            DrawCardShadow(g, _card.Bounds, S(BaseCardRadius));
        }
    }

    /// <summary>
    /// 卡片外投影，近似 box-shadow: 0 16px 40px rgba(0,0,0,.2)。
    /// GDI+ 没有软阴影，用多层 alpha 递减的圆角描边向外扩散叠出渐变，
    /// 逐层下移得到 16px 的向下偏移。只在缓存重建时算一次。
    /// </summary>
    private void DrawCardShadow(Graphics g, Rectangle cardRect, int radius)
    {
        int spread = S(40);
        int offsetY = S(16);
        if (spread <= 0) return;

        for (int i = spread; i >= 1; i--)
        {
            // 平方衰减：越靠近卡片越浓，向外快速淡出
            float t = 1f - i / (float)spread;
            int alpha = (int)(5 * t * t);
            if (alpha <= 0) continue;

            var r = Rectangle.Inflate(cardRect, i, i);
            r.Offset(0, offsetY * i / spread);
            using var pen = new Pen(Color.FromArgb(alpha, 0, 0, 0), 1f);
            using var path = RoundedRect(r, radius + i);
            g.DrawPath(pen, path);
        }
    }

    /// <summary>合成"背景图铺满 + 深色薄遮罩"，对应 .lock-bg-img 与 .lock-overlay。</summary>
    private Bitmap BuildBackground(int w, int h)
    {
        var bmp = new Bitmap(w, h);
        using var g = Graphics.FromImage(bmp);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;

        if (_bgImage is not null)
        {
            // background-size: cover —— 等比缩放到铺满，超出的部分从中心裁掉
            float srcAspect = _bgImage.Width / (float)_bgImage.Height;
            float dstAspect = w / (float)h;
            RectangleF src;
            if (srcAspect > dstAspect)
            {
                float cropW = _bgImage.Height * dstAspect;
                src = new RectangleF((_bgImage.Width - cropW) / 2f, 0, cropW, _bgImage.Height);
            }
            else
            {
                float cropH = _bgImage.Width / dstAspect;
                src = new RectangleF(0, (_bgImage.Height - cropH) / 2f, _bgImage.Width, cropH);
            }
            g.DrawImage(_bgImage, new Rectangle(0, 0, w, h), src.X, src.Y, src.Width, src.Height, GraphicsUnit.Pixel);
        }
        else
        {
            // 图片缺失时的兜底：深紫渐变，保证卡片与白色文字仍然可读
            using var fallback = new LinearGradientBrush(
                new Rectangle(0, 0, w, h),
                Color.FromArgb(91, 33, 182),
                Color.FromArgb(124, 58, 237),
                LinearGradientMode.ForwardDiagonal);
            g.FillRectangle(fallback, 0, 0, w, h);
        }

        // 135° 三段深色遮罩
        using var scrim = new LinearGradientBrush(
            new Rectangle(0, 0, w, h), ScrimStart, ScrimEnd, 135f)
        {
            InterpolationColors = new ColorBlend
            {
                Colors = new[] { ScrimStart, ScrimMid, ScrimEnd },
                Positions = new[] { 0f, 0.5f, 1f },
            },
        };
        g.FillRectangle(scrim, 0, 0, w, h);
        return bmp;
    }

    /// <summary>
    /// 生成卡片区域的模糊背景，近似 backdrop-filter: blur(6px)。
    /// 手法是降采样再放大：GDI+ 没有模糊滤镜，缩到 1/8 再用双线性插值放大回来，
    /// 效果与小半径高斯模糊接近，代价只有一次缩放，且结果被缓存。
    /// </summary>
    private static Bitmap BuildCardBlur(Bitmap bg, Rectangle cardRect)
    {
        // 裁切范围向外扩一圈，避免放大时边缘缺少邻域像素而发暗
        int pad = BlurDownscale * 2;
        var expanded = Rectangle.Inflate(cardRect, pad, pad);
        expanded.Intersect(new Rectangle(0, 0, bg.Width, bg.Height));
        if (expanded.Width <= 0 || expanded.Height <= 0)
        {
            return new Bitmap(Math.Max(1, cardRect.Width), Math.Max(1, cardRect.Height));
        }

        int smallW = Math.Max(1, expanded.Width / BlurDownscale);
        int smallH = Math.Max(1, expanded.Height / BlurDownscale);

        using var small = new Bitmap(smallW, smallH);
        using (var gs = Graphics.FromImage(small))
        {
            gs.InterpolationMode = InterpolationMode.HighQualityBilinear;
            gs.PixelOffsetMode = PixelOffsetMode.HighQuality;
            gs.DrawImage(bg, new Rectangle(0, 0, smallW, smallH),
                expanded.X, expanded.Y, expanded.Width, expanded.Height, GraphicsUnit.Pixel);
        }

        var result = new Bitmap(cardRect.Width, cardRect.Height);
        using (var gr = Graphics.FromImage(result))
        {
            gr.InterpolationMode = InterpolationMode.HighQualityBilinear;
            gr.PixelOffsetMode = PixelOffsetMode.HighQuality;
            // 放大回扩张后的尺寸，再平移到卡片自身坐标系，等于取中间那块
            gr.DrawImage(small,
                new Rectangle(expanded.X - cardRect.X, expanded.Y - cardRect.Y, expanded.Width, expanded.Height),
                0, 0, smallW, smallH, GraphicsUnit.Pixel);
        }
        return result;
    }

    /// <summary>
    /// 用输入框所在区域的背景平均色推算 TextBox 底色。
    /// 背景之上压了卡片的 4% 白与输入框的 10% 白，等效于向白色插值约 13.6%。
    /// </summary>
    private void UpdateInputBackColor()
    {
        if (_bg is null) return;

        var area = new Rectangle(_card.Left + _inputRect.X, _card.Top + _inputRect.Y, _inputRect.Width, _inputRect.Height);
        area.Intersect(new Rectangle(0, 0, _bg.Width, _bg.Height));
        if (area.Width <= 0 || area.Height <= 0) return;

        // 缩到 1×1 直接读平均色，比逐像素遍历快且代码更短
        Color avg;
        using (var one = new Bitmap(1, 1))
        {
            using (var g = Graphics.FromImage(one))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.DrawImage(_bg, new Rectangle(0, 0, 1, 1), area.X, area.Y, area.Width, area.Height, GraphicsUnit.Pixel);
            }
            avg = one.GetPixel(0, 0);
        }

        const float t = 0.136f;
        _password.BackColor = Color.FromArgb(
            (int)(avg.R + (255 - avg.R) * t),
            (int)(avg.G + (255 - avg.G) * t),
            (int)(avg.B + (255 - avg.B) * t));
    }

    // ── 绘制 ───────────────────────────────────────────────────────────────

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (ClientRectangle.Width <= 0 || ClientRectangle.Height <= 0) return;

        EnsureCache();
        if (_bg is not null) e.Graphics.DrawImageUnscaled(_bg, 0, 0);
    }

    /// <summary>
    /// 画卡片：毛玻璃背景 + 半透明白底 + 边框 + Logo 圆 + 文案 + 输入框容器。
    /// 卡片背景必须自己贴，因为 BackColor = Transparent 的子控件只继承父控件的背景填充，
    /// 拿不到父控件 OnPaint 里画的那张背景图。
    /// </summary>
    private void PaintCard(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        EnsureCache();

        var rect = new Rectangle(0, 0, _card.Width - 1, _card.Height - 1);
        int radius = S(BaseCardRadius);
        using var shape = RoundedRect(rect, radius);

        // 毛玻璃：把模糊后的背景裁成圆角贴进来
        if (_cardBlur is not null)
        {
            var saved = g.Clip;
            g.SetClip(shape);
            g.DrawImageUnscaled(_cardBlur, 0, 0);
            g.Clip = saved;
        }

        using (var bg = new SolidBrush(CardBg)) g.FillPath(bg, shape);
        using (var border = new Pen(CardBorder, 1f)) g.DrawPath(border, shape);

        // 顶部内高光（inset 0 1px 0 rgba(255,255,255,.08)）
        using (var gloss = new Pen(CardTopGloss, 1f))
        {
            g.DrawArc(gloss, rect.X, rect.Y + 1, radius * 2, radius * 2, 200, 70);
            g.DrawLine(gloss, rect.X + radius, rect.Y + 1, rect.Right - radius, rect.Y + 1);
            g.DrawArc(gloss, rect.Right - radius * 2, rect.Y + 1, radius * 2, radius * 2, 270, 70);
        }

        DrawLogo(g, _logoRect);

        using var center = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap,
        };

        // 标题 22px / 副标题 13px / 提示 14px
        using (var white = new SolidBrush(Color.White))
        {
            g.DrawString("界面已锁定", _titleFont, white, _titleRect, center);
        }

        using (var subBrush = new SolidBrush(SubColor))
        {
            g.DrawString("请输入密码以继续操作", _subFont, subBrush, _subRect, center);
        }

        DrawInputFrame(g);

        using (var tipBrush = new SolidBrush(_tipError ? ErrorColor : TipColor))
        {
            g.DrawString(_tipText, _tipFont, tipBrush, _tipRect, center);
        }
    }

    /// <summary>输入框圆角容器：半透明白底 + 边框，聚焦时边框转紫并带一圈淡光。</summary>
    private void DrawInputFrame(Graphics g)
    {
        var rect = new Rectangle(_inputRect.X, _inputRect.Y, _inputRect.Width - 1, _inputRect.Height - 1);
        using var path = RoundedRect(rect, S(BaseFieldRadius));

        using (var bg = new SolidBrush(InputBg)) g.FillPath(bg, path);

        if (_inputFocused)
        {
            // box-shadow: 0 0 0 3px rgba(139,92,246,.2)
            using var halo = new Pen(Color.FromArgb(51, LogoTo), S(3));
            g.DrawPath(halo, path);
            using var focus = new Pen(InputFocusBorder, 1f);
            g.DrawPath(focus, path);
        }
        else
        {
            using var border = new Pen(InputBorder, 1f);
            g.DrawPath(border, path);
        }
    }

    /// <summary>
    /// Logo：紫蓝渐变圆 + 白色圆圈对勾，对应 .lock-logo 里那个 24×24 的 SVG。
    /// SVG 坐标按 32/24 放进 64 的圆里，这里统一折算成相对 rect 尺寸的比例，自动适配 DPI。
    /// </summary>
    private static void DrawLogo(Graphics g, Rectangle rect)
    {
        // 外发光环：box-shadow 0 0 0 4px rgba(99,102,241,.2)
        float ring = rect.Width * 4f / 64f;
        using (var halo = new Pen(LogoHalo, ring))
        {
            g.DrawEllipse(halo, rect.X - ring / 2, rect.Y - ring / 2, rect.Width + ring, rect.Height + ring);
        }

        using (var fill = new LinearGradientBrush(rect, LogoFrom, LogoTo, 135f))
        {
            g.FillEllipse(fill, rect);
        }

        // SVG 内容占 64 坐标系里居中的 32×32，即缩放 4/3、偏移 16
        float k = rect.Width / 64f;
        float Px(float v) => rect.X + (16f + v * 32f / 24f) * k;
        float Py(float v) => rect.Y + (16f + v * 32f / 24f) * k;

        // circle cx=12 cy=12 r=9.5 stroke-width=1.4 opacity=.9
        using (var circlePen = new Pen(Color.FromArgb(230, 255, 255, 255), Math.Max(1f, 1.4f * 32f / 24f * k)))
        {
            float r = 9.5f * 32f / 24f * k;
            g.DrawEllipse(circlePen, Px(12) - r, Py(12) - r, r * 2, r * 2);
        }

        // path M7.5 12.5 l3 3 l6 -6.5，圆头圆角
        using var checkPen = new Pen(Color.White, Math.Max(1.5f, 2f * 32f / 24f * k))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
        g.DrawLines(checkPen, new[]
        {
            new PointF(Px(7.5f), Py(12.5f)),
            new PointF(Px(10.5f), Py(15.5f)),
            new PointF(Px(16.5f), Py(9f)),
        });
    }

    // ── 交互 ───────────────────────────────────────────────────────────────

    /// <summary>显示遮罩时重置输入与提示，并聚焦密码框。</summary>
    public void ResetAndFocus()
    {
        _password.Clear();
        SetTip("输入密码解锁页面", false);
        _unlock.Enabled = true;
        _password.Focus();
    }

    private void SetTip(string text, bool error)
    {
        _tipText = text;
        _tipError = error;
        _card.Invalidate();
    }

    private async Task DoUnlockAsync()
    {
        if (VerifyAsync is null) return;

        _unlock.Enabled = false;
        try
        {
            var ok = await VerifyAsync(_password.Text.Trim());
            if (ok)
            {
                Unlocked?.Invoke();
            }
            else
            {
                SetTip("密码错误，请重新输入", true);
                _password.SelectAll();
                _password.Focus();
            }
        }
        catch (Exception ex)
        {
            SetTip("校验失败：" + ex.Message, true);
        }
        finally
        {
            _unlock.Enabled = true;
        }
    }

    /// <summary>加载与 Vue 锁屏共用的背景图（构建时由 csproj 从 web/src/assets 嵌入）。</summary>
    private static Image? LoadBackgroundImage()
    {
        try
        {
            using var stream = typeof(LockOverlayControl).Assembly.GetManifestResourceStream("lock-bg.jpg");
            if (stream is null) return null;
            // 从流直接构造的 Image 会持有流引用，复制一份以便安全释放流
            using var raw = Image.FromStream(stream);
            return new Bitmap(raw);
        }
        catch { return null; }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _bg?.Dispose();
            _cardBlur?.Dispose();
            _bgImage?.Dispose();
            _titleFont.Dispose();
            _subFont.Dispose();
            _tipFont.Dispose();
            _inputFont.Dispose();
            _buttonFont.Dispose();
        }
        base.Dispose(disposing);
    }

    internal static GraphicsPath RoundedRect(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        int d = Math.Max(1, radius * 2);
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

/// <summary>
/// 圆角渐变按钮，对应 Vue 锁屏里被覆写过样式的 el-button：
/// 135° 紫蓝渐变 + 圆角 12 + 悬停提亮。按钮本身不透明，所以无需处理父背景。
/// </summary>
internal sealed class GradientButton : Button
{
    private bool _hovered;

    // 标上 Hidden：这个控件不进设计器，否则 WFO1000 分析器会要求为属性配置代码序列化。
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Color ColorFrom { get; set; } = Color.FromArgb(0x63, 0x66, 0xF1);

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Color ColorTo { get; set; } = Color.FromArgb(0x8B, 0x5C, 0xF6);

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal int CornerRadius { get; set; } = 12;

    public GradientButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        ForeColor = Color.White;
        Cursor = Cursors.Hand;
        DoubleBuffered = true;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hovered = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hovered = false;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        // 按钮四角之外露出的是父控件那块背景，用父控件背景色填一下避免黑角
        using (var clear = new SolidBrush(Parent?.BackColor ?? Color.Transparent))
        {
            g.FillRectangle(clear, ClientRectangle);
        }

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = LockOverlayControl.RoundedRect(rect, CornerRadius);

        var from = ColorFrom;
        var to = ColorTo;
        if (!Enabled)
        {
            from = Blend(from, Color.Gray, 0.5f);
            to = Blend(to, Color.Gray, 0.5f);
        }
        else if (_hovered)
        {
            // filter: brightness(1.1)
            from = Brighten(from, 1.1f);
            to = Brighten(to, 1.1f);
        }

        using (var fill = new LinearGradientBrush(rect, from, to, 135f))
        {
            g.FillPath(fill, path);
        }

        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };
        using var text = new SolidBrush(Enabled ? ForeColor : Color.FromArgb(180, 255, 255, 255));
        g.DrawString(Text, Font, text, rect, format);
    }

    private static Color Brighten(Color c, float factor) => Color.FromArgb(
        Math.Min(255, (int)(c.R * factor)),
        Math.Min(255, (int)(c.G * factor)),
        Math.Min(255, (int)(c.B * factor)));

    private static Color Blend(Color a, Color b, float t) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * t),
        (int)(a.G + (b.G - a.G) * t),
        (int)(a.B + (b.B - a.B) * t));
}

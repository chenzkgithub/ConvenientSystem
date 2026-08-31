using System.Drawing.Drawing2D;

namespace ConvenientSystem;

/// <summary>树图中的一个待布局节点。</summary>
internal sealed class TreemapNode
{
    public string Name = "";
    public string FullPath = "";
    public long Size;
    public bool IsDir;
    /// <summary>布局计算后的屏幕矩形。</summary>
    public RectangleF Bounds;
}

/// <summary>
/// 磁盘占用树图控件：用 squarified treemap 算法把文件按大小铺成矩形块，
/// 面积正比于占用空间，颜色区分类型。类似 WizTree 底部的可视化区域。
/// </summary>
internal sealed class TreemapControl : Control
{
    // 品牌色（coding-standards 2.10: #3b82f6）为基准的调色板，按扩展名散列取色
    private static readonly Color[] Palette =
    {
        Color.FromArgb(59, 130, 246),   // 蓝（品牌色）
        Color.FromArgb(16, 185, 129),   // 绿
        Color.FromArgb(245, 158, 11),   // 橙
        Color.FromArgb(139, 92, 246),   // 紫
        Color.FromArgb(236, 72, 153),   // 粉
        Color.FromArgb(6, 182, 212),    // 青
        Color.FromArgb(234, 179, 8),    // 黄
        Color.FromArgb(239, 68, 68),    // 红
        Color.FromArgb(99, 102, 241),   // 靛
        Color.FromArgb(20, 184, 166),   // 蓝绿
    };

    private static readonly Color BorderColor = Color.FromArgb(255, 255, 255);
    private static readonly Color EmptyText = Color.FromArgb(148, 163, 184);

    private readonly List<TreemapNode> _nodes = new();
    private readonly Font _labelFont;
    private readonly ToolTip _tooltip = new() { InitialDelay = 300, ReshowDelay = 100 };
    private TreemapNode? _hover;

    /// <summary>双击某个块时触发，参数为该块对应的完整路径。</summary>
    public event Action<string>? NodeActivated;

    public TreemapControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw, true);
        BackColor = Color.FromArgb(248, 250, 252);
        _labelFont = new Font("Microsoft YaHei UI", 8f);
    }

    /// <summary>
    /// 设置要展示的节点集合（通常是某个目录的直接子项），重新布局并重绘。
    /// </summary>
    public void SetNodes(IEnumerable<TreemapNode> nodes)
    {
        _nodes.Clear();
        // 只保留有实际占用的项，按大小降序（squarified 算法要求有序输入）
        _nodes.AddRange(nodes.Where(n => n.Size > 0).OrderByDescending(n => n.Size));
        _hover = null;
        Relayout();
        Invalidate();
    }

    public void Clear()
    {
        _nodes.Clear();
        _hover = null;
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Relayout();
    }

    private void Relayout()
    {
        if (_nodes.Count == 0 || Width <= 2 || Height <= 2) return;

        long total = 0;
        foreach (var n in _nodes) total += n.Size;
        if (total <= 0) return;

        Squarify(_nodes, 0, _nodes.Count, new RectangleF(0, 0, Width, Height), total);
    }

    /// <summary>
    /// squarified treemap：沿短边方向逐行铺块，通过控制每行的宽高比尽量接近 1，
    /// 让矩形更接近正方形（比朴素 slice-and-dice 可读性高得多）。
    /// </summary>
    private static void Squarify(List<TreemapNode> nodes, int start, int end,
        RectangleF rect, long total)
    {
        while (true)
        {
            if (start >= end || rect.Width <= 0 || rect.Height <= 0) return;

            // 剩一个节点直接占满剩余空间
            if (end - start == 1)
            {
                nodes[start].Bounds = rect;
                return;
            }

            bool horizontal = rect.Width >= rect.Height;
            float shortSide = horizontal ? rect.Height : rect.Width;

            // 贪心扩张当前行：加入节点直到宽高比开始变差
            int count = 0;
            long rowSum = 0;
            float bestRatio = float.MaxValue;

            for (int i = start; i < end; i++)
            {
                long newSum = rowSum + nodes[i].Size;
                if (newSum <= 0) break;

                // 该行在长边方向占据的厚度
                float thickness = (float)((double)newSum / total) *
                                  (horizontal ? rect.Width : rect.Height);
                if (thickness <= 0) break;

                // 用行内最大项和最小项评估最差宽高比
                long maxItem = nodes[start].Size;
                long minItem = nodes[i].Size;
                float maxLen = (float)((double)maxItem / newSum) * shortSide;
                float minLen = (float)((double)minItem / newSum) * shortSide;

                float worst = Math.Max(
                    maxLen > 0 ? thickness / maxLen : float.MaxValue,
                    minLen > 0 ? minLen / thickness : float.MaxValue);
                if (worst < 1f && worst > 0f) worst = 1f / worst;

                if (worst > bestRatio) break;

                bestRatio = worst;
                rowSum = newSum;
                count++;
            }

            if (count == 0) { count = 1; rowSum = nodes[start].Size; }
            if (rowSum <= 0) return;

            // 铺放当前行
            float rowThickness = (float)((double)rowSum / total) *
                                 (horizontal ? rect.Width : rect.Height);
            float cursor = horizontal ? rect.Y : rect.X;

            for (int i = start; i < start + count; i++)
            {
                float len = (float)((double)nodes[i].Size / rowSum) * shortSide;
                nodes[i].Bounds = horizontal
                    ? new RectangleF(rect.X, cursor, rowThickness, len)
                    : new RectangleF(cursor, rect.Y, len, rowThickness);
                cursor += len;
            }

            // 尾递归改循环：处理剩余区域，避免深层节点导致栈增长
            rect = horizontal
                ? new RectangleF(rect.X + rowThickness, rect.Y, rect.Width - rowThickness, rect.Height)
                : new RectangleF(rect.X, rect.Y + rowThickness, rect.Width, rect.Height - rowThickness);
            total -= rowSum;
            start += count;

            if (total <= 0) return;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);

        if (_nodes.Count == 0)
        {
            TextRenderer.DrawText(g, "选择左侧文件夹查看占用分布", _labelFont,
                new Rectangle(0, 0, Width, Height), EmptyText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        g.SmoothingMode = SmoothingMode.None;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using var borderPen = new Pen(BorderColor, 1f);

        foreach (var node in _nodes)
        {
            var r = node.Bounds;
            // 小于 1px 的块画不出来，跳过省时间
            if (r.Width < 1f || r.Height < 1f) continue;

            var baseColor = ColorFor(node);
            // 悬停块提亮，给出反馈
            var fill = ReferenceEquals(node, _hover) ? Lighten(baseColor, 0.25f) : baseColor;

            using (var brush = new SolidBrush(fill))
                g.FillRectangle(brush, r);

            if (r.Width > 2f && r.Height > 2f)
                g.DrawRectangle(borderPen, r.X, r.Y, r.Width, r.Height);

            // 只在块足够大时画文字，否则纯噪音
            if (r.Width > 46f && r.Height > 16f)
            {
                var textRect = Rectangle.Round(new RectangleF(
                    r.X + 3f, r.Y + 2f, r.Width - 6f, r.Height - 4f));
                TextRenderer.DrawText(g, node.Name, _labelFont, textRect, Color.White,
                    TextFormatFlags.WordEllipsis | TextFormatFlags.NoPadding);
            }
        }
    }

    /// <summary>目录用品牌蓝，文件按扩展名散列到固定颜色（同类型同色）。</summary>
    private static Color ColorFor(TreemapNode node)
    {
        if (node.IsDir) return Palette[0];

        var ext = Path.GetExtension(node.Name);
        if (string.IsNullOrEmpty(ext)) return Palette[1];

        int hash = 0;
        foreach (var c in ext)
            hash = hash * 31 + char.ToLowerInvariant(c);
        return Palette[Math.Abs(hash) % Palette.Length];
    }

    private static Color Lighten(Color c, float amount)
    {
        return Color.FromArgb(c.A,
            (int)Math.Min(255, c.R + (255 - c.R) * amount),
            (int)Math.Min(255, c.G + (255 - c.G) * amount),
            (int)Math.Min(255, c.B + (255 - c.B) * amount));
    }

    private TreemapNode? HitTest(Point p)
    {
        foreach (var node in _nodes)
            if (node.Bounds.Contains(p.X, p.Y))
                return node;
        return null;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hit = HitTest(e.Location);
        if (ReferenceEquals(hit, _hover)) return;

        // 只重绘受影响的两个块，避免整幅树图重画造成闪烁
        var old = _hover;
        _hover = hit;
        if (old is not null) Invalidate(Rectangle.Round(old.Bounds));
        if (hit is not null) Invalidate(Rectangle.Round(hit.Bounds));

        var tip = hit is null ? "" : $"{hit.Name}  ({FormatSize(hit.Size)})";
        if (_tooltip.GetToolTip(this) != tip) _tooltip.SetToolTip(this, tip);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hover is null) return;
        var old = _hover;
        _hover = null;
        Invalidate(Rectangle.Round(old.Bounds));
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        var hit = HitTest(e.Location);
        if (hit is not null && !string.IsNullOrEmpty(hit.FullPath))
            NodeActivated?.Invoke(hit.FullPath);
    }

    /// <summary>字节数转可读大小，供树图提示与外部列表复用。</summary>
    public static string FormatSize(long bytes)
    {
        if (bytes < 0) return "";
        if (bytes < 1024) return $"{bytes} B";
        double v = bytes;
        string[] units = { "KB", "MB", "GB", "TB", "PB" };
        foreach (var u in units)
        {
            v /= 1024;
            if (v < 1024) return $"{v:0.##} {u}";
        }
        return $"{v:0.##} PB";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _labelFont?.Dispose();
            _tooltip?.Dispose();
        }
        base.Dispose(disposing);
    }
}

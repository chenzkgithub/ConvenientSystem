using System.Reflection;

namespace ConvenientSystem;

/// <summary>
/// WinForms 界面样式辅助：统一 DataGridView 主题，减少各窗体重复代码。
/// </summary>
internal static class UiStyle
{
    internal static readonly Color ThemeGreen = Color.FromArgb(47, 169, 143);
    internal static readonly Color ThemeGreenLight = Color.FromArgb(240, 251, 248);
    internal static readonly Color BorderColor = Color.FromArgb(230, 235, 242);
    internal static readonly Color HeaderBack = Color.FromArgb(245, 247, 250);
    internal static readonly Color HeaderFore = Color.FromArgb(40, 50, 60);
    internal static readonly Color RowAlternate = Color.FromArgb(252, 253, 254);

    /// <summary>应用统一风格到 DataGridView。</summary>
    public static void Apply(this DataGridView dgv)
    {
        dgv.BackgroundColor = Color.White;
        dgv.BorderStyle = BorderStyle.None;
        dgv.AllowUserToAddRows = false;
        dgv.AllowUserToDeleteRows = false;
        dgv.ReadOnly = true;
        dgv.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        dgv.MultiSelect = false;
        dgv.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        dgv.RowHeadersVisible = false;
        dgv.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        dgv.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = HeaderBack,
            ForeColor = HeaderFore,
            Font = new Font(dgv.Font.FontFamily, 9f, FontStyle.Bold),
            Alignment = DataGridViewContentAlignment.MiddleCenter,
            SelectionBackColor = HeaderBack,
            SelectionForeColor = HeaderFore,
        };
        dgv.ColumnHeadersHeight = 34;
        dgv.EnableHeadersVisualStyles = false;
        dgv.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.White,
            ForeColor = HeaderFore,
            Font = new Font(dgv.Font.FontFamily, 9f),
            Alignment = DataGridViewContentAlignment.MiddleCenter,
            SelectionBackColor = ThemeGreenLight,
            SelectionForeColor = HeaderFore,
        };
        dgv.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = RowAlternate,
        };
        dgv.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        dgv.GridColor = BorderColor;
        dgv.RowTemplate.Height = 34;

        // 开启双缓冲，减少大数据量滚动闪烁
        SetDoubleBuffered(dgv, true);
    }

    /// <summary>给分组面板设置统一边框与背景。</summary>
    public static void ApplyPanel(this Panel panel, string title)
    {
        panel.BackColor = Color.White;
        panel.Padding = new Padding(1);
        if (!string.IsNullOrEmpty(title) && panel.Controls.Count == 0)
        {
            // 可扩展：添加标题栏
        }
    }

    private static void SetDoubleBuffered(Control control, bool value)
    {
        var prop = typeof(Control).GetProperty("DoubleBuffered", BindingFlags.NonPublic | BindingFlags.Instance);
        prop?.SetValue(control, value);
    }
}

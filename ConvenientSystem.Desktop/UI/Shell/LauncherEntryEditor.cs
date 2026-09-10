namespace ConvenientSystem;

/// <summary>
/// 启动器自定义条目编辑器：DataGridView 内联编辑，支持增删改。
/// 底部保存按钮显式保存，关闭时自动保存（兜底）。
/// </summary>
internal sealed class LauncherEntryEditor : Form
{
    private readonly LauncherStore _store;
    private readonly DataGridView _grid;
    private readonly Button _saveButton;

    public LauncherEntryEditor(LauncherStore store)
    {
        _store = store;

        Text = "管理启动器条目";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        Width = 640;
        Height = 520;
        MinimumSize = new Size(480, 340);
        ShowInTaskbar = false;

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = true,
            AllowUserToDeleteRows = true,
            AllowUserToResizeColumns = true,
            RowHeadersVisible = false,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.None,
            ColumnHeadersHeight = 32,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        };

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Title",
            HeaderText = "名称",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Target",
            HeaderText = "目标",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            MinimumWidth = 200,
        });
        var kindCol = new DataGridViewComboBoxColumn
        {
            Name = "Kind",
            HeaderText = "类型",
            Width = 90,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
        };
        kindCol.Items.AddRange("url", "file", "command");
        _grid.Columns.Add(kindCol);

        _grid.DefaultValuesNeeded += (_, e) =>
        {
            e.Row.Cells["Kind"].Value = "url";
        };

        // ComboBox 列单次点击即展开下拉：默认需两次点击（一次选中、一次进入编辑），
        // 这里在 CellClick 中立即 BeginEdit 并展开下拉。
        _grid.CellClick += (_, e) =>
        {
            if (e.RowIndex < 0) return;
            if (_grid.Columns[e.ColumnIndex].Name != "Kind") return;
            _grid.BeginEdit(true);
            BeginInvoke(() =>
            {
                if (_grid.EditingControl is DataGridViewComboBoxEditingControl combo)
                    combo.DroppedDown = true;
            });
        };

        // 从 API 拉取最新数据，确保编辑器显示的是数据库当前值而非本地缓存。
        // 网页端可能已修改并保存到数据库，本地缓存还是旧的。
        _store.ReloadFromApi();

        foreach (var entry in _store.Entries)
            _grid.Rows.Add(entry.Title, entry.Target, entry.Kind);

        // 底部示例提示
        var examplesLabel = new Label
        {
            Text = "示例：\n  1. 百度  ｜ https://baidu.com  ｜ 类型：url（网址）\n  2. 工作目录  ｜ D:\\Work  ｜ 类型：file（文件/文件夹）\n  3. 记事本  ｜ notepad  ｜ 类型：command（命令）",
            Dock = DockStyle.Bottom,
            Height = 84,
            ForeColor = Color.FromArgb(100, 116, 139),
            Font = new Font("Microsoft YaHei UI", 9F),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(12, 4, 12, 4),
        };

        // 删除选中行按钮
        var deleteButton = new Button
        {
            Text = "删除选中行",
            Width = 110,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(239, 68, 68),
            ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 9F),
        };
        deleteButton.FlatAppearance.BorderSize = 0;
        deleteButton.Click += (_, _) =>
        {
            if (_grid.CurrentCell == null) return;
            int idx = _grid.CurrentCell.RowIndex;
            if (idx < 0 || idx >= _grid.Rows.Count) return;
            if (_grid.Rows[idx].IsNewRow) return;
            _grid.Rows.RemoveAt(idx);
        };

        // 保存按钮
        _saveButton = new Button
        {
            Text = "保存",
            Width = 100,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(64, 158, 255),
            ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
        };
        _saveButton.FlatAppearance.BorderSize = 0;
        _saveButton.Click += (_, _) =>
        {
            SaveFromGrid();
            MessageBox.Show("已保存", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };

        // 按钮面板：删除在左、保存在右
        var buttonPanel = new Panel { Dock = DockStyle.Bottom, Height = 44 };
        buttonPanel.Controls.Add(deleteButton);
        buttonPanel.Controls.Add(_saveButton);
        _saveButton.Dock = DockStyle.Right;
        deleteButton.Dock = DockStyle.Right;

        Controls.Add(_grid);
        Controls.Add(examplesLabel);
        Controls.Add(buttonPanel);
        // Dock 顺序：索引越大越先停靠（从边缘向内），
        // buttonPanel 最先停靠占最底部，examplesLabel 其次，grid 填充剩余。
        Controls.SetChildIndex(_grid, 0);
        Controls.SetChildIndex(examplesLabel, 1);
        Controls.SetChildIndex(buttonPanel, 2);
    }

    /// <summary>从网格读取所有有效行并替换存储（空标题/空目标的行自动丢弃）。</summary>
    private void SaveFromGrid()
    {
        var entries = new List<LauncherCustomEntry>();
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.IsNewRow) continue;
            var title = row.Cells["Title"].Value?.ToString() ?? "";
            var target = row.Cells["Target"].Value?.ToString() ?? "";
            var kind = row.Cells["Kind"].Value?.ToString() ?? "url";
            if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(target))
                entries.Add(new LauncherCustomEntry { Title = title, Target = target, Kind = kind });
        }
        _store.ReplaceAll(entries);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        SaveFromGrid();
        base.OnFormClosed(e);
    }
}

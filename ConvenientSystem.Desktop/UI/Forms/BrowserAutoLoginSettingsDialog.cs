namespace ConvenientSystem;

/// <summary>在单个窗口内维护按网站地址唯一的自动登录配置。</summary>
internal sealed class BrowserAutoLoginSettingsDialog : Form
{
    private const int FormWidth = 900;
    private const int Gutter = 20;
    private const int ContentTop = 48;
    private const int ContentHeight = 400;
    private const int GridWidth = 420;
    private const int PanelLeft = Gutter + GridWidth + 16;
    private const int PanelWidth = FormWidth - Gutter - PanelLeft;
    private const int ButtonWidth = 88;
    private const int ButtonGap = 8;
    private const int FieldLabelLeft = 20;
    private const int FieldInputLeft = 100;
    private const int FieldInputWidth = PanelWidth - FieldInputLeft - 20;

    private readonly BrowserAutoLoginCredentialStore _store;
    private readonly bool _closeAfterSave;
    private readonly DataGridView _siteGrid;
    private readonly TextBox _nameTextBox;
    private readonly TextBox _addressTextBox;
    private readonly ComboBox _adapterComboBox;
    private readonly TextBox _userNameTextBox;
    private readonly TextBox _passwordTextBox;
    private readonly CheckBox _enabledCheckBox;
    private Guid _editingId;
    private bool _changed;
    private bool _suppressSelection;

    public BrowserAutoLoginSettingsDialog(
        BrowserAutoLoginCredentialStore store,
        IReadOnlyList<BrowserAutoLoginAdapterOption> adapters,
        string? suggestedAddress = null)
    {
        _store = store;
        _closeAfterSave = !string.IsNullOrWhiteSpace(suggestedAddress);

        Text = "自动登录配置";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        ClientSize = new Size(FormWidth, 520);
        BackColor = Color.White;
        Font = new Font("Microsoft YaHei UI", 9F);

        Controls.Add(new Label
        {
            Text = "每个网站地址仅绑定一个账号。密码使用 Windows DPAPI 加密，仅当前 Windows 用户可解密。",
            Bounds = new Rectangle(Gutter, 14, FormWidth - Gutter * 2, 24),
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(100, 100, 100),
        });

        _siteGrid = CreateSiteGrid();
        Controls.Add(_siteGrid);

        var editorGroup = new GroupBox
        {
            Text = "网站登录信息",
            Bounds = new Rectangle(PanelLeft, ContentTop, PanelWidth, ContentHeight),
        };
        Controls.Add(editorGroup);

        _nameTextBox = AddTextField(editorGroup, "网站名称", 0);
        _addressTextBox = AddTextField(editorGroup, "网站地址", 1);

        AddFieldLabel(editorGroup, "登录方式", 2);
        _adapterComboBox = new ComboBox
        {
            Bounds = new Rectangle(FieldInputLeft, FieldTop(2), FieldInputWidth, 27),
            DropDownStyle = ComboBoxStyle.DropDownList,
            DisplayMember = nameof(BrowserAutoLoginAdapterOption.DisplayName),
            ValueMember = nameof(BrowserAutoLoginAdapterOption.AdapterType),
            DataSource = adapters.ToArray(),
        };
        editorGroup.Controls.Add(_adapterComboBox);

        _userNameTextBox = AddTextField(editorGroup, "账号", 3);
        _passwordTextBox = AddTextField(editorGroup, "密码", 4);
        _passwordTextBox.UseSystemPasswordChar = true;

        var optionTop = FieldTop(5) - 6;
        var showPassword = new CheckBox
        {
            Text = "显示密码",
            Bounds = new Rectangle(FieldInputLeft, optionTop, 96, 26),
        };
        showPassword.CheckedChanged += (_, _) => _passwordTextBox.UseSystemPasswordChar = !showPassword.Checked;
        editorGroup.Controls.Add(showPassword);

        _enabledCheckBox = new CheckBox
        {
            Text = "启用此网站的自动登录",
            Bounds = new Rectangle(FieldInputLeft + 110, optionTop, FieldInputWidth - 110, 26),
            Checked = true,
        };
        editorGroup.Controls.Add(_enabledCheckBox);

        editorGroup.Controls.Add(new Label
        {
            Text = "网站地址不带端口时匹配该主机的任意端口；带端口时仅匹配该端口，且优先于任意端口的配置。",
            Bounds = new Rectangle(FieldInputLeft, optionTop + 40, FieldInputWidth, 44),
            ForeColor = Color.FromArgb(130, 130, 130),
        });

        var buttonTop = ContentTop + ContentHeight + 16;

        var addButton = CreateButton("新增", Gutter, buttonTop, false);
        addButton.Click += (_, _) => BeginNewSite();
        Controls.Add(addButton);

        var deleteButton = CreateButton("删除", Gutter + ButtonWidth + ButtonGap, buttonTop, false);
        deleteButton.Click += (_, _) => DeleteSelectedSite();
        Controls.Add(deleteButton);

        var saveButton = CreateButton("保存配置", PanelLeft, buttonTop, true);
        saveButton.Click += OnSaveClick;
        Controls.Add(saveButton);

        var resetButton = CreateButton("取消编辑", PanelLeft + ButtonWidth + ButtonGap, buttonTop, false);
        resetButton.Click += (_, _) => LoadSelectedSiteOrNew();
        Controls.Add(resetButton);

        var closeButton = CreateButton("关闭", FormWidth - Gutter - ButtonWidth, buttonTop, false);
        closeButton.Click += (_, _) =>
        {
            DialogResult = _changed ? DialogResult.OK : DialogResult.Cancel;
            Close();
        };
        Controls.Add(closeButton);
        CancelButton = closeButton;

        RefreshGrid();
        InitializeEditor(suggestedAddress);
    }

    private DataGridView CreateSiteGrid()
    {
        var grid = new DataGridView
        {
            Bounds = new Rectangle(Gutter, ContentTop + 8, GridWidth, ContentHeight - 8),
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AutoGenerateColumns = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            ColumnHeadersHeight = 32,
            EnableHeadersVisualStyles = false,
            GridColor = Color.FromArgb(232, 232, 232),
            MultiSelect = false,
            ReadOnly = true,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        };
        grid.RowTemplate.Height = 30;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(245, 247, 250);
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(245, 247, 250);
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(60, 60, 60);
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(222, 242, 237);
        grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(30, 30, 30);
        grid.DefaultCellStyle.Padding = new Padding(4, 0, 4, 0);
        grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(4, 0, 4, 0);

        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "网站名称", FillWeight = 28 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "网站地址", FillWeight = 47 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "账号", FillWeight = 25 });
        grid.SelectionChanged += (_, _) =>
        {
            // 仅响应用户在列表中的选择，避免程序性选中刷新编辑区打断下拉框等正在进行的输入。
            if (!_suppressSelection && grid.ContainsFocus && SelectedSite is not null)
                LoadSite(SelectedSite);
        };
        return grid;
    }

    private static Button CreateButton(string text, int left, int top, bool primary)
    {
        var button = new Button
        {
            Text = text,
            Bounds = new Rectangle(left, top, ButtonWidth, 34),
            FlatStyle = primary ? FlatStyle.Flat : FlatStyle.Standard,
        };
        if (primary)
        {
            button.BackColor = Color.FromArgb(47, 169, 143);
            button.ForeColor = Color.White;
            button.FlatAppearance.BorderSize = 0;
        }
        return button;
    }

    private static int FieldTop(int row) => 36 + row * 46;

    private static void AddFieldLabel(Control parent, string label, int row)
        => parent.Controls.Add(new Label
        {
            Text = label,
            Bounds = new Rectangle(FieldLabelLeft, FieldTop(row), FieldInputLeft - FieldLabelLeft - 8, 27),
            TextAlign = ContentAlignment.MiddleLeft,
        });

    private static TextBox AddTextField(Control parent, string label, int row)
    {
        AddFieldLabel(parent, label, row);
        var textBox = new TextBox
        {
            Bounds = new Rectangle(FieldInputLeft, FieldTop(row), FieldInputWidth, 27),
        };
        parent.Controls.Add(textBox);
        return textBox;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // 从外部浏览器右键唤起时本程序不在前台，短暂置顶把窗口带到最前，随后取消置顶以免干扰下拉框。
        TopMost = true;
        TopMost = false;
        Activate();
    }

    private BrowserAutoLoginSiteCredential? SelectedSite
        => _siteGrid.SelectedRows.Count == 0
            ? null
            : _siteGrid.SelectedRows[0].Tag as BrowserAutoLoginSiteCredential;

    private void InitializeEditor(string? suggestedAddress)
    {
        if (BrowserAutoLoginCredentialStore.TryNormalizeSiteAddress(
                suggestedAddress ?? string.Empty, out var normalized, out _))
        {
            var target = new Uri(normalized);
            var matchingSites = _store.GetAll()
                .Where(site => Uri.TryCreate(site.BaseAddress, UriKind.Absolute, out var configured) &&
                    configured.Scheme.Equals(target.Scheme, StringComparison.OrdinalIgnoreCase) &&
                    configured.Host.Equals(target.Host, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var existing = matchingSites.FirstOrDefault(site =>
                !site.MatchAnyPort && new Uri(site.BaseAddress).Port == target.Port);
            existing ??= matchingSites.FirstOrDefault(site => site.MatchAnyPort);
            if (existing is not null)
            {
                SelectSite(existing.Id);
                LoadSite(existing);
                ActiveControl = existing.CredentialsAvailable ? _userNameTextBox : _passwordTextBox;
                return;
            }

            BeginNewSite(normalized);
            return;
        }

        if (SelectedSite is not null)
        {
            LoadSite(SelectedSite);
            ActiveControl = _nameTextBox;
        }
        else
        {
            BeginNewSite();
        }
    }

    private void RefreshGrid(Guid? selectedId = null)
    {
        _suppressSelection = true;
        try
        {
            _siteGrid.Rows.Clear();
            foreach (var site in _store.GetAll())
            {
                var address = site.MatchAnyPort
                    ? $"{site.BaseAddress}（任意端口）"
                    : site.BaseAddress;
                var rowIndex = _siteGrid.Rows.Add(
                    site.Name,
                    address,
                    site.CredentialsAvailable ? site.UserName : $"{site.UserName}（需重设）");
                _siteGrid.Rows[rowIndex].Tag = site;
            }
            _siteGrid.ClearSelection();
            if (selectedId.HasValue)
                SelectSite(selectedId.Value);
            else if (_siteGrid.Rows.Count > 0)
                _siteGrid.Rows[0].Selected = true;
        }
        finally
        {
            _suppressSelection = false;
        }
    }

    private void SelectSite(Guid id)
    {
        foreach (DataGridViewRow row in _siteGrid.Rows)
        {
            if (row.Tag is BrowserAutoLoginSiteCredential site && site.Id == id)
            {
                _siteGrid.ClearSelection();
                row.Selected = true;
                _siteGrid.CurrentCell = row.Cells[0];
                return;
            }
        }
    }

    private void LoadSelectedSiteOrNew()
    {
        if (SelectedSite is not null)
            LoadSite(SelectedSite);
        else
            BeginNewSite();
    }

    private void LoadSite(BrowserAutoLoginSiteCredential site)
    {
        _editingId = site.Id;
        _nameTextBox.Text = site.Name;
        _addressTextBox.Text = site.BaseAddress;
        _adapterComboBox.SelectedValue = site.AdapterType;
        _userNameTextBox.Text = site.UserName;
        _passwordTextBox.Text = site.Password;
        _enabledCheckBox.Checked = site.Enabled;
    }

    private void BeginNewSite(string? suggestedAddress = null)
    {
        _suppressSelection = true;
        _siteGrid.ClearSelection();
        _suppressSelection = false;

        var legacy = _store.GetAll().Count == 0 ? _store.GetLegacyCredential() : null;
        _editingId = Guid.NewGuid();
        _nameTextBox.Text = GetDefaultSiteName(suggestedAddress);
        _addressTextBox.Text = suggestedAddress ?? string.Empty;
        if (_adapterComboBox.Items.Count > 0) _adapterComboBox.SelectedIndex = 0;
        _userNameTextBox.Text = legacy?.UserName ?? string.Empty;
        _passwordTextBox.Text = legacy?.Password ?? string.Empty;
        _enabledCheckBox.Checked = true;
        ActiveControl = string.IsNullOrWhiteSpace(_addressTextBox.Text) ? _nameTextBox : _userNameTextBox;
    }

    private void OnSaveClick(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_nameTextBox.Text))
        {
            ShowRequiredMessage("请输入网站名称。", _nameTextBox);
            return;
        }
        if (!BrowserAutoLoginCredentialStore.TryNormalizeSiteAddress(
                _addressTextBox.Text, out var baseAddress, out var matchAnyPort))
        {
            ShowRequiredMessage("请输入有效的 HTTP 或 HTTPS 网站地址。", _addressTextBox);
            return;
        }
        if (_adapterComboBox.SelectedItem is not BrowserAutoLoginAdapterOption adapter)
        {
            ShowRequiredMessage("请选择已支持的网站登录方式。", _adapterComboBox);
            return;
        }
        if (string.IsNullOrWhiteSpace(_userNameTextBox.Text))
        {
            ShowRequiredMessage("请输入账号。", _userNameTextBox);
            return;
        }
        if (string.IsNullOrEmpty(_passwordTextBox.Text))
        {
            ShowRequiredMessage("请输入密码。", _passwordTextBox);
            return;
        }

        var site = new BrowserAutoLoginSiteCredential(
            _editingId,
            _nameTextBox.Text.Trim(),
            baseAddress,
            adapter.AdapterType,
            _userNameTextBox.Text.Trim(),
            _passwordTextBox.Text,
            _enabledCheckBox.Checked,
            matchAnyPort);

        try
        {
            _store.Save(site);
            _changed = true;
            RefreshGrid(site.Id);
            LoadSite(site);
            if (_closeAfterSave)
            {
                DialogResult = DialogResult.OK;
                Close();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"保存失败：{ex.Message}", "自动登录配置",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void DeleteSelectedSite()
    {
        var site = SelectedSite;
        if (site is null)
        {
            MessageBox.Show(this, "请先选择一条网站配置。", "自动登录配置",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (MessageBox.Show(this, $"确定删除“{site.Name}”的自动登录配置吗？", "自动登录配置",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            _store.Delete(site.Id);
            _changed = true;
            RefreshGrid();
            LoadSelectedSiteOrNew();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"删除失败：{ex.Message}", "自动登录配置",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ShowRequiredMessage(string message, Control control)
    {
        MessageBox.Show(this, message, "自动登录配置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        control.Focus();
    }

    private static string GetDefaultSiteName(string? address)
        => Uri.TryCreate(address, UriKind.Absolute, out var uri) ? uri.Host : string.Empty;
}

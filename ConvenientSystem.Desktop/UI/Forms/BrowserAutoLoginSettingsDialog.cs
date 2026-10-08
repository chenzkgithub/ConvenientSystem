namespace ConvenientSystem;

/// <summary>在单个窗口内维护按网站地址唯一的自动登录配置。</summary>
internal sealed class BrowserAutoLoginSettingsDialog : Form
{
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
        TopMost = true;
        ClientSize = new Size(940, 525);
        BackColor = Color.White;
        Font = new Font("Microsoft YaHei UI", 9F);

        Controls.Add(new Label
        {
            Text = "每个网站地址仅绑定一个账号。密码使用 Windows DPAPI 加密，仅当前 Windows 用户可解密。",
            Bounds = new Rectangle(24, 18, 860, 26),
            ForeColor = Color.FromArgb(80, 80, 80),
        });

        _siteGrid = CreateSiteGrid();
        Controls.Add(_siteGrid);

        var addButton = CreateButton("新增", 24, 430, false);
        addButton.Click += (_, _) => BeginNewSite();
        Controls.Add(addButton);

        var deleteButton = CreateButton("删除", 118, 430, false);
        deleteButton.Click += (_, _) => DeleteSelectedSite();
        Controls.Add(deleteButton);

        var editorGroup = new GroupBox
        {
            Text = "网站登录信息",
            Bounds = new Rectangle(460, 52, 456, 410),
        };
        Controls.Add(editorGroup);

        _nameTextBox = AddTextField(editorGroup, "网站名称", 34);
        _addressTextBox = AddTextField(editorGroup, "网站地址", 82);

        editorGroup.Controls.Add(new Label
        {
            Text = "登录方式",
            Bounds = new Rectangle(24, 130, 82, 27),
            TextAlign = ContentAlignment.MiddleLeft,
        });
        _adapterComboBox = new ComboBox
        {
            Bounds = new Rectangle(116, 130, 312, 28),
            DropDownStyle = ComboBoxStyle.DropDownList,
            DisplayMember = nameof(BrowserAutoLoginAdapterOption.DisplayName),
            ValueMember = nameof(BrowserAutoLoginAdapterOption.AdapterType),
            DataSource = adapters.ToArray(),
        };
        editorGroup.Controls.Add(_adapterComboBox);

        _userNameTextBox = AddTextField(editorGroup, "账号", 178);
        _passwordTextBox = AddTextField(editorGroup, "密码", 226);
        _passwordTextBox.UseSystemPasswordChar = true;

        var showPassword = new CheckBox
        {
            Text = "显示密码",
            Bounds = new Rectangle(116, 262, 100, 26),
        };
        showPassword.CheckedChanged += (_, _) => _passwordTextBox.UseSystemPasswordChar = !showPassword.Checked;
        editorGroup.Controls.Add(showPassword);

        _enabledCheckBox = new CheckBox
        {
            Text = "启用此网站的自动登录",
            Bounds = new Rectangle(244, 262, 190, 26),
            Checked = true,
        };
        editorGroup.Controls.Add(_enabledCheckBox);

        var saveButton = CreateButton("保存配置", 242, 324, true);
        saveButton.Click += OnSaveClick;
        editorGroup.Controls.Add(saveButton);

        var resetButton = CreateButton("取消编辑", 336, 324, false);
        resetButton.Click += (_, _) => LoadSelectedSiteOrNew();
        editorGroup.Controls.Add(resetButton);

        var closeButton = CreateButton("关闭", 828, 477, true);
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
            Bounds = new Rectangle(24, 52, 412, 360),
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AutoGenerateColumns = false,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            MultiSelect = false,
            ReadOnly = true,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        };
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "网站名称",
            Width = 110,
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "网站地址",
            Width = 180,
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "账号",
            Width = 100,
        });
        grid.SelectionChanged += (_, _) =>
        {
            if (!_suppressSelection && SelectedSite is not null)
                LoadSite(SelectedSite);
        };
        return grid;
    }

    private static Button CreateButton(string text, int left, int top, bool primary)
    {
        var button = new Button
        {
            Text = text,
            Bounds = new Rectangle(left, top, 88, 34),
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

    private static TextBox AddTextField(Control parent, string label, int top)
    {
        parent.Controls.Add(new Label
        {
            Text = label,
            Bounds = new Rectangle(24, top, 82, 27),
            TextAlign = ContentAlignment.MiddleLeft,
        });
        var textBox = new TextBox
        {
            Bounds = new Rectangle(116, top, 312, 27),
        };
        parent.Controls.Add(textBox);
        return textBox;
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

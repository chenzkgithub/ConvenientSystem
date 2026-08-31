using System.Drawing;
using System.Windows.Forms;

namespace ConvenientSystem;

/// <summary>
/// 更新模式：Web 前端更新 / 桌面程序更新 / 两者同时更新。
/// </summary>
internal enum UpdateDialogMode
{
    /// <summary>仅 Web 前端版本更新（后台替换 wwwroot，无需重启）。</summary>
    WebOnly,
    /// <summary>仅桌面程序安装包更新（需要重启）。</summary>
    DesktopOnly,
    /// <summary>桌面程序和 Web 前端同时有更新，优先处理桌面更新。</summary>
    DesktopAndWeb,
}

/// <summary>
/// 统一更新对话框：支持 Web 前端更新、桌面程序更新、两者同时更新三种模式。
/// Web 更新完成后显示完成状态；桌面更新下载完成后启动安装程序并退出当前进程。
/// </summary>
internal sealed class UpdateDialog : Form
{
    private enum State { NewVersion, Updating, Complete, Error }

    private readonly UpdateDialogMode _mode;
    private readonly Func<IProgress<(int percent, string status)>, Task> _updateCallback;
    private readonly string _localVersion;
    private readonly string _remoteVersion;
    private readonly string? _description;
    private readonly WebUpdateInfo? _webUpdate;

    /// <summary>标记更新已结束（成功或失败），阻止 Progress 回调覆盖最终状态文本。</summary>
    private volatile bool _updateFinished;

    // UI 元素
    private readonly Panel _contentPanel;
    private Label _lblTitle = null!;
    private Label _lblVersionCompare = null!;
    private Label _lblWebUpdate = null!;
    private Label _lblDescription = null!;
    private ProgressBar _progressBar = null!;
    private Label _lblProgress = null!;
    private Button _btnUpdate = null!;
    private Button _btnLater = null!;
    private Button _btnOk = null!;

    public UpdateDialog(
        UpdateDialogMode mode,
        string localVersion,
        string remoteVersion,
        string? description,
        Func<IProgress<(int percent, string status)>, Task> updateCallback,
        WebUpdateInfo? webUpdate = null)
    {
        _mode = mode;
        _localVersion = localVersion;
        _remoteVersion = remoteVersion;
        _description = description;
        _updateCallback = updateCallback;
        _webUpdate = webUpdate;

        // 窗口设置
        Text = "检查更新";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(480, _mode == UpdateDialogMode.DesktopAndWeb ? 380 : 320);
        BackColor = Color.White;

        _contentPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24),
        };
        Controls.Add(_contentPanel);

        BuildUi();
        ShowState(State.NewVersion);
    }

    private void BuildUi()
    {
        // 标题
        _lblTitle = new Label
        {
            Text = "发现新版本！",
            Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Top,
            Height = 36,
        };
        _contentPanel.Controls.Add(_lblTitle);

        // 版本对比
        _lblVersionCompare = new Label
        {
            Font = new Font("Microsoft YaHei UI", 10F),
            ForeColor = Color.FromArgb(70, 70, 70),
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 30,
            Padding = new Padding(0, 6, 0, 0),
        };
        _contentPanel.Controls.Add(_lblVersionCompare);

        // Web 前端更新提示（仅在 DesktopAndWeb 模式显示）
        _lblWebUpdate = new Label
        {
            Font = new Font("Microsoft YaHei UI", 9F),
            ForeColor = Color.FromArgb(100, 100, 100),
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 44,
            Padding = new Padding(12, 4, 12, 0),
            Visible = false,
        };
        _contentPanel.Controls.Add(_lblWebUpdate);

        // 更新说明
        _lblDescription = new Label
        {
            Font = new Font("Microsoft YaHei UI", 9F),
            ForeColor = Color.FromArgb(100, 100, 100),
            TextAlign = ContentAlignment.TopLeft,
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 70,
            Padding = new Padding(12, 8, 12, 0),
        };
        _contentPanel.Controls.Add(_lblDescription);

        // 进度条
        _progressBar = new ProgressBar
        {
            Style = ProgressBarStyle.Continuous,
            Dock = DockStyle.Top,
            Height = 10,
            Visible = false,
        };
        _contentPanel.Controls.Add(_progressBar);

        // 进度文本
        _lblProgress = new Label
        {
            Font = new Font("Microsoft YaHei UI", 9F),
            ForeColor = Color.FromArgb(100, 100, 100),
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Top,
            Height = 24,
            Visible = false,
        };
        _contentPanel.Controls.Add(_lblProgress);

        // 按钮区域
        var btnPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 48,
        };
        _contentPanel.Controls.Add(btnPanel);

        // 立即更新 / 立即更新并重启
        _btnUpdate = new Button
        {
            Font = new Font("Microsoft YaHei UI", 9F),
            Size = new Size(140, 32),
            Anchor = AnchorStyles.Right,
            BackColor = Color.FromArgb(64, 158, 255),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
        };
        _btnUpdate.FlatAppearance.BorderSize = 0;
        _btnUpdate.Click += OnUpdateClick;

        // 以后再说
        _btnLater = new Button
        {
            Text = "以后再说",
            Font = new Font("Microsoft YaHei UI", 9F),
            Size = new Size(90, 32),
            Anchor = AnchorStyles.Right,
            FlatStyle = FlatStyle.Flat,
            DialogResult = DialogResult.Cancel,
        };
        _btnLater.FlatAppearance.BorderColor = Color.FromArgb(200, 200, 200);
        _btnLater.Click += (_, _) => Close();

        // 确定（完成/错误状态）
        _btnOk = new Button
        {
            Text = "确定",
            Font = new Font("Microsoft YaHei UI", 9F),
            Size = new Size(90, 32),
            Anchor = AnchorStyles.Right,
            FlatStyle = FlatStyle.Flat,
            Visible = false,
        };
        _btnOk.FlatAppearance.BorderColor = Color.FromArgb(200, 200, 200);
        _btnOk.Click += (_, _) => Close();

        // 布局按钮
        btnPanel.Layout += (_, _) =>
        {
            var y = (btnPanel.Height - 32) / 2;
            _btnOk.Location = new Point(btnPanel.Width - _btnOk.Width - 16, y);
            _btnLater.Location = new Point(btnPanel.Width - _btnLater.Width - 16, y);
            _btnUpdate.Location = new Point(btnPanel.Width - _btnUpdate.Width - _btnLater.Width - 28, y);
        };

        btnPanel.Controls.Add(_btnUpdate);
        btnPanel.Controls.Add(_btnLater);
        btnPanel.Controls.Add(_btnOk);
    }

    private void ShowState(State state)
    {
        _lblTitle.Visible = state == State.NewVersion || state == State.Complete || state == State.Error;
        _lblVersionCompare.Visible = state == State.NewVersion || state == State.Complete;
        _lblWebUpdate.Visible = state == State.NewVersion && _mode == UpdateDialogMode.DesktopAndWeb;
        _lblDescription.Visible = state == State.NewVersion;
        _progressBar.Visible = state == State.Updating;
        _lblProgress.Visible = state == State.Updating || state == State.Complete || state == State.Error;
        _btnUpdate.Visible = state == State.NewVersion;
        _btnLater.Visible = state == State.NewVersion;
        _btnOk.Visible = state == State.Complete || state == State.Error;

        switch (state)
        {
            case State.NewVersion:
                _lblTitle.Text = "发现新版本！";
                _lblTitle.ForeColor = Color.FromArgb(33, 33, 33);

                if (_mode == UpdateDialogMode.WebOnly)
                {
                    _btnUpdate.Text = "立即更新";
                    _lblVersionCompare.Text = $"当前版本  {_localVersion}  →  最新版本  {_remoteVersion}";
                    _lblDescription.Text = string.IsNullOrEmpty(_description) ? "" : $"更新内容：\n{_description}";
                }
                else if (_mode == UpdateDialogMode.DesktopOnly)
                {
                    _btnUpdate.Text = "立即更新并重启";
                    _lblVersionCompare.Text = $"桌面程序  {_localVersion}  →  {_remoteVersion}";
                    _lblDescription.Text = string.IsNullOrEmpty(_description) ? "" : $"更新内容：\n{_description}";
                }
                else // DesktopAndWeb
                {
                    _btnUpdate.Text = "立即更新并重启";
                    _lblVersionCompare.Text = $"桌面程序  {_localVersion}  →  {_remoteVersion}";
                    var webLocal = _webUpdate?.LocalVersion ?? "(未知)";
                    _lblWebUpdate.Text = $"Web 前端  {webLocal}  →  {_webUpdate?.Version}\nWeb 前端将在后台自动更新，重启后生效";
                    _lblDescription.Text = string.IsNullOrEmpty(_description) ? "" : $"桌面程序更新内容：\n{_description}";
                }
                break;
            case State.Updating:
                _progressBar.Value = 0;
                _lblProgress.Text = _mode == UpdateDialogMode.WebOnly ? "准备中..." : "正在准备下载安装程序...";
                break;
            case State.Complete:
                _lblTitle.Text = "更新完成！";
                _lblTitle.ForeColor = Color.FromArgb(76, 175, 80);
                _lblVersionCompare.Text = $"已更新到版本  {_remoteVersion}";
                _progressBar.Visible = false;
                _lblProgress.Text = "点击确定继续启动程序";
                _lblProgress.ForeColor = Color.FromArgb(76, 175, 80);
                break;
            case State.Error:
                _lblTitle.Text = "更新失败";
                _lblTitle.ForeColor = Color.FromArgb(244, 67, 54);
                _progressBar.Visible = false;
                _lblProgress.Text = "更新失败，请检查网络后重试或跳过本次更新";
                _lblProgress.ForeColor = Color.FromArgb(244, 67, 54);
                break;
        }
    }

    private async void OnUpdateClick(object? sender, EventArgs e)
    {
        ShowState(State.Updating);
        _btnUpdate.Enabled = false;
        _btnLater.Enabled = false;

        var progress = new Progress<(int percent, string status)>(p =>
        {
            if (_updateFinished) return; // 更新已结束，忽略迟到的进度回调
            _progressBar.Value = Math.Min(p.percent, 100);
            _lblProgress.Text = p.status;
        });

        try
        {
            await _updateCallback(progress);

            // 桌面更新：回调内部已启动安装程序并退出，不会执行到这里
            // Web 更新：显示完成状态
            _updateFinished = true;
            ShowState(State.Complete);
        }
        catch (Exception ex)
        {
            _updateFinished = true;
            ShowState(State.Error);
            _lblProgress.Text = $"更新失败：{ex.Message}";
        }
        finally
        {
            _btnUpdate.Enabled = true;
            _btnLater.Enabled = true;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // 更新进行中不允许关闭
        if (!_progressBar.Visible)
        {
            base.OnFormClosing(e);
        }
        else
        {
            e.Cancel = true;
        }
    }
}

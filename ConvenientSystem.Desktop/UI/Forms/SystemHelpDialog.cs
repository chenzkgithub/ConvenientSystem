using System.Drawing;
using System.Text.Json;
using System.Windows.Forms;

namespace ConvenientSystem;

/// <summary>
/// 系统帮助对话框：展示当前版本信息（桌面程序 / Web 前端 / 更新服务器），
/// 并提供手动「检查更新」入口。检查逻辑复用启动期同一套服务
/// （DesktopUpdateService / WebUpdateService），发现更新时关闭本窗并弹出
/// 统一更新对话框（UpdateDialog），行为与启动期检查保持一致。
/// </summary>
internal sealed class SystemHelpDialog : Form
{
    // 与 UpdateDialog 一致的视觉基调
    private static readonly Color TextPrimary = Color.FromArgb(33, 33, 33);
    private static readonly Color TextSecondary = Color.FromArgb(100, 100, 100);
    private static readonly Color TextSuccess = Color.FromArgb(76, 175, 80);
    private static readonly Color TextError = Color.FromArgb(244, 67, 54);

    private readonly string _remoteBaseUrl;
    private readonly string _desktopVersion;
    private readonly string _webVersion;

    private Label _lblStatus = null!;
    private Button _btnCheck = null!;

    public SystemHelpDialog(IWritableOptions<AppSettings> appSettings)
    {
        _remoteBaseUrl = ResolveRemoteBaseUrl(appSettings);
        // 桌面版本唯一真相：exe 内嵌的程序集版本（与 Program.ResolveDesktopVersion 同源）
        _desktopVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0.0";
        _webVersion = ReadWebVersion();

        Text = "系统帮助";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        TopMost = true;
        ClientSize = new Size(400, 228);
        BackColor = Color.White;
        Font = new Font("Microsoft YaHei UI", 9F);

        BuildUi();
    }

    private void BuildUi()
    {
        // 分隔线
        var separator = new Panel
        {
            BackColor = Color.FromArgb(238, 240, 243),
            Bounds = new Rectangle(48, 30, 304, 1),
        };
        Controls.Add(separator);

        // 版本信息三行（桌面程序 / Web 前端 / 更新服务器）
        AddInfoRow(48, 52, "桌面程序版本", "v" + _desktopVersion);
        AddInfoRow(48, 80, "Web 前端版本", "v" + _webVersion);
        AddInfoRow(48, 108, "更新服务器",
            string.IsNullOrEmpty(_remoteBaseUrl) ? "未配置" : _remoteBaseUrl.Replace("http://", string.Empty));

        // 状态行：检查更新的结果反馈
        _lblStatus = new Label
        {
            Font = new Font("Microsoft YaHei UI", 9F),
            ForeColor = TextSecondary,
            Bounds = new Rectangle(48, 140, 304, 20),
            AutoEllipsis = true,
        };
        Controls.Add(_lblStatus);

        // 检查更新按钮
        _btnCheck = new Button
        {
            Text = "检查更新",
            Bounds = new Rectangle(184, 176, 110, 32),
            BackColor = Color.FromArgb(64, 158, 255),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
        };
        _btnCheck.FlatAppearance.BorderSize = 0;
        _btnCheck.Click += OnCheckUpdateClick;
        Controls.Add(_btnCheck);

        // 关闭按钮（显式关闭事件：窗体以 Show 非模态打开，仅靠 DialogResult 不会自动关闭）
        var btnClose = new Button
        {
            Text = "关闭",
            Bounds = new Rectangle(306, 176, 75, 32),
            FlatStyle = FlatStyle.Flat,
            DialogResult = DialogResult.Cancel,
        };
        btnClose.FlatAppearance.BorderColor = Color.FromArgb(200, 200, 200);
        btnClose.Click += (_, _) => Close();
        Controls.Add(btnClose);
        CancelButton = btnClose;

        // 未配置更新服务器时禁用检查入口
        if (string.IsNullOrEmpty(_remoteBaseUrl))
        {
            _btnCheck.Enabled = false;
            _lblStatus.Text = "未配置更新服务器（appsettings.json → AppSettings:RemoteServerUrl）";
        }
    }

    private void AddInfoRow(int x, int y, string caption, string value)
    {
        var lblCaption = new Label
        {
            Text = caption,
            ForeColor = TextSecondary,
            Bounds = new Rectangle(x, y, 110, 20),
            TextAlign = ContentAlignment.MiddleLeft,
        };
        Controls.Add(lblCaption);

        var lblValue = new Label
        {
            Text = value,
            ForeColor = TextPrimary,
            Bounds = new Rectangle(x + 110, y, 194, 20),
            TextAlign = ContentAlignment.MiddleLeft,
        };
        Controls.Add(lblValue);
    }

    private async void OnCheckUpdateClick(object? sender, EventArgs e)
    {
        _btnCheck.Enabled = false;
        _lblStatus.ForeColor = TextSecondary;
        _lblStatus.Text = "正在检查更新...";

        try
        {
            var wwwrootDir = Path.Combine(AppContext.BaseDirectory, "wwwroot");
            var desktopUpdate = await DesktopUpdateService.CheckAsync(_remoteBaseUrl, _desktopVersion);
            var webUpdate = await WebUpdateService.PeekAsync(wwwrootDir, _remoteBaseUrl);

            if (desktopUpdate != null)
            {
                // 桌面更新优先：关闭帮助窗，弹统一更新对话框（下载回调与启动期完全一致）
                var mode = webUpdate != null ? UpdateDialogMode.DesktopAndWeb : UpdateDialogMode.DesktopOnly;
                using var dialog = new UpdateDialog(
                    mode,
                    _desktopVersion,
                    desktopUpdate.Version,
                    desktopUpdate.Description,
                    async progress =>
                    {
                        var setupPath = await DesktopUpdateService.DownloadAsync(_remoteBaseUrl, desktopUpdate, progress);
                        progress?.Report((98, "正在启动安装程序..."));
                        var setupProcess = DesktopUpdateService.LaunchInstaller(setupPath);

                        // 等待安装程序真正启动（UAC 授权、进程创建需要时间），避免当前进程退出过早导致安装程序也被系统回收。
                        await Task.Delay(2500);
                        try
                        {
                            setupProcess.Refresh();
                            if (setupProcess.HasExited)
                            {
                                progress?.Report((100, "安装程序未能正常启动，请手动运行安装包"));
                                await Task.Delay(2000);
                                return;
                            }
                        }
                        catch
                        {
                            // 无法访问进程句柄时视为已启动（权限隔离等情况）
                        }

                        progress?.Report((100, "安装程序已启动，即将关闭当前程序"));
                        await Task.Delay(800);
                        Environment.Exit(0);
                    },
                    webUpdate);

                Close();
                dialog.ShowDialog();
            }
            else if (webUpdate != null)
            {
                // 仅 Web 前端更新：关闭帮助窗，复用既有 Web 更新对话框（含下载进度与完成态）
                Close();
                await WebUpdateService.CheckAndShowDialogAsync(wwwrootDir, _remoteBaseUrl);
            }
            else
            {
                _lblStatus.ForeColor = TextSuccess;
                _lblStatus.Text = "当前已是最新版本";
            }
        }
        catch (Exception ex)
        {
            _lblStatus.ForeColor = TextError;
            _lblStatus.Text = $"检查失败：{ex.Message}";
        }
        finally
        {
            // 弹出更新对话框的分支里本窗已关闭，访问控件前先判 disposed
            if (!IsDisposed && !Disposing)
                _btnCheck.Enabled = true;
        }
    }

    /// <summary>读取本地 Web 前端版本（wwwroot/version.json），读不到时显示"未知"。</summary>
    private static string ReadWebVersion()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "wwwroot", "version.json");
            if (File.Exists(path))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.TryGetProperty("version", out var v) &&
                    v.GetString() is { Length: > 0 } version)
                    return version;
            }
        }
        catch
        {
            // version.json 损坏等异常按未知处理
        }
        return "未知";
    }

    /// <summary>从 appsettings.json 解析更新服务器基址，与 Program.cs 启动期同规则。</summary>
    private static string ResolveRemoteBaseUrl(IWritableOptions<AppSettings> appSettings)
    {
        try
        {
            var remote = appSettings.Value.RemoteServerUrl.Trim();
            return string.IsNullOrEmpty(remote) ? string.Empty : $"http://{remote.TrimEnd('/')}";
        }
        catch
        {
            return string.Empty;
        }
    }
}

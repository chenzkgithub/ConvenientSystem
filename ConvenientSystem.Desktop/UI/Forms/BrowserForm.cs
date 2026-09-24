using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ConvenientSystem;

/// <summary>
/// 应用内浏览器窗口：承载独立的 WebView2，用于打开第三方链接。
/// 与主窗口共享同一 WebView2 环境（相同用户数据目录），因此 Cookie / 登录态一致，
/// 且第三方页面作为顶层文档打开（第一方 Cookie），可正常登录、跳转，
/// 避免了 iframe 内嵌导致的第三方 Cookie 被拦截、X-Frame-Options 拒绝等问题。
///
/// 使用系统原生边框窗口（FormBorderStyle.Sizable），双击最大化/还原、拖拽、调整大小全部系统原生行为。
/// </summary>
public sealed class BrowserForm : Form, ILockable
{
    private readonly WebView2 _webView;

    // 锁屏遮罩：使用原生 LockOverlayControl 代替 WebView2，
    // 避免锁屏页加载 Vue 应用触发 API 调用（共享 localStorage 导致 401 清除全局登录态）。
    private LockOverlayControl _lockOverlay = null!;

    private readonly BrowserFormViewModel _viewModel = new();

    private readonly IServiceProvider _services;

    public BrowserForm(IServiceProvider services)
    {
        _services = services;
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

        // ── 系统原生边框窗口（双击最大化/还原、拖拽、调整大小全部系统原生行为） ──
        FormBorderStyle = FormBorderStyle.Sizable;

        // WebView2 填充客户区
        _webView = new WebView2 { Dock = DockStyle.Fill };
        Controls.Add(_webView);

        // 窗口关闭时从锁屏协调器注销，避免协调器持有已释放窗口。
        FormClosed += (_, _) => LockCoordinator.Unregister(this);
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
        _viewModel.FixedTitle = title;
        Text = title;
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
            if (!string.IsNullOrEmpty(_viewModel.FixedTitle)) return;
            var title = string.IsNullOrWhiteSpace(core.DocumentTitle) ? "浏览器" : core.DocumentTitle;
            Text = title;
        };

        // 页面内再次弹窗（window.open / target=_blank）继续在应用内新窗口打开
        core.NewWindowRequested += OnNewWindowRequested;

        // 放行 dingtalk:// 协议，支持钉钉客户端一键授权登录。
        ExternalUriSchemePolicy.Attach(core);

        // 权限放行策略：文件选择框不再弹“想要打开文件”提示，本机页面其余权限也直接放行。
        WebView2PermissionPolicy.Attach(core);

        // 网页请求关闭窗口（window.close）时关闭本窗体
        core.WindowCloseRequested += (_, _) =>
        {
            if (!IsDisposed) Close();
        };

        // 用户在本窗口内的操作不会传到主页面，为避免其"正在使用却被自动锁屏"，
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

            // 外部协议链接（如 dingtalk://）：由宿主直接启动，绕过 WebView2 外部协议策略
            if (root.TryGetProperty("type", out var msgType) && msgType.GetString() == "scheme:open"
                && root.TryGetProperty("url", out var schemeUrlEl)
                && schemeUrlEl.GetString() is { Length: > 0 } schemeUrl)
            {
                try { Process.Start(new ProcessStartInfo(schemeUrl) { UseShellExecute = true }); }
                catch { /* 启动失败时静默忽略 */ }
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

    /// <summary>强制重新计算 WebView2 尺寸。</summary>
    private void ForceLayoutRefresh()
    {
        if (IsDisposed || !IsHandleCreated) return;
        PerformLayout();
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
            var child = _services.GetRequiredService<BrowserForm>();
            // 先显示窗口，确保在前台（await 之后 Show 可能被前台锁阻止）
            child.SizeToWorkingArea();
            child.Show();
            child.Activate();
            await child.InitializeAsync(env);
            e.NewWindow = child.Core;
            e.Handled = true;
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

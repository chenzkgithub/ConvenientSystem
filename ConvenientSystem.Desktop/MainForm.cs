using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ConvenientSystem;

/// <summary>
/// 桌面宿主窗口：内嵌 WebView2 控件，加载本机 Kestrel 提供的前端页面。
/// </summary>
public sealed class MainForm : Form
{
    private readonly WebView2 _webView;
    private readonly string _startUrl;

    // 启动时直接显示主窗口
    private bool _allowVisible = true;

    // 共享的 WebView2 环境：内嵌浏览器与弹出窗口复用它以保持 Cookie / 登录态一致。
    private CoreWebView2Environment? _env;

    // 会话 Cookie 持久化：WebView2 默认只自动保存带过期时间的 Cookie，
    // 无过期的"会话 Cookie"在进程退出时会丢失（导致重启后需重新登录）。
    // 这里在退出时将会话 Cookie 写入文件，启动时回写，实现"像浏览器一样保持登录"。
    private string? _cookieFile;
    private bool _cookiesSaved;

    // 真正退出程序标志：用户点关闭按钮时改为隐藏到托盘，只有托盘"退出程序"才真的关闭。
    private bool _realExit;

    // 会话 Cookie 定时保存：退出时保存一次不够——强杀进程/断电时 OnFormClosing 不执行，
    // 会话 Cookie 丢失导致重新登录。改为每 5 分钟额外落盘一次，异常退出也能保住最近登录态。
    private System.Windows.Forms.Timer? _cookieSaveTimer;
    private bool _cookieSaving;
    
    // 独立窗口字典：按 page 路径索引已打开的 BrowserForm 窗口，避免同一菜单页重复打开。
    // 窗口关闭时自动从字典中移除。
    private readonly Dictionary<string, Form> _openPageWindows = new();

    // 系统托盘图标与右键菜单
    private NotifyIcon? _tray;
    private ContextMenuStrip? _trayMenu;
    
    // 悬浮按钮及其独立菜单
    private FloatingButton? _floatingBtn;
    // 前端上报的菜单树 JSON（悬浮按钮平铺展开卡片网格用）
    private JsonElement? _lastMenuTree;

    // 全局热键管理器：注册系统级热键（主窗口隐藏到托盘时仍能触发）
    private GlobalHotkeyManager? _hotkey;
    // 快速启动器：全局热键呼出的搜索式启动器
    private QuickLauncher? _launcher;
    // 程序索引服务：后台扫描开始菜单快捷方式
    private AppIndexService? _appIndex;
    // 启动器自定义条目存储
    private LauncherStore? _store;
    // 本地文件索引服务：后台全盘扫描，融入启动器搜索
    private FileIndexService? _fileIndex;
    public MainForm(string startUrl)
    {
        _startUrl = startUrl;

        Text = "ConvenientSystem";
        StartPosition = FormStartPosition.CenterScreen;
        // 启动时以合适尺寸居中显示（非全屏/最大化），用户可自行拖拽或最大化。
        WindowState = FormWindowState.Normal;
        Size = new Size(1280, 820);
        MinimumSize = new Size(1024, 700);
        // 启动时在任务栏显示
        ShowInTaskbar = true;

        // 窗口与任务栏图标：优先从嵌入程序集的 appicon.ico 加载（多尺寸、不受单文件发布/图标缓存影响），
        // 失败时再回退到提取 exe 自带图标。
        try
        {
            using var iconStream = typeof(MainForm).Assembly.GetManifestResourceStream("appicon.ico");
            if (iconStream is not null)
                Icon = new Icon(iconStream);
            else
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        catch
        {
            // 加载失败时忽略，使用默认图标
        }

        _webView = new WebView2 { Dock = DockStyle.Fill };
        Controls.Add(_webView);

        // 初始化系统托盘图标与右键菜单（页面项待前端上报后填充）。
        InitTray();

        // 初始化悬浮按钮：独立顶层窗口，与托盘共用同一菜单实例。
        InitFloatingButton();

        Load += OnFormLoadAsync;
    }

    private async void OnFormLoadAsync(object? sender, EventArgs e)
    {
        try
        {
            // WebView2 用户数据存放在 exe 目录下。
            var userDataFolder = Path.Combine(Application.StartupPath, "WebView2");
            Directory.CreateDirectory(userDataFolder);

            _env = await CoreWebView2Environment.CreateAsync(null, userDataFolder, new CoreWebView2EnvironmentOptions
            {
                // 禁用浏览器后台节能：隐藏的内嵌第三方页面可能被 Edge 休眠/内存回收，
                // 再次显示时会自动重新加载（表现为“切回菜单时页面在刷新”）。
                // 未知特性名会被 Chromium 忽略，多写无副作用。
                AdditionalBrowserArguments =
                    "--disable-features=msWebView2TabHibernation,TabHibernation,AutomaticTabDiscarding,TabDiscarding,TabSleep,msTabSleep,HighEfficiencyMode,CalculateNativeWinOcclusion " +
                    "--disable-renderer-backgrounding --disable-backgrounding-occluded-windows --disable-background-timer-throttling",
            });
            await _webView.EnsureCoreWebView2Async(_env);

            // 开放右键菜单与开发者工具（F12），便于日常使用与排查。
            _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            _webView.CoreWebView2.Settings.AreDevToolsEnabled = true;
            _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;

            // 第三方外链在新窗口/新标签打开时（target=_blank、window.open），
            // 在应用内独立浏览器窗口打开（共享同一环境，页面为第一方 Cookie，可正常登录）。
            _webView.CoreWebView2.NewWindowRequested += OnNewWindowRequested;

            // 外部协议唤起策略：放行 dingtalk://，支持钉钉客户端一键授权登录。
            ExternalUriSchemePolicy.Attach(_webView.CoreWebView2);

            // 接收前端（Vue）的消息：打开独立窗口、锁屏联动、菜单上报等。
            _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

            // 启动时回写上次保存的会话 Cookie（必须在首次导航前），实现登录态跨重启保持。
            _cookieFile = Path.Combine(userDataFolder, "session-cookies.json");
            await RestoreSessionCookiesAsync();

            // 定时保存会话 Cookie（每 5 分钟）：强杀/断电等异常退出时也能保住最近的登录态。
            _cookieSaveTimer = new System.Windows.Forms.Timer { Interval = 5 * 60 * 1000 };
            _cookieSaveTimer.Tick += async (_, _) => await SaveSessionCookiesSafeAsync();
            _cookieSaveTimer.Start();

            // 初始化全局锁屏协调器：提供解锁密码校验基址，以及“回发前端解锁/活动”的通道，
            // 使弹出浏览器窗口能与主页面锁屏状态保持一致（一处解锁，全部解锁）。
            LockCoordinator.Init(
                _startUrl,
                notifyWebUnlock: () => _webView.CoreWebView2?.PostWebMessageAsJson("{\"type\":\"host:unlock\"}"),
                notifyWebActivity: () => _webView.CoreWebView2?.PostWebMessageAsJson("{\"type\":\"host:activity\"}"));

            // 首次导航前清一次 WebView2 磁盘缓存（仅 HTTP 缓存，不含 Cookie/localStorage，登录态不受影响）：
            // 前端资源由本机 Kestrel 提供、重新拉取成本极低，但可彻底避免“发布新前端后 WebView2 仍复用旧缓存”
            // 导致的菜单丢失、按钮无响应等疑难问题（历史多次出现）。配合 index.html 的 no-cache 头形成双保险。
            try
            {
                await _webView.CoreWebView2.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.DiskCache);
            }
            catch { /* 清缓存失败不影响启动 */ }

            _webView.CoreWebView2.Navigate(_startUrl);

            // === 快速启动器 + 全局热键 ===
            _appIndex = new AppIndexService();
            _store = new LauncherStore(_startUrl, () => LockCoordinator.CachedJwt);
            _fileIndex = new FileIndexService();
            _launcher = new QuickLauncher(
                _appIndex, _store, _fileIndex,
                openPage: (page, title, ext) => OpenPageWindow(page, title, ext),
                openUrl: url => OpenPageWindow(url, url, true));
            if (_lastMenuTree is not null) _launcher.SetMenuTree(_lastMenuTree);
            _appIndex.StartIndexAsync(onCompleted: () =>
            {
                if (_launcher is not null && _launcher.Visible) _launcher.RefreshResults();
            });
            _fileIndex.StartIndexAsync(onCompleted: () =>
            {
                if (_launcher is not null && _launcher.Visible) _launcher.RefreshResults();
            });

            _hotkey = new GlobalHotkeyManager();
            if (!_hotkey.Register("Ctrl+Alt+Space", () => _launcher?.Popup()))
                LogHost("HOTKEY-FAIL Ctrl+Alt+Space（可能被其他程序占用）");
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "初始化 WebView2 失败，请先安装 Microsoft Edge WebView2 运行时。\n\n" +
                "下载地址：https://developer.microsoft.com/microsoft-edge/webview2/\n\n" +
                "错误信息：" + ex.Message,
                "启动失败",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            Close();
        }
    }

    /// <summary>
    /// 新窗口请求（target=_blank / window.open）：在应用内独立浏览器窗口打开。
    /// 新窗口复用主环境并作为顶层页面加载，Cookie 为第一方，第三方站点可正常登录与跳转。
    /// </summary>
    private async void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        if (_env is null) return;

        var deferral = e.GetDeferral();
        try
        {
            var browser = new BrowserForm();
            await browser.InitializeAsync(_env);
            // 把新建的内核交给引擎，由引擎自动导航到目标地址（保留 window.open 语义）。
            e.NewWindow = browser.Core;
            e.Handled = true;
            browser.Show();
            browser.SizeToWorkingArea();
        }
        catch
        {
            // 打开失败时忽略（如非法 URI 或内核初始化失败）
        }
        finally
        {
            deferral.Complete();
        }
    }

    /// <summary>
    /// 处理前端消息：打开独立窗口、锁屏联动、菜单上报。
    /// 消息格式（JSON）：
    ///   { type:"page:open", page, title, external }
    ///   { type:"host:lock" } / { type:"host:unlock" }
    ///   { type:"menu:list", items:[...] }
    /// </summary>
    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var typeEl)) return;
            var type = typeEl.GetString();

            switch (type)
            {
                case "page:open":
                    // 前端点击外链菜单：以独立窗口打开真实页面；
                    // 同一页面已有窗口时直接激活前置（与托盘/悬浮按钮菜单一致）。
                    if (root.TryGetProperty("page", out var openPageEl) &&
                        openPageEl.GetString()?.Trim() is { Length: > 0 } openPage)
                    {
                        var winTitle = root.TryGetProperty("title", out var winTitleEl)
                            ? winTitleEl.GetString() ?? string.Empty
                            : string.Empty;
                        bool openExternal = root.TryGetProperty("external", out var openExtEl) &&
                                            openExtEl.ValueKind == JsonValueKind.True;
                        OpenPageWindow(openPage, winTitle, openExternal);
                    }
                    break;

                case "host:lock":
                    // 前端锁屏（空闲计时 / 手动）：让所有弹出浏览器窗口一并显示锁屏遮罩。
                    // 同时缓存 JWT：弹出窗口若打开的是外部链接（不同域），
                    // 其 WebView2 读不到 localStorage 里的 token，解锁时需用此缓存值带认证。
                    _ = CacheJwtForLockAsync();
                    LockCoordinator.LockAll();
                    break;

                case "host:unlock":
                    // 前端在主页面解锁：同步隐藏所有弹出浏览器窗口的遮罩（无需再回发前端）。
                    LockCoordinator.UnlockAll(notifyWeb: false);
                    break;

                case "menu:list":
                    // 前端上报“首页所有页面”菜单树：据此重建托盘右键菜单。
                    RebuildTrayMenu(root.TryGetProperty("items", out var itemsEl) ? itemsEl : (JsonElement?)null);
                    // 登录成功后菜单树上报，此时缓存 JWT 并从数据库拉取启动器条目。
                    _ = ReloadLauncherFromApiAsync();
                    break;
            
                case "file:open":
                case "file:save":
                case "file:saveAs":
                case "file:openExplorer":
                case "file:openRecycleBin":
                    // 文件操作委托共享服务（BrowserForm 独立窗口也使用同一逻辑）
                    HostFileService.TryHandleMessage(root, _webView.CoreWebView2!, this);
                    break;

                case "scheme:open":
                    // 前端请求打开外部协议链接（如 dingtalk://）：绕过 WebView2 外部协议策略，由宿主直接启动
                    if (root.TryGetProperty("url", out var schemeUrlEl) &&
                        schemeUrlEl.GetString() is { Length: > 0 } schemeUrl)
                    {
                        try { Process.Start(new ProcessStartInfo(schemeUrl) { UseShellExecute = true }); }
                        catch (Exception ex) { LogHost($"SCHEME-OPEN FAIL: {ex.Message}"); }
                    }
                    break;

            }
        }
        catch (Exception ex)
        {
            // 消息格式异常/开窗失败时忽略，但记入诊断日志便于排查。
            LogHost($"MSG-FAIL ex={ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// 检查主窗口当前是否已登录（localStorage 中存在有效 token）。
    /// </summary>
    private async Task<bool> CheckLoginAsync()
    {
        try
        {
            var core = _webView.CoreWebView2;
            if (core is null) return false;
            var json = await core.ExecuteScriptAsync(
                "(() => { try { const s = localStorage.getItem('auth_state_v1'); if (!s) return false; const o = JSON.parse(s); return !!(o && o.token); } catch { return false; } })()");
            return json == "true";
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 从主窗口 WebView2 读取 JWT 并缓存到 LockCoordinator。
    /// 主窗口与前端同源，始终能读到 localStorage 里的 token；
    /// 弹出窗口若打开外部链接则不同源读不到，解锁时需用此缓存值。
    /// </summary>
    private async Task CacheJwtForLockAsync()
    {
        try
        {
            var json = await _webView.CoreWebView2.ExecuteScriptAsync(
                "(() => { try { const s = localStorage.getItem('auth_state_v1'); if (!s) return null; const o = JSON.parse(s); return o?.token || null; } catch { return null; } })()");
            LockCoordinator.CachedJwt = System.Text.Json.JsonSerializer.Deserialize<string>(json);
        }
        catch { /* 缓存失败不影响锁屏流程，弹出窗口仍可尝试自己读取 */ }
    }

    /// <summary>
    /// 登录成功后：缓存 JWT，从数据库拉取启动器条目覆盖本地，刷新启动器。
    /// </summary>
    private async Task ReloadLauncherFromApiAsync()
    {
        await CacheJwtForLockAsync();
        try
        {
            _store?.ReloadFromApi();
            _launcher?.RefreshResults();
        }
        catch { /* 启动器加载失败不影响主界面 */ }
    }

    /// <summary>宿主诊断日志通用入口：写入 exe 目录\host.log。</summary>
    private static void LogHost(string message)
    {
        try
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}";
            var path = Path.Combine(Application.StartupPath, "host.log");
            File.AppendAllText(path, line);
        }
        catch
        {
            // 诊断日志写入失败忽略
        }
    }

    /// <summary>
    /// 初始化悬浮按钮及其独立菜单。
    /// </summary>
    private void InitFloatingButton()
    {
        _floatingBtn = new FloatingButton(iconSize: 40)
        {
            DoubleClickAction = ActivateMainWindow,
            OpenPageAction = (page, title, external) => OpenPageWindow(page, title, external),
            LauncherPopupAction = () => _launcher?.Popup(),
            EntryEditorAction = () =>
            {
                if (_store is null) return;
                using var editor = new LauncherEntryEditor(_store);
                editor.ShowDialog();
                _launcher?.RefreshResults();
            },
            RefreshIndexAction = () => _fileIndex?.ForceRebuild(),
            CloseAllWindowsAction = () =>
            {
                var windows = _openPageWindows.Values.ToList();
                foreach (var win in windows) win.Close();
                _openPageWindows.Clear();
            },
            RestartAction = RestartApp,
            LogoutAction = LogoutApp,
            ExitAction = ExitApp,
            HasOpenWindowsFunc = () => _openPageWindows.Count > 0,
        };
        _floatingBtn.PositionAtScreenCorner();
        _floatingBtn.Show();
    }

    // ===================== 系统托盘图标与右键菜单 =====================

    /// <summary>初始化系统托盘图标及其右键菜单（页面项由前端上报后动态填充）。</summary>
    private void InitTray()
    {
        _trayMenu = new ContextMenuStrip
        {
            Renderer = new ModernMenuRenderer(),
        };
        _tray = new NotifyIcon
        {
            Icon = Icon ?? SystemIcons.Application,
            Text = "ConvenientSystem",
            Visible = true,
            ContextMenuStrip = _trayMenu,
        };
        // 双击托盘图标：显示并激活主窗口。
        _tray.DoubleClick += (_, _) => ActivateMainWindow();
        RebuildTrayMenu(null);
    }

    /// <summary>
    /// 重建托盘右键菜单，并同步悬浮按钮的卡片网格菜单数据。
    /// </summary>
    private void RebuildTrayMenu(JsonElement? itemsEl)
    {
        if (_trayMenu is null) return;

        // 克隆菜单树供悬浮按钮/启动器使用（脱离原 JsonDocument 生命周期）。
        var mergedTree = CloneMenuTree(itemsEl);
        _lastMenuTree = mergedTree;
        if (_floatingBtn is not null) _floatingBtn.MenuTree = mergedTree;
        if (_launcher is not null) _launcher.SetMenuTree(mergedTree);

        // ═══════ 托盘菜单 ═══════
        _trayMenu.Items.Clear();

        var showMainItem = new ToolStripMenuItem("打开主界面");
        showMainItem.Click += (_, _) => ActivateMainWindow();
        _trayMenu.Items.Add(showMainItem);

        int pageCount = BuildPageItems(_trayMenu, itemsEl);
        if (pageCount == 0)
            _trayMenu.Items.Add(new ToolStripMenuItem("（登录后显示页面）") { Enabled = false });
        _trayMenu.Items.Add(new ToolStripSeparator());

        // 显示悬浮按钮（仅托盘菜单有，悬浮按钮自身不需要）
        var showFloatItem = new ToolStripMenuItem("显示悬浮按钮");
        showFloatItem.Click += (_, _) =>
        {
            if (_floatingBtn is not null)
            {
                _floatingBtn.Show();
                _floatingBtn.TopMost = true;
            }
        };
        _trayMenu.Opening += (_, _) =>
        {
            bool visible = _floatingBtn is not null && _floatingBtn.Visible;
            showFloatItem.Enabled = !visible;
            showFloatItem.Visible = !visible;
        };
        _trayMenu.Items.Add(showFloatItem);

        var launcherItem = new ToolStripMenuItem("快速启动器  Ctrl+Alt+Space");
        launcherItem.Click += (_, _) => _launcher?.Popup();
        _trayMenu.Items.Add(launcherItem);

        var entryEditorItem = new ToolStripMenuItem("管理启动器条目");
        entryEditorItem.Click += (_, _) =>
        {
            if (_store is null) return;
            using var editor = new LauncherEntryEditor(_store);
            editor.ShowDialog();
            _launcher?.RefreshResults();
        };
        _trayMenu.Items.Add(entryEditorItem);

        AddCommonMenuItems(_trayMenu);
    }

    /// <summary>
    /// 克隆前端上报的菜单树，供悬浮按钮/启动器使用。
    /// 返回的 JsonElement 已 Clone()，脱离原 JsonDocument 生命周期。
    /// </summary>
    private static JsonElement? CloneMenuTree(JsonElement? itemsEl)
    {
        var arr = new JsonArray();
        if (itemsEl is { ValueKind: JsonValueKind.Array } existing)
        {
            foreach (var item in existing.EnumerateArray())
            {
                var node = JsonNode.Parse(item.GetRawText());
                if (node is not null) arr.Add(node);
            }
        }

        using var doc = JsonDocument.Parse(arr.ToJsonString());
        return doc.RootElement.Clone();
    }

    /// <summary>从 JSON 菜单树构建页面菜单项并添加到目标菜单，返回添加的页面项数量。</summary>
    /// <param name="filterFloat">为 true 时仅包含 float=true 的菜单项（用于悬浮按钮菜单）。</param>
    private int BuildPageItems(ContextMenuStrip menu, JsonElement? itemsEl, bool filterFloat = false)
    {
        int count = 0;
        if (itemsEl is { ValueKind: JsonValueKind.Array } arr)
        {
            foreach (var node in arr.EnumerateArray())
            {
                var item = BuildTrayMenuItem(node, filterFloat);
                if (item is not null) { menu.Items.Add(item); count++; }
            }
        }
        return count;
    }

    /// <summary>添加两套菜单共用的固定项：关闭所有弹窗 / 重启 / 退出登录 / 退出程序。</summary>
    private void AddCommonMenuItems(ContextMenuStrip menu)
    {
        // 末尾分隔线（避免与已有分隔线重复）
        if (menu.Items.Count > 0 && menu.Items[menu.Items.Count - 1] is not ToolStripSeparator)
            menu.Items.Add(new ToolStripSeparator());

        var closeAllItem = new ToolStripMenuItem("关闭所有弹窗");
        closeAllItem.Click += (_, _) =>
        {
            var windows = _openPageWindows.Values.ToList();
            foreach (var win in windows) win.Close();
            _openPageWindows.Clear();
        };
        menu.Opening += (_, _) =>
        {
            closeAllItem.Enabled = _openPageWindows.Count > 0;
        };
        menu.Items.Add(closeAllItem);

        var restartItem = new ToolStripMenuItem("重启");
        restartItem.Click += (_, _) => RestartApp();
        menu.Items.Add(restartItem);

        var logoutItem = new ToolStripMenuItem("退出登录");
        logoutItem.Click += (_, _) => LogoutApp();
        menu.Items.Add(logoutItem);

        var exitItem = new ToolStripMenuItem("退出程序");
        exitItem.Click += (_, _) => ExitApp();
        menu.Items.Add(exitItem);
    }

    /// <summary>把上报的单个菜单节点转换为托盘菜单项（递归处理分组与叶子）。</summary>
    /// <param name="filterFloat">为 true 时仅包含 float=true 的菜单项。</param>
    private ToolStripMenuItem? BuildTrayMenuItem(JsonElement node, bool filterFloat = false)
    {
        string title = node.TryGetProperty("title", out var t) ? (t.GetString() ?? string.Empty) : string.Empty;
        if (string.IsNullOrEmpty(title)) title = "(未命名)";

        // 读取 float 属性
        bool isFloat = node.TryGetProperty("float", out var floatEl) &&
                       floatEl.ValueKind == JsonValueKind.True;

        // 过滤模式：如果 filterFloat=true，且当前节点 float!=true，则跳过
        if (filterFloat && !isFloat)
        {
            // 但如果当前是分组菜单，检查其子节点是否有 float=true 的
            if (node.TryGetProperty("children", out var children) &&
                children.ValueKind == JsonValueKind.Array &&
                children.GetArrayLength() > 0)
            {
                // 分组菜单本身没有 float，但子菜单可能有，继续递归
            }
            else
            {
                return null; // 叶子节点且 float!=true，跳过
            }
        }

        var item = new ToolStripMenuItem(title);

        if (node.TryGetProperty("children", out var childrenArr) &&
            childrenArr.ValueKind == JsonValueKind.Array &&
            childrenArr.GetArrayLength() > 0)
        {
            foreach (var child in childrenArr.EnumerateArray())
            {
                var sub = BuildTrayMenuItem(child, filterFloat);
                if (sub is not null) item.DropDownItems.Add(sub);
            }

            // 过滤模式下，如果分组菜单没有子项，则整个分组不显示
            if (filterFloat && item.DropDownItems.Count == 0)
                return null;
        }
        else if (node.TryGetProperty("page", out var pageEl) &&
                 pageEl.ValueKind == JsonValueKind.String &&
                 pageEl.GetString() is { Length: > 0 } page)
        {
            bool isExternal = node.TryGetProperty("external", out var extEl) &&
                              extEl.ValueKind == JsonValueKind.True;
            item.Click += (_, _) => OpenPageWindow(page, title, isExternal);
        }
        else
        {
            item.Enabled = false; // 既无子项也无跳转目标
        }

        return item;
    }

    /// <summary>
    /// 在应用内独立窗口（BrowserForm）打开指定页面：外链直接打开真实 URL，
    /// 内部路由则打开本机应用地址的对应 hash 路由。共享同一 WebView2 环境（登录态/Cookie 一致）。
    /// 窗口标题固定为配置的菜单名，不随网页标题变化。
    /// 同一页面只允许打开一个窗口，已有窗口时直接激活并前置。
    /// 内部页面打开前会先检查登录态，未登录时激活主窗口并跳转到登录页，不打开独立窗口。
    /// </summary>
    private async void OpenPageWindow(string page, string title, bool external = false)
    {
        if (_env is null) return;

        // 内部页面需要先登录；外部链接直接打开。
        if (!external && !page.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !page.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            var isLoggedIn = await CheckLoginAsync();
            if (!isLoggedIn)
            {
                ActivateMainWindow();
                MessageBox.Show(this, "请先登录后再打开页面。", "未登录", MessageBoxButtons.OK, MessageBoxIcon.Information);
                // 尝试让主窗口前端跳转到登录页（若已加载）
                try { _webView.CoreWebView2?.Navigate(_startUrl); } catch { /* 忽略 */ }
                return;
            }
        }

        // 若该页面已有窗口且未释放，直接激活并前置，不重复打开。
        if (_openPageWindows.TryGetValue(page, out var existing) && !existing.IsDisposed)
        {
            if (existing.WindowState == FormWindowState.Minimized)
                existing.WindowState = FormWindowState.Normal;
            if (!existing.Visible) existing.Show();
            existing.Activate();
            existing.BringToFront();
            return;
        }

        var url = BuildPageUrl(page, external);
        var browser = new BrowserForm();
        if (!string.IsNullOrWhiteSpace(title)) browser.SetFixedTitle(title);

        // 先注册到字典并绑定关闭清理，防止 await 期间重复打开同一页面。
        _openPageWindows[page] = browser;
        browser.FormClosed += (_, _) => _openPageWindows.Remove(page);

        // 先显示窗口（此时显示“加载中…”），确保窗口立即出现在前台。
        // 不能在 await 之后才 Show()：await 会让出 UI 线程，恢复后调用 Show()
        // 时 Windows 前台锁会阻止新窗口获取焦点，导致窗口只在任务栏闪烁
        // 而不显示到前台（表现为“页面只打开在状态栏没有直接在窗口显示”）。
        browser.SizeToWorkingArea();
        browser.Show();
        browser.Activate();

        try
        {
            await browser.InitializeAsync(_env);
            if (browser.IsDisposed) return;
            browser.Core.Navigate(url);
        }
        catch
        {
            // 初始化或导航失败：关闭窗口（FormClosed 会自动从字典移除）
            if (!browser.IsDisposed) browser.Close();
        }
    }

    /// <summary>把菜单 page 解析为可导航的绝对地址：
    /// external=true 时原样返回外部 URL；
    /// 内部路由拼接本机基址的 hash 路由，并带 standalone=1 标记，让前端以“纯净窗口”模式打开（只渲染该页面、跳过登录与主框架）。</summary>
    private string BuildPageUrl(string page, bool external = false)
    {
        if (external ||
            page.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            page.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return page;

        var baseUrl = _startUrl.TrimEnd('/');
        var route = page.StartsWith('/') ? page : "/" + page;
        var sep = route.Contains('?') ? "&" : "?";
        return baseUrl + "/#" + route + sep + "standalone=1";
    }

    /// <summary>拦截窗口首次显示：启动时不弹出主界面，仅创建句柄（使 Load 事件触发、WebView2 初始化正常进行）。</summary>
    protected override void SetVisibleCore(bool value)
    {
        if (!_allowVisible)
        {
            value = false;
            if (!IsHandleCreated) CreateHandle();
        }
        base.SetVisibleCore(value);
    }

    /// <summary>显示并激活主窗口（若最小化则先还原）。</summary>
    private void ActivateMainWindow()
    {
        _allowVisible = true;
        ShowInTaskbar = true;
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        if (!Visible) Show();
        Activate();
        BringToFront();
    }

    /// <summary>重启应用：带 --restart 参数拉起新实例（其会等待本实例退出后再启动），随后关闭本实例。</summary>
    private void RestartApp()
    {
        try
        {
            var exe = Environment.ProcessPath ?? Application.ExecutablePath;
            Process.Start(new ProcessStartInfo(exe, "--restart") { UseShellExecute = true });
        }
        catch
        {
            return; // 启动新实例失败则保持当前实例运行
        }
        ExitApp();
    }

    /// <summary>关闭应用（触发关闭流程，保存会话 Cookie 后退出）。</summary>
    private void ExitApp()
    {
        _realExit = true;
        Close();
    }

    /// <summary>
    /// 退出登录：清空所有浏览数据（Cookie / localStorage / 缓存等）并删除持久化的会话 Cookie，
    /// 随后重新加载到起始页回到登录界面；程序继续运行。
    /// </summary>
    private async void LogoutApp()
    {
        var core = _webView.CoreWebView2;
        if (core is null) return;

        try
        {
            // 删除本地持久化的会话 Cookie 文件，避免其在下次启动时被回写。
            if (_cookieFile is not null && File.Exists(_cookieFile))
                File.Delete(_cookieFile);
        }
        catch
        {
            // 删除失败忽略
        }

        try
        {
            // 清空当前用户配置下的全部浏览数据（Cookie、localStorage、缓存等）。
            await core.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.AllProfile);
        }
        catch
        {
            // 清理失败忽略，仍继续重载
        }

        // localStorage 已清空 → 重新加载起始页会回到登录界面。
        try { core.Navigate(_startUrl); } catch { /* 导航失败忽略 */ }

        // 托盘页面项来自前端上报，登出后先复位为占位提示，待重新登录再由前端刷新。
        RebuildTrayMenu(null);
        ActivateMainWindow();
    }

    /// <summary>
    /// 窗口关闭前处理：
    /// - 用户点关闭按钮 / Alt+F4：取消关闭，保存会话 Cookie 后隐藏到托盘；
    /// - 托盘"退出程序"或系统关机：保存会话 Cookie 后真正关闭。
    /// </summary>
    protected override async void OnFormClosing(FormClosingEventArgs e)
    {
        // 用户主动关闭窗口时，默认行为改为隐藏到托盘而不是退出进程。
        if (!_realExit && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            HideToTray();
            _ = TrySaveSessionCookiesAsync();
            return;
        }

        if (!_cookiesSaved && _webView.CoreWebView2 is not null)
        {
            _cookiesSaved = true;
            _cookieSaveTimer?.Stop(); // 退出前停止定时保存，避免与最终保存并发写文件
            e.Cancel = true;
            try
            {
                await SaveSessionCookiesAsync();
            }
            catch
            {
                // 保存失败不阻止关闭
            }
            Close();
            return;
        }

        base.OnFormClosing(e);
    }

    /// <summary>尝试保存会话 Cookie（静默忽略异常，不影响后续定时保存）。</summary>
    private async Task TrySaveSessionCookiesAsync()
    {
        if (_cookieSaving || _webView.CoreWebView2 is null) return;
        _cookieSaving = true;
        _cookieSaveTimer?.Stop();
        try
        {
            await SaveSessionCookiesAsync();
        }
        catch
        {
            // 保存失败忽略；下次显示或真正退出时还会尝试。
        }
        finally
        {
            _cookieSaving = false;
            _cookieSaveTimer?.Start();
        }
    }

    /// <summary>隐藏主窗口到系统托盘。</summary>
    private void HideToTray()
    {
        _allowVisible = false;
        Hide();
        ShowInTaskbar = false;
        _tray?.ShowBalloonTip(2000, "ConvenientSystem", "程序已最小化到系统托盘，双击图标可打开主界面。", ToolTipIcon.Info);
    }

    /// <summary>窗口关闭后清理托盘图标（否则图标会残留在通知区域直到鼠标划过）及悬浮按钮。</summary>
    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _cookieSaveTimer?.Stop();
        _cookieSaveTimer?.Dispose();
        _cookieSaveTimer = null;

        if (_floatingBtn is not null)
        {
            _floatingBtn.Close();
            _floatingBtn.Dispose();
            _floatingBtn = null;
        }

        _hotkey?.Dispose();
        _launcher?.Dispose();

        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _tray = null;
        }
        _trayMenu?.Dispose();
        _trayMenu = null;
        base.OnFormClosed(e);
    }

    /// <summary>定时保存会话 Cookie：防重入，失败静默（等下个周期再试）。</summary>
    private async Task SaveSessionCookiesSafeAsync()
    {
        if (_cookieSaving || _cookiesSaved) return; // 正在保存或已进入退出保存流程时跳过
        _cookieSaving = true;
        try
        {
            await SaveSessionCookiesAsync();
        }
        catch
        {
            // 保存失败忽略，下个周期重试
        }
        finally
        {
            _cookieSaving = false;
        }
    }

    /// <summary>退出时将当前所有“会话 Cookie”（无过期时间）写入本地文件。</summary>
    private async Task SaveSessionCookiesAsync()
    {
        if (_cookieFile is null || _webView.CoreWebView2 is null) return;

        var manager = _webView.CoreWebView2.CookieManager;
        var cookies = await manager.GetCookiesAsync(null);

        var list = new List<CookieDto>();
        foreach (var c in cookies)
        {
            // 带过期时间的 Cookie 已由 WebView2 自动持久化，无需重复保存；只处理会话 Cookie。
            if (!c.IsSession) continue;
            list.Add(new CookieDto
            {
                Name = c.Name,
                Value = c.Value,
                Domain = c.Domain,
                Path = c.Path,
                IsSecure = c.IsSecure,
                IsHttpOnly = c.IsHttpOnly,
                SameSite = (int)c.SameSite,
            });
        }

        var json = JsonSerializer.Serialize(list);
        await File.WriteAllTextAsync(_cookieFile, json);
    }

    /// <summary>启动时回写上次保存的会话 Cookie，并赋予一个较长过期时间使其持久化。</summary>
    private async Task RestoreSessionCookiesAsync()
    {
        if (_cookieFile is null || _webView.CoreWebView2 is null) return;
        if (!File.Exists(_cookieFile)) return;

        var json = await File.ReadAllTextAsync(_cookieFile);
        List<CookieDto>? list;
        try
        {
            list = JsonSerializer.Deserialize<List<CookieDto>>(json);
        }
        catch
        {
            return;
        }
        if (list is null || list.Count == 0) return;

        var manager = _webView.CoreWebView2.CookieManager;
        // 回写为 30 天后过期的持久 Cookie。
        DateTime expires = DateTime.Now.AddDays(30);

        foreach (var dto in list)
        {
            if (string.IsNullOrEmpty(dto.Name)) continue;
            try
            {
                var cookie = manager.CreateCookie(
                    dto.Name,
                    dto.Value ?? string.Empty,
                    dto.Domain ?? string.Empty,
                    string.IsNullOrEmpty(dto.Path) ? "/" : dto.Path);
                cookie.IsSecure = dto.IsSecure;
                cookie.IsHttpOnly = dto.IsHttpOnly;
                cookie.SameSite = (CoreWebView2CookieSameSiteKind)dto.SameSite;
                cookie.Expires = expires;
                manager.AddOrUpdateCookie(cookie);
            }
            catch
            {
                // 单条 Cookie 回写失败时跳过
            }
        }
    }

    /// <summary>会话 Cookie 持久化的序列化结构。</summary>
    private sealed class CookieDto
    {
        public string Name { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string Domain { get; set; } = string.Empty;
        public string Path { get; set; } = "/";
        public bool IsSecure { get; set; }
        public bool IsHttpOnly { get; set; }
        public int SameSite { get; set; }
    }
}

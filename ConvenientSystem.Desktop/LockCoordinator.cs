using System.Text;
using System.Text.Json;

namespace ConvenientSystem;

/// <summary>
/// 可被锁屏协调器统一上锁/解锁的窗口目标（如应用内浏览器窗口 <see cref="BrowserForm"/>）。
/// </summary>
public interface ILockable
{
    /// <summary>显示本窗口的锁屏遮罩。</summary>
    void ShowLock();

    /// <summary>隐藏本窗口的锁屏遮罩。</summary>
    void HideLock();
}

/// <summary>
/// 全局锁屏协调器：作为“是否锁屏”的唯一真相，负责把锁定/解锁状态同步到所有窗口。
///
/// 锁屏发起方仅有主窗口内的前端（空闲计时 / “立即锁屏”按钮）：
///   前端锁屏 → 主窗口收到 host:lock → <see cref="LockAll"/> 让所有弹出浏览器窗口显示遮罩；
///   前端解锁 → 主窗口收到 host:unlock → <see cref="UnlockAll"/>（notifyWeb=false）隐藏所有遮罩；
///   某个弹出窗口内解锁 → <see cref="UnlockAll"/>（notifyWeb=true）隐藏所有遮罩，并回发 host:unlock
///     通知主窗口前端同步解除锁屏。
/// 锁定期间新打开的弹出窗口，会在 <see cref="Register"/> 时立即上锁。
/// </summary>
internal static class LockCoordinator
{
    private static readonly List<ILockable> _targets = new();
    private static readonly object _sync = new();
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

    private static string _baseUrl = string.Empty;
    private static Action? _notifyWebUnlock;
    private static Action? _notifyWebActivity;

    /// <summary>当前是否处于锁屏状态。</summary>
    public static bool IsLocked { get; private set; }

    /// <summary>
    /// 缓存的 JWT 令牌（锁屏时由主窗口写入）。
    /// 弹出窗口打开外部链接时，其 WebView2 在不同域下无法读到 localStorage 里的 token，
    /// 解锁时退而用此缓存值携带认证，确保后端能识别用户。
    /// </summary>
    public static string? CachedJwt { get; set; }

    /// <summary>
    /// 初始化协调器。
    /// </summary>
    /// <param name="baseUrl">本机 Web 服务基址（用于校验解锁密码）。</param>
    /// <param name="notifyWebUnlock">通知主窗口前端解除锁屏（回发 host:unlock）。</param>
    /// <param name="notifyWebActivity">通知主窗口前端“有活动”，用于重置空闲计时（回发 host:activity）。</param>
    public static void Init(string baseUrl, Action notifyWebUnlock, Action notifyWebActivity)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _notifyWebUnlock = notifyWebUnlock;
        _notifyWebActivity = notifyWebActivity;
    }

    /// <summary>注册一个可锁定窗口；若当前已锁屏，则立即对其上锁。</summary>
    public static void Register(ILockable target)
    {
        lock (_sync)
        {
            if (!_targets.Contains(target)) _targets.Add(target);
        }
        if (IsLocked) target.ShowLock();
    }

    /// <summary>注销一个可锁定窗口（窗口关闭时调用）。</summary>
    public static void Unregister(ILockable target)
    {
        lock (_sync) _targets.Remove(target);
    }

    /// <summary>锁定所有窗口（前端已锁屏时由主窗口调用）。</summary>
    public static void LockAll()
    {
        IsLocked = true;
        foreach (var t in Snapshot()) t.ShowLock();
    }

    /// <summary>
    /// 解锁所有窗口。
    /// </summary>
    /// <param name="notifyWeb">是否回发 host:unlock 通知主窗口前端（弹出窗口内解锁时为 true）。</param>
    public static void UnlockAll(bool notifyWeb)
    {
        IsLocked = false;
        foreach (var t in Snapshot()) t.HideLock();
        if (notifyWeb) _notifyWebUnlock?.Invoke();
    }

    /// <summary>弹出窗口内检测到用户活动时调用，转发给主窗口前端重置空闲计时。</summary>
    public static void NotifyActivity() => _notifyWebActivity?.Invoke();

    private static ILockable[] Snapshot()
    {
        lock (_sync) return _targets.ToArray();
    }

    /// <summary>调用本机 Web 服务校验解锁密码（复用前端相同的 /api/Common/Lock/VerifyUnlock 接口）。</summary>
    /// <param name="password">用户输入的解锁密码。</param>
    /// <param name="jwt">当前登录用户的 JWT 令牌（可选）：携带认证信息，
    /// 避免后端策略收紧（如全局 fallback policy）时解锁请求被拒绝。</param>
    public static async Task<bool> VerifyAsync(string password, string? jwt = null)
    {
        // 弹出窗口打开外部链接时，其 WebView2 读不到同源 localStorage 里的 token，
        // 此时退而使用主窗口在锁屏时缓存的 JWT，保证解锁请求始终带认证。
        var effectiveJwt = !string.IsNullOrEmpty(jwt) ? jwt : CachedJwt;
        // 路由由 BaseController 的模板 api/[area]/[controller]/[action] + [Area("Common")] 生成，
        // 必须带 /api/Common 前缀，与前端 lock.ts 调用的路径一致，否则 404 会被误报为解锁失败。
        var url = _baseUrl + "/api/Common/Lock/VerifyUnlock";
        var json = JsonSerializer.Serialize(new { password });
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        if (!string.IsNullOrEmpty(effectiveJwt))
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", effectiveJwt);
        using var resp = await _http.SendAsync(request);
        // 接口异常（如路由变更导致 404）时抛出，避免被误报为"密码错误"
        resp.EnsureSuccessStatusCode();

        var body = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("ok", out var ok) && ok.GetBoolean();
    }
}

using Microsoft.Web.WebView2.Core;

namespace ConvenientSystem;

/// <summary>
/// WebView2 权限请求放行策略。
///
/// 统一规则：仅放行本机回环来源（exe 内嵌 Kestrel 的 127.0.0.1 页面）的权限请求，
/// 文件选择、通知、剪贴板等不再弹权限提示；独立窗口中打开的外部站点保持
/// WebView2 默认提示，不静默授予敏感权限。
///
/// 注：当前 SDK 的 CoreWebView2PermissionKind 枚举没有 FilePicker 成员（Chromium
/// 的文件选择不走权限请求），且权限状态直接读写事件参数的 e.State（无 Handler 属性）。
/// </summary>
internal static class WebView2PermissionPolicy
{
    /// <summary>为指定内核挂接权限放行策略。</summary>
    public static void Attach(CoreWebView2 core)
    {
        core.PermissionRequested += (_, e) =>
        {
            try
            {
                if (IsLoopbackUri(e.Uri))
                {
                    e.State = CoreWebView2PermissionState.Allow;
                }
            }
            catch
            {
                // 页面销毁等竞态导致事件参数已失效时走默认提示，不影响功能
            }
        };
    }

    /// <summary>是否本机回环地址（http://127.0.0.1 / http://localhost）。</summary>
    private static bool IsLoopbackUri(string? uri)
    {
        if (string.IsNullOrEmpty(uri)) return false;
        try
        {
            return new Uri(uri).IsLoopback;
        }
        catch
        {
            return false;
        }
    }
}

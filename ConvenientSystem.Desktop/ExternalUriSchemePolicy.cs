using Microsoft.Web.WebView2.Core;

namespace ConvenientSystem;

/// <summary>
/// WebView2 外部协议（如 dingtalk://）唤起策略。
///
/// 统一规则：仅在用户主动触发（点击等）时放行，页面脚本静默唤起本机程序一律拦截。
/// 钉钉协议不再特殊放行：避免打开第三方页面时自动弹出钉钉客户端/扫码登录；
/// 用户主动点击页面上的“钉钉快捷登录”按钮时仍可正常唤起。
/// </summary>
internal static class ExternalUriSchemePolicy
{
    /// <summary>为指定内核挂接外部协议唤起策略。</summary>
    public static void Attach(CoreWebView2 core)
    {
        core.LaunchingExternalUriScheme += (_, e) =>
        {
            // 所有外部协议（含 dingtalk://）：非用户主动触发的一律拦截，去除自动登录弹窗。
            e.Cancel = !e.IsUserInitiated;
        };
    }
}

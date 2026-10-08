namespace ConvenientSystem.Shared.Common.Security
{
    /// <summary>
    /// 客户端平台标识常量与规范化：区分请求来自手机端还是 Web/桌面端。
    /// 用途：① 会话按平台分桶（跨端同时在线互不挤号）② 注册来源记录（SysUser.RegisterSource）。
    /// </summary>
    public static class ClientPlatform
    {
        /// <summary>Web/桌面端（默认值，兼容未携带标识的历史客户端）</summary>
        public const string Web = "web";

        /// <summary>手机端（uni-app App/H5）</summary>
        public const string App = "app";

        /// <summary>规范化平台标识：仅识别 "app"，其余（含 null/空/未知值）一律回退为 "web"。</summary>
        public static string Normalize(string? value)
            => string.Equals(value?.Trim(), App, StringComparison.OrdinalIgnoreCase) ? App : Web;
    }
}

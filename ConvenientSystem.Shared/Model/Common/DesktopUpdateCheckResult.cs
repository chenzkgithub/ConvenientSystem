namespace ConvenientSystem.Shared.Model.Common
{
    /// <summary>
    /// 桌面程序更新检查结果：桌面端启动时调用，判断是否需要下载新安装包。
    /// </summary>
    public class DesktopUpdateCheckResult
    {
        /// <summary>是否有可用更新</summary>
        public bool HasUpdate { get; set; }

        /// <summary>服务器最新版本号</summary>
        public string Version { get; set; } = string.Empty;

        /// <summary>更新说明</summary>
        public string? Description { get; set; }

        /// <summary>安装包文件大小（字节）</summary>
        public long FileSize { get; set; }

        /// <summary>下载地址（相对路径，由客户端补全 baseUrl）</summary>
        public string DownloadUrl { get; set; } = string.Empty;
    }
}

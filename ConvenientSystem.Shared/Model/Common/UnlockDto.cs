namespace ConvenientSystem.Shared.Model.Common
{
    /// <summary>
    /// 锁屏解锁校验请求（界面加密）
    /// </summary>
    public class UnlockDto
    {
        public string? password { get; set; }
    }

    /// <summary>
    /// 前端运行所需的客户端配置
    /// </summary>
    public class AppConfigDto
    {
        /// <summary>是否开启锁屏功能</summary>
        public bool EnableLock { get; set; }

        /// <summary>自动锁屏空闲时长（秒），缺省 120</summary>
        public int LockTimeout { get; set; } = 120;
    }

    /// <summary>
    /// 锁屏解锁校验结果
    /// </summary>
    public class UnlockVerifyDto
    {
        public bool Ok { get; set; }
    }
}

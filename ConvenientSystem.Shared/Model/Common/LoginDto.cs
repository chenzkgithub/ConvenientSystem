namespace ConvenientSystem.Shared.Model.Common
{
    /// <summary>
    /// 登录校验请求
    /// </summary>
    public class LoginDto
    {
        public string? account { get; set; }
        public string? password { get; set; }
    }

    /// <summary>
    /// 登录界面默认回填的账号密码
    /// </summary>
    public class LoginDefaultDto
    {
        public string Account { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    /// <summary>
    /// 登录校验结果
    /// </summary>
    public class LoginVerifyDto
    {
        public bool Ok { get; set; }
        /// <summary>登录成功的用户 Id（GUID；失败为空 Guid）。用于登录时刻写入在线追踪。</summary>
        public Guid UserId { get; set; }
        /// <summary>登录成功的账号（失败为 null）。</summary>
        public string? Account { get; set; }
        public string? DisplayName { get; set; }
        /// <summary>头像（data:image/...;base64）；用于登录后顶栏展示，不写入 JWT 以免令牌过大</summary>
        public string? Avatar { get; set; }
        /// <summary>登录成功签发的 JWT（失败为 null）</summary>
        public string? Token { get; set; }
        /// <summary>可见菜单权限码（菜单 Name），前端可用于按鈕级控制</summary>
        public List<string> MenuCodes { get; set; } = new();
        /// <summary>用户角色编码</summary>
        public List<string> Roles { get; set; } = new();
        /// <summary>失败原因码：account_disabled / wrong_password / account_not_found。Ok=true 时为 null。</summary>
        public string? Reason { get; set; }
        /// <summary>会话超时时间（分钟）：0 表示不自动退出，由系统配置 Security.SessionTimeoutMinutes 决定。</summary>
        public int SessionTimeoutMinutes { get; set; }
    }
    
    /// <summary>
    /// 心跳状态检查结果（前端轮询用，判断当前登录账号是否仍处于启用状态）。
    /// </summary>
    public class LoginStatusDto
    {
        public bool Enabled { get; set; }
    }
}

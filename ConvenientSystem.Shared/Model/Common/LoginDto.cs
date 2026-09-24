namespace ConvenientSystem.Shared.Model.Common
{
    /// <summary>
    /// 登录校验请求
    /// </summary>
    public class LoginDto
    {
        public string? account { get; set; }
        public string? password { get; set; }
        /// <summary>客户端平台标识：app=手机端；缺省/其他值按 web 处理（跨端会话互不挤号）</summary>
        public string? platform { get; set; }
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
        /// <summary>可见菜单权限码：Web/桌面端=PC 菜单码；手机端（platform=app）=SysUserAppPerm 白名单的 app-* 码，前端可用于入口显隐</summary>
        public List<string> MenuCodes { get; set; } = new();
        /// <summary>用户角色编码</summary>
        public List<string> Roles { get; set; } = new();
        /// <summary>失败原因码：account_disabled / wrong_password / account_not_found / no_app_permission（无 app-login，拒绝登录手机端）/ no_pc_permission（无任意启用角色，拒绝登录 PC 端）。Ok=true 时为 null。</summary>
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

        /// <summary>用户头像（JWT 不再嵌入头像，心跳接口代而从此字段返回给在线追踪器）。</summary>
        public string? Avatar { get; set; }

        /// <summary>显示名称（心跳接口用于更新在线追踪器）。</summary>
        public string? DisplayName { get; set; }
    }
}

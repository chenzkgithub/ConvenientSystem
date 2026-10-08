namespace ConvenientSystem.Shared.Common.Security
{
    /// <summary>
    /// 手机端权限点常量与目录（SysUserAppPerm.PermCode 的取值范围）。
    /// 两端权限完全独立：手机端功能可见性只由 SysUserAppPerm 白名单决定（无行=无权限），
    /// 与 PC 端角色/菜单权限互不影响；admin 恒全权限且不写表（防自锁）。
    /// 权限码只签发进手机端登录 JWT 的 menuCodes claim（PC 端 token 不含 app-* 码）。
    /// </summary>
    public static class AppPerm
    {
        /// <summary>准入：登录手机端（无此码拒绝登录 App，Web 登录不受影响）</summary>
        public const string Login = "app-login";

        /// <summary>消息：聊天会话与消息收发</summary>
        public const string Chat = "app-chat";

        /// <summary>好友：搜索添加好友与处理好友申请</summary>
        public const string Friends = "app-friends";

        /// <summary>通知：系统公告与通知提醒</summary>
        public const string Notice = "app-notice";

        /// <summary>仪表盘：业务数据趋势（手机端专用接口 /Common/MobileDashboard/*）</summary>
        public const string Dashboard = "app-dashboard";

        /// <summary>彩票：开奖走势、智能选号与选号记录</summary>
        public const string Lottery = "app-lottery";

        /// <summary>朋友圈（占位，功能开发中）</summary>
        public const string Moments = "app-moments";

        /// <summary>收藏（占位，功能开发中）</summary>
        public const string Favorites = "app-favorites";

        /// <summary>权限管理：手机端「权限管理」页入口（默认仅 admin 拥有）</summary>
        public const string PermissionManage = "app-permission-manage";

        /// <summary>用户管理：在手机端查看/新建/编辑用户、启停、重置密码与删除（对应 Web 端 user-manage 系列接口）</summary>
        public const string UserManage = "app-user-manage";

        /// <summary>角色管理：在手机端查看/新建/编辑角色、启停、删除与分配菜单权限（对应 Web 端 role-manage 系列接口）</summary>
        public const string RoleManage = "app-role-manage";

        /// <summary>
        /// 注册默认预置：基础四项（登录/消息/好友/通知）。
        /// 三条建号路径统一预置：Web 注册、手机端注册、管理端新建（UserManageService.SaveUser）。
        /// </summary>
        public static readonly string[] DefaultGrant = { Login, Chat, Friends, Notice };

        /// <summary>全部权限点（admin 恒全量；管理页动态下发的全集）。</summary>
        public static readonly string[] All =
        {
            Login, Chat, Friends, Notice, Dashboard, Lottery, Moments, Favorites, PermissionManage, UserManage, RoleManage,
        };

        /// <summary>
        /// 权限点目录（Code/名称/说明/分组）：手机端权限管理页动态下发开关清单，
        /// 新增权限点只需在此加常量 + 目录行（分组建议填 基础/沟通/业务/管理，留空前端归“其他”），
        /// 手机端管理页自动出现，无需改前端。
        /// </summary>
        public static readonly (string Code, string Name, string Description, string Group)[] Catalog =
        {
            (Login, "登录手机端", "允许使用手机端 App 登录", "基础"),
            (Chat, "消息", "聊天会话与消息收发", "沟通"),
            (Friends, "好友", "搜索添加好友与处理好友申请", "沟通"),
            (Notice, "通知", "系统公告与通知提醒", "沟通"),
            (Dashboard, "仪表盘", "业务数据趋势一览", "业务"),
            (Lottery, "彩票", "开奖走势、智能选号与选号记录", "业务"),
            (Moments, "朋友圈", "同事动态（开发中）", "业务"),
            (Favorites, "收藏", "内容收藏（开发中）", "业务"),
            (PermissionManage, "权限管理", "在手机端为同事开通/关闭以上功能", "管理"),
            (UserManage, "用户管理", "查看/新建/编辑用户、启停、重置密码与删除", "管理"),
            (RoleManage, "角色管理", "查看/新建/编辑角色与分配菜单权限", "管理"),
        };
    }
}

namespace ConvenientSystem.Shared.Model.Common
{
    /// <summary>
    /// 手机端权限点（管理页动态下发的开关清单，来源 AppPerm.Catalog）。
    /// 新增权限点由后端目录自动出现，手机端管理页无需发版。
    /// </summary>
    public class AppPermPointDto
    {
        /// <summary>权限点码（app-*）</summary>
        public string Code { get; set; } = string.Empty;

        /// <summary>显示名称（如"消息"）</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>功能说明（开关行副文案）</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>分组名（基础/沟通/业务/管理；管理页分组展示，空值前端归“其他”）</summary>
        public string Group { get; set; } = string.Empty;
    }

    /// <summary>
    /// 手机端权限管理页的用户条目（含各自已开通权限码）。
    /// </summary>
    public class AppPermUserDto
    {
        public Guid UserId { get; set; }
        public string Account { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>头像（data:image/...;base64）</summary>
        public string? Avatar { get; set; }

        /// <summary>账号是否启用（停用用户行展示"已停用"标签）</summary>
        public bool Enabled { get; set; }

        /// <summary>是否管理员（任一启用角色 IsAdmin，或内置 admin 账号）：恒有全部手机端权限，页面应禁止编辑</summary>
        public bool IsAdmin { get; set; }

        /// <summary>已开通权限码（管理员恒返回全量）</summary>
        public List<string> PermCodes { get; set; } = new();
    }

    /// <summary>
    /// 保存用户手机端权限（差量更新：新增插行、移除删行，审计字段保留）。
    /// </summary>
    public class AppPermSaveDto
    {
        public Guid UserId { get; set; }

        /// <summary>目标权限码全集（空列表=清空该用户全部手机端权限）</summary>
        public List<string>? PermCodes { get; set; }
    }
}

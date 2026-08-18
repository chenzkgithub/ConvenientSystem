namespace ConvenientSystem.Shared.Model.Common
{
    /// <summary>用户列表项（含所属角色）。</summary>
    public class UserManageDto
    {
        public Guid Id { get; set; }
        public string Account { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        /// <summary>头像：data:image/...;base64 内联图片</summary>
        public string? Avatar { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? Remark { get; set; }
        public bool Enabled { get; set; }
        public DateTime CreateTime { get; set; }
        public List<int> RoleIds { get; set; } = new();
        public List<string> RoleNames { get; set; } = new();
    }

    /// <summary>新增/编辑用户请求。Id 为空 Guid 为新增（必须带密码）；编辑时 Password 留空表示不改。</summary>
    public class UserSaveDto
    {
        public Guid Id { get; set; }
        public string Account { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        public string? Password { get; set; }
        /// <summary>头像：data:image/...;base64；空表示无头像</summary>
        public string? Avatar { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? Remark { get; set; }
        public bool Enabled { get; set; } = true;
        public List<int> RoleIds { get; set; } = new();
    }

    /// <summary>重置密码请求。</summary>
    public class ResetPasswordDto
    {
        public Guid Id { get; set; }
        public string Password { get; set; } = string.Empty;
    }

    /// <summary>启停用户请求。</summary>
    public class SetEnabledDto
    {
        public Guid Id { get; set; }
        public bool Enabled { get; set; }
    }

    /// <summary>启停角色请求（角色主键仍为 int）。</summary>
    public class RoleSetEnabledDto
    {
        public int Id { get; set; }
        public bool Enabled { get; set; }
    }

    /// <summary>角色列表项（含可见菜单 Id）。</summary>
    public class RoleDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool Enabled { get; set; }
        public bool IsAdmin { get; set; }
        /// <summary>数据范围：0=本人 1=全部</summary>
        public int DataScope { get; set; }
        public DateTime CreateTime { get; set; }
        public List<int> MenuIds { get; set; } = new();
    }

    /// <summary>新增/编辑角色请求（含分配的菜单 Id）。</summary>
    public class RoleSaveDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool Enabled { get; set; } = true;
        public bool IsAdmin { get; set; }
        /// <summary>数据范围：0=本人 1=全部</summary>
        public int DataScope { get; set; }
        public List<int> MenuIds { get; set; } = new();
    }

    /// <summary>菜单扁平项（供角色分配菜单的树选择）。</summary>
    public class MenuFlatDto
    {
        public int Id { get; set; }
        public int? ParentId { get; set; }
        public string Title { get; set; } = string.Empty;
        /// <summary>节点类型：0=Group，1=Page，2=Button</summary>
        public byte Type { get; set; }
    }

    /// <summary>权限设置：单独更新角色可见菜单与视图权限点，不修改角色基本信息。</summary>
    public class RolePermissionsDto
    {
        public int RoleId { get; set; }
        public List<int> MenuIds { get; set; } = new();
        /// <summary>角色被授权的视图权限点 Id 列表（独立于菜单授权）</summary>
        public List<int> ViewPermIds { get; set; } = new();
    }

    /// <summary>角色 + 该角色下的用户列表（供权限设置左侧树）。</summary>
    public class RoleWithUsersDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool Enabled { get; set; }
        public bool IsAdmin { get; set; }
        public List<int> MenuIds { get; set; } = new();
        /// <summary>角色已授权的视图权限点 Id 列表</summary>
        public List<int> ViewPermIds { get; set; } = new();
        public List<UserBriefDto> Users { get; set; } = new();
    }

    /// <summary>用户简要信息（角色树叶子）。</summary>
    public class UserBriefDto
    {
        public Guid Id { get; set; }
        public string Account { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        public string? Avatar { get; set; }
        public bool Enabled { get; set; }
    }

    /// <summary>用户级权限保存请求。</summary>
    public class UserPermissionsDto
    {
        public Guid UserId { get; set; }
        public List<int> MenuIds { get; set; } = new();
        /// <summary>用户被授权的视图权限点 Id 列表（独立于菜单授权）</summary>
        public List<int> ViewPermIds { get; set; } = new();
    }

    /// <summary>在线用户列表项。</summary>
    public class OnlineUserDto
    {
        public Guid UserId { get; set; }
        public string Account { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        public string Ip { get; set; } = string.Empty;
        public DateTime LoginTime { get; set; }
        public DateTime LastSeen { get; set; }
    }
}

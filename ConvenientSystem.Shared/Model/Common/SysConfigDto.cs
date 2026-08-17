namespace ConvenientSystem.Shared.Model.Common
{
    /// <summary>单条配置项 DTO（前端展示用）</summary>
    public class SysConfigItemDto
    {
        public int Id { get; set; }
        public string ConfigKey { get; set; } = string.Empty;
        public string ConfigValue { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string InputType { get; set; } = "text";
        public string TabGroup { get; set; } = "system";
        public int SortOrder { get; set; }
    }

    /// <summary>分组配置 DTO（按 Category 聚合）</summary>
    public class SysConfigGroupDto
    {
        public string Category { get; set; } = string.Empty;
        public List<SysConfigItemDto> Items { get; set; } = new();
    }

    /// <summary>批量更新配置请求</summary>
    public class SysConfigUpdateDto
    {
        public string ConfigKey { get; set; } = string.Empty;
        public string ConfigValue { get; set; } = string.Empty;
    }

    /// <summary>查看敏感配置明文请求（需验证用户登录密码）</summary>
    public class SysConfigRevealDto
    {
        public string ConfigKey { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    /// <summary>查看敏感配置明文响应</summary>
    public class SysConfigRevealResult
    {
        public bool Ok { get; set; }
        public string? Value { get; set; }
    }
}

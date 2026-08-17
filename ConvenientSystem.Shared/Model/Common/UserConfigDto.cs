namespace ConvenientSystem.Shared.Model.Common
{
    /// <summary>单条用户配置项 DTO（前端展示用，合并全局默认值 + 用户覆盖值）</summary>
    public class UserConfigItemDto
    {
        public string ConfigKey { get; set; } = string.Empty;
        public string ConfigValue { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string InputType { get; set; } = "text";
        public string Category { get; set; } = string.Empty;
        public int SortOrder { get; set; }
    }

    /// <summary>分组配置 DTO（按 Category 聚合）</summary>
    public class UserConfigGroupDto
    {
        public string Category { get; set; } = string.Empty;
        public List<UserConfigItemDto> Items { get; set; } = new();
    }

    /// <summary>批量更新用户配置请求</summary>
    public class UserConfigSaveDto
    {
        public string ConfigKey { get; set; } = string.Empty;
        public string ConfigValue { get; set; } = string.Empty;
    }
}

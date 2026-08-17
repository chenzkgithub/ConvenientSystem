namespace ConvenientSystem.Shared.Model.Common
{
    /// <summary>公开页面列表/详情 DTO</summary>
    public class SysPublicPageItemDto
    {
        public int Id { get; set; }
        public string PageKey { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Component { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool Enabled { get; set; }
        public int SortOrder { get; set; }
    }

    /// <summary>新增公开页面请求</summary>
    public class SysPublicPageCreateDto
    {
        public string PageKey { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Component { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool Enabled { get; set; } = true;
        public int SortOrder { get; set; }
    }

    /// <summary>编辑公开页面请求</summary>
    public class SysPublicPageUpdateDto
    {
        public int Id { get; set; }
        public string PageKey { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Component { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool Enabled { get; set; } = true;
        public int SortOrder { get; set; }
    }
}

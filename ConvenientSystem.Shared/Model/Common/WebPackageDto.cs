namespace ConvenientSystem.Shared.Model.Common
{
    /// <summary>Web 前端版本包 DTO</summary>
    public class WebPackageDto
    {
        public int Id { get; set; }
        public string Version { get; set; } = "";
        public long FileSize { get; set; }
        public string? Description { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreateTime { get; set; }
        public string? CreatedByName { get; set; }
    }

    /// <summary>激活版本请求</summary>
    public class WebPackageActivateDto
    {
        public int Id { get; set; }
    }

    /// <summary>编辑版本信息请求</summary>
    public class WebPackageUpdateDto
    {
        public int Id { get; set; }
        public string Version { get; set; } = "";
        public string? Description { get; set; }
    }
}

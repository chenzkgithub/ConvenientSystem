namespace ConvenientSystem.Shared.Model.Common
{
    /// <summary>
    /// 桌面安装包版本 DTO：管理端列表展示与上传响应。
    /// </summary>
    public class DesktopPackageDto
    {
        /// <summary>安装包 Id</summary>
        public int Id { get; set; }

        /// <summary>版本号</summary>
        public string Version { get; set; } = string.Empty;

        /// <summary>服务器端存储文件名</summary>
        public string FileName { get; set; } = string.Empty;

        /// <summary>文件大小（字节）</summary>
        public long FileSize { get; set; }

        /// <summary>更新说明</summary>
        public string? Description { get; set; }

        /// <summary>是否为当前激活版本</summary>
        public bool IsActive { get; set; }

        /// <summary>上传人显示名</summary>
        public string? CreatedByName { get; set; }

        /// <summary>上传时间</summary>
        public DateTime CreateTime { get; set; }
    }
}

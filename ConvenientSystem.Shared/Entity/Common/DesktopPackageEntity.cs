using FreeSql.DataAnnotations;

namespace ConvenientSystem.Shared.Entity.Common
{
    /// <summary>
    /// 桌面安装包版本表（见 db/init.sql dbo.DesktopPackage）。
    /// 用于桌面客户端自更新：管理员上传 Setup.exe，激活后桌面端启动时检查并下载安装。
    /// </summary>
    [Table(Name = "DesktopPackage")]
    public class DesktopPackageEntity
    {
        [Column(IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>版本号（语义化版本，如 1.2.0）</summary>
        public string Version { get; set; } = string.Empty;

        /// <summary>服务器端存储文件名</summary>
        public string FileName { get; set; } = string.Empty;

        /// <summary>文件大小（字节）</summary>
        public long FileSize { get; set; }

        /// <summary>更新说明</summary>
        [Column(StringLength = -1)]
        public string? Description { get; set; }

        /// <summary>是否为当前激活版本</summary>
        public bool IsActive { get; set; }

        /// <summary>上传人用户 Id</summary>
        public Guid? CreatedById { get; set; }

        [Column(CanInsert = false, CanUpdate = false)]
        public DateTime CreateTime { get; set; }

        [Column(IsIgnore = true)]
        public string? CreatedByName { get; set; }
    }
}

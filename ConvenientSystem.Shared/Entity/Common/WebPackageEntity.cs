using FreeSql.DataAnnotations;

namespace ConvenientSystem.Shared.Entity.Common
{
    /// <summary>
    /// Web 前端版本包（桌面客户端更新用）。
    /// 管理员通过 Web 版本管理页面上传 zip 压缩包，桌面端启动时检查并下载激活版本。
    /// </summary>
    [Table(Name = "WebPackage")]
    public class WebPackageEntity
    {
        [Column(IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>版本号（如 1.0.0）</summary>
        public string Version { get; set; } = string.Empty;

        /// <summary>存储文件名</summary>
        public string FileName { get; set; } = string.Empty;

        /// <summary>文件大小（字节）</summary>
        public long FileSize { get; set; }

        /// <summary>更新说明</summary>
        public string? Description { get; set; }

        /// <summary>是否当前激活版本（桌面端下载此版本）</summary>
        public bool IsActive { get; set; }

        /// <summary>上传时间</summary>
        public DateTime CreateTime { get; set; } = DateTime.Now;

        /// <summary>上传人用户 Id（GUID，关联 SysUser.Id）</summary>
        public Guid? CreatedById { get; set; }
    }
}

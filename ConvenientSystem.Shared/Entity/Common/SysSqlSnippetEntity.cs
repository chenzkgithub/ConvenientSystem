using FreeSql.DataAnnotations;

namespace ConvenientSystem.Shared.Entity.Common
{
    /// <summary>
    /// SQL 快捷输入表（本地配置库 ConvenientSystem，见 db/init.sql）
    /// </summary>
    [Table(Name = "SysSqlSnippet")]
    public class SysSqlSnippetEntity
    {
        [Column(IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>快捷输入缩写（如 sf），唯一约束</summary>
        public string Shortcut { get; set; } = string.Empty;

        /// <summary>展开内容（如 SELECT * FROM ）</summary>
        public string Expansion { get; set; } = string.Empty;

        /// <summary>备注说明</summary>
        public string? Remark { get; set; }

        /// <summary>排序号</summary>
        public int SortOrder { get; set; }

        /// <summary>创建时间（数据库默认 GETDATE()）</summary>
        [Column(CanInsert = false, CanUpdate = false)]
        public DateTime CreateTime { get; set; }

        /// <summary>创建人用户 Id（SysUser.Id，GUID；用于数据权限过滤）</summary>
        public Guid? CreatedById { get; set; }
    }
}

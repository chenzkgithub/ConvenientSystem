using FreeSql.DataAnnotations;

namespace ConvenientSystem.Shared.Entity.Common
{
    /// <summary>
    /// SQL 查询收藏表（本地配置库 ConvenientSystem）
    /// </summary>
    [Table(Name = "SysSqlFavorite")]
    public class SysSqlFavoriteEntity
    {
        [Column(IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>收藏名称</summary>
        [Column(StringLength = 100)]
        public string Name { get; set; } = string.Empty;

        /// <summary>SQL 内容</summary>
        [Column(StringLength = -1)]
        public string SqlContent { get; set; } = string.Empty;

        /// <summary>备注说明</summary>
        [Column(StringLength = 500)]
        public string? Remark { get; set; }

        /// <summary>绑定的数据源名称（可选）</summary>
        [Column(StringLength = 100)]
        public string? DataSource { get; set; }

        /// <summary>排序号</summary>
        public int SortOrder { get; set; }

        /// <summary>创建时间（数据库默认 GETDATE()）</summary>
        [Column(CanInsert = false, CanUpdate = false)]
        public DateTime CreateTime { get; set; }

        /// <summary>创建人用户 Id（SysUser.Id，GUID；用于数据权限过滤）</summary>
        public Guid? CreatedById { get; set; }
    }
}

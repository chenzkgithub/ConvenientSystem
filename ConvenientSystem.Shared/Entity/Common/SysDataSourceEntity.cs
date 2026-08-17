using FreeSql.DataAnnotations;

namespace ConvenientSystem.Shared.Entity.Common
{
    /// <summary>
    /// SQL 查询工具数据源表（本地配置库 ConvenientSystem，见 db/init.sql）
    /// </summary>
    [Table(Name = "dbo.SysDataSource")]
    public class SysDataSourceEntity
    {
        [Column(IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>数据源显示名（唯一约束 UQ_SysDataSource_Name）</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>连接字符串</summary>
        public string ConnectionString { get; set; } = string.Empty;

        /// <summary>数据库类型：sqlserver/mysql/postgresql/oracle/sqlite/clickhouse</summary>
        public string DbType { get; set; } = "sqlserver";

        /// <summary>创建时间（数据库默认 GETDATE()）</summary>
        [Column(CanInsert = false, CanUpdate = false)]
        public DateTime CreateTime { get; set; }

        /// <summary>创建人用户 Id（SysUser.Id，GUID；用于数据权限过滤）</summary>
        public Guid? CreatedById { get; set; }
    }
}

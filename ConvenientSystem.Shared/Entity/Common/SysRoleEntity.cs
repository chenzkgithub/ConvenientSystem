using FreeSql.DataAnnotations;

namespace ConvenientSystem.Shared.Entity.Common
{
    /// <summary>
    /// 角色表（本地配置库 ConvenientSystem，见 db/init.sql）。Code=admin 为超级管理员，全通。
    /// </summary>
    [Table(Name = "SysRole")]
    public class SysRoleEntity
    {
        [Column(IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>角色名称</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>角色编码（唯一；admin 为超级管理员）</summary>
        public string Code { get; set; } = string.Empty;

        /// <summary>描述</summary>
        public string? Description { get; set; }

        /// <summary>是否启用</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>是否为管理员，可看所有用户数据</summary>
        public bool IsAdmin { get; set; }

        /// <summary>数据范围：0=本人 1=全部</summary>
        public int DataScope { get; set; }

        [Column(CanInsert = false, CanUpdate = false)]
        public DateTime CreateTime { get; set; }
    }
}

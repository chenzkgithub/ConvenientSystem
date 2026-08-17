using FreeSql.DataAnnotations;

namespace ConvenientSystem.Shared.Entity.Common
{
    /// <summary>
    /// 用户-角色关联表（本地配置库 ConvenientSystem，见 db/init.sql）。
    /// </summary>
    [Table(Name = "dbo.SysUserRole")]
    public class SysUserRoleEntity
    {
        [Column(IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>用户 Id（SysUser.Id，GUID）</summary>
        public Guid UserId { get; set; }

        public int RoleId { get; set; }
    }
}

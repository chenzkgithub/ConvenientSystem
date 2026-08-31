using FreeSql.DataAnnotations;

namespace ConvenientSystem.Shared.Entity.Common
{
    /// <summary>角色 → 视图权限点 授权映射</summary>
    [Table(Name = "SysRoleViewPerm")]
    public class SysRoleViewPermEntity
    {
        [Column(IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        public int RoleId { get; set; }

        /// <summary>视图权限点 Id（SysViewPermission.Id）</summary>
        public int ViewPermId { get; set; }
    }
}

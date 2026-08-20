using FreeSql.DataAnnotations;

namespace ConvenientSystem.Shared.Entity.Common
{
    /// <summary>
    /// 角色-菜单关联表（本地配置库 ConvenientSystem，见 db/init.sql）。
    /// 既决定角色可见哪些菜单，也作为接口鉴权的权限码来源（菜单 Name）。
    /// </summary>
    [Table(Name = "SysRoleMenu")]
    public class SysRoleMenuEntity
    {
        [Column(IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        public int RoleId { get; set; }

        public int MenuId { get; set; }
    }
}

using FreeSql.DataAnnotations;

namespace ConvenientSystem.Shared.Entity.Common
{
    /// <summary>
    /// 用户菜单授权表（本地配置库 ConvenientSystem，见 db/init.sql）。
    /// 在角色权限之外为用户额外授予菜单（加法模型），登录时与角色菜单取并集。
    /// </summary>
    [Table(Name = "SysUserMenu")]
    public class SysUserMenuEntity
    {
        [Column(IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>用户 Id（SysUser.Id，GUID）</summary>
        public Guid UserId { get; set; }

        /// <summary>菜单 Id（SysMenu.Id）</summary>
        public int MenuId { get; set; }
    }
}

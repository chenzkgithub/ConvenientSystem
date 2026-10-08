using ConvenientSystem.Shared.Common;
using FreeSql.DataAnnotations;

namespace ConvenientSystem.Shared.Entity.Common
{
    /// <summary>
    /// 手机端权限授权表（白名单行式存储：一行=一个用户的一个权限点，无行=无权限）。
    /// 与 PC 端角色/菜单权限完全独立；权限码取值见 AppPerm 常量（app-*）。
    /// 内置 admin 不写表、恒读全量（防自锁）。
    /// </summary>
    [Table(Name = "SysUserAppPerm")]
    public class SysUserAppPermEntity
    {
        /// <summary>用户 Id（SysUser.Id，GUID）</summary>
        [Column(IsPrimary = true)]
        public Guid UserId { get; set; }

        /// <summary>权限点码（app-login/app-chat/...，见 AppPerm 常量）</summary>
        [Column(IsPrimary = true)]
        public string PermCode { get; set; } = string.Empty;

        public DateTime CreateTime { get; set; } = TimeHelper.Now;

        /// <summary>开通人 Id（审计：谁在何时给谁开的权限；自助注册时等于本人）</summary>
        public Guid? CreatedById { get; set; }
    }
}

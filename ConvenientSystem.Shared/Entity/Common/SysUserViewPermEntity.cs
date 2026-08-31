using FreeSql.DataAnnotations;

namespace ConvenientSystem.Shared.Entity.Common
{
    /// <summary>用户 → 视图权限点 授权映射（用户级额外授权）</summary>
    [Table(Name = "SysUserViewPerm")]
    public class SysUserViewPermEntity
    {
        [Column(IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        public Guid UserId { get; set; }

        /// <summary>视图权限点 Id（SysViewPermission.Id）</summary>
        public int ViewPermId { get; set; }
    }
}

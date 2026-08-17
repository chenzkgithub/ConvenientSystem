using FreeSql.DataAnnotations;

namespace ConvenientSystem.Shared.Entity.Common
{
    /// <summary>
    /// 通知定向发送角色表（本地配置库 ConvenientSystem，见 db/init.sql）。
    /// 角色内全部用户可见该通知；与用户定向取并集，两表均无记录时默认全部人员可见。
    /// </summary>
    [Table(Name = "dbo.SysNoticeRole")]
    public class SysNoticeRoleEntity
    {
        [Column(IsPrimary = true, IsIdentity = true)]
        public long Id { get; set; }

        /// <summary>关联通知 Id（SysNotice.Id）</summary>
        public int NoticeId { get; set; }

        /// <summary>定向接收角色 Id（SysRole.Id）</summary>
        public int RoleId { get; set; }
    }
}

using FreeSql.DataAnnotations;

namespace ConvenientSystem.Shared.Entity.Common
{
    /// <summary>
    /// 通知定向发送用户表（本地配置库 ConvenientSystem，见 db/init.sql）。
    /// 某通知无定向记录（用户表与角色表均无）时默认发送给全部人员。
    /// </summary>
    [Table(Name = "SysNoticeUser")]
    public class SysNoticeUserEntity
    {
        [Column(IsPrimary = true, IsIdentity = true)]
        public long Id { get; set; }

        /// <summary>关联通知 Id（SysNotice.Id）</summary>
        public int NoticeId { get; set; }

        /// <summary>定向接收用户 Id（SysUser.Id，GUID）</summary>
        public Guid UserId { get; set; }
    }
}

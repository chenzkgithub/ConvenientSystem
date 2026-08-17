using FreeSql.DataAnnotations;

namespace ConvenientSystem.Shared.Entity.Common
{
    /// <summary>
    /// 通知已读记录表（见 db/init.sql dbo.SysNoticeRead）。
    /// 每用户每通知至多一条记录（NoticeId + UserId 唯一）。
    /// </summary>
    [Table(Name = "dbo.SysNoticeRead")]
    public class SysNoticeReadEntity
    {
        [Column(IsPrimary = true, IsIdentity = true)]
        public long Id { get; set; }

        /// <summary>关联通知 Id</summary>
        public int NoticeId { get; set; }

        /// <summary>已读用户 Id（关联 SysUser.Id）</summary>
        public Guid UserId { get; set; }

        /// <summary>阅读时间</summary>
        public DateTime ReadTime { get; set; } = DateTime.Now;
    }
}

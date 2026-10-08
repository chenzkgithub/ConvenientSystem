using ConvenientSystem.Shared.Common;
using FreeSql.DataAnnotations;

namespace ConvenientSystem.Shared.Entity.Common
{
    /// <summary>好友关系表（双行模式：A 与 B 成为好友即成对插入 A→B 与 B→A 两行，事务保证成对；见 db/init-app.sql）。</summary>
    [Table(Name = "ChatFriendship")]
    public class ChatFriendshipEntity
    {
        [Column(IsPrimary = true, IsIdentity = true)]
        public long Id { get; set; }

        /// <summary>关系持有方（关联 SysUser.Id）。</summary>
        public Guid UserId { get; set; }

        /// <summary>好友用户 Id（关联 SysUser.Id）。</summary>
        public Guid FriendId { get; set; }

        /// <summary>我给对方设置的备注名（双行模式各存各的，空=未设置）。</summary>
        public string? Remark { get; set; }

        public DateTime CreateTime { get; set; } = TimeHelper.Now;
    }

    /// <summary>
    /// 好友申请表：方向明确（From→To），同一对待处理申请靠过滤唯一索引防重（见建表语句）。
    /// 状态：0=待处理 1=已同意（双方互为好友） 2=已拒绝。
    /// </summary>
    [Table(Name = "ChatFriendRequest")]
    public class ChatFriendRequestEntity
    {
        [Column(IsPrimary = true, IsIdentity = true)]
        public long Id { get; set; }

        /// <summary>申请人（关联 SysUser.Id）。</summary>
        public Guid FromUserId { get; set; }

        /// <summary>接收人（关联 SysUser.Id）。</summary>
        public Guid ToUserId { get; set; }

        /// <summary>验证留言（可选，最长 100 字）。</summary>
        public string? Message { get; set; }

        /// <summary>状态：0=待处理 1=已同意 2=已拒绝。</summary>
        public int Status { get; set; }

        /// <summary>处理时间（待处理为空）。</summary>
        public DateTime? HandleTime { get; set; }

        public DateTime CreateTime { get; set; } = TimeHelper.Now;
    }
}

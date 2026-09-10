using FreeSql.DataAnnotations;

namespace ConvenientSystem.Shared.Entity.Common
{
    /// <summary>聊天会话表（本地配置库 ConvenientSystem，见 db/init.sql / db/migrate-add-chat.sql）。</summary>
    [Table(Name = "ChatConversation")]
    public class ChatConversationEntity
    {
        [Column(IsPrimary = true, IsIdentity = true)]
        public long Id { get; set; }

        /// <summary>双向归一键：两个用户 Guid 排序后拼接，A→B 与 B→A 同一会话（UQ 唯一）。</summary>
        public string UserKey { get; set; } = string.Empty;

        /// <summary>最后一条消息 Id（0=尚无消息）。</summary>
        public long LastMessageId { get; set; }

        /// <summary>最后一条消息发送者（未读判断：非我发送且 Id &gt; 我的已读水位）。</summary>
        public Guid? LastSenderId { get; set; }

        /// <summary>最后一条消息时间。</summary>
        public DateTime? LastMessageTime { get; set; }

        /// <summary>最后一条消息预览（会话列表展示，超长截断）。</summary>
        public string? LastMessageText { get; set; }

        public DateTime CreateTime { get; set; } = DateTime.Now;
    }

    /// <summary>聊天会话成员表：每会话两条成员记录，含已读水位 / 免打扰 / 隐藏。</summary>
    [Table(Name = "ChatConversationMember")]
    public class ChatConversationMemberEntity
    {
        [Column(IsPrimary = true, IsIdentity = true)]
        public long Id { get; set; }

        /// <summary>关联 ChatConversation.Id。</summary>
        public long ConversationId { get; set; }

        /// <summary>成员用户 Id（关联 SysUser.Id）。</summary>
        public Guid UserId { get; set; }

        /// <summary>已读水位：已读到的最后消息 Id（未读数 = 水位之后且非我发送的消息数）。</summary>
        public long ReadMessageId { get; set; }

        /// <summary>免打扰（仍计未读，前端不提醒）。</summary>
        public bool Muted { get; set; }

        /// <summary>会话隐藏：删除会话即隐藏，收到新消息自动恢复显示。</summary>
        public bool Hidden { get; set; }

        public DateTime CreateTime { get; set; } = DateTime.Now;
    }

    /// <summary>聊天消息表（按会话 + Id 索引，倒序分页拉取历史）。</summary>
    [Table(Name = "ChatMessage")]
    public class ChatMessageEntity
    {
        [Column(IsPrimary = true, IsIdentity = true)]
        public long Id { get; set; }

        /// <summary>关联 ChatConversation.Id。</summary>
        public long ConversationId { get; set; }

        /// <summary>发送者用户 Id（关联 SysUser.Id）。</summary>
        public Guid SenderId { get; set; }

        /// <summary>消息正文（纯文本，超长截断）。</summary>
        public string Content { get; set; } = string.Empty;

        public DateTime CreateTime { get; set; } = DateTime.Now;
    }

    /// <summary>聊天屏蔽名单（通讯录模式下以屏蔽代替好友关系控制；任一方屏蔽则双向拒收）。</summary>
    [Table(Name = "ChatBlockList")]
    public class ChatBlockListEntity
    {
        [Column(IsPrimary = true, IsIdentity = true)]
        public long Id { get; set; }

        /// <summary>屏蔽发起人 Id（关联 SysUser.Id）。</summary>
        public Guid UserId { get; set; }

        /// <summary>被屏蔽人 Id（关联 SysUser.Id）。</summary>
        public Guid BlockedUserId { get; set; }

        public DateTime CreateTime { get; set; } = DateTime.Now;
    }
}

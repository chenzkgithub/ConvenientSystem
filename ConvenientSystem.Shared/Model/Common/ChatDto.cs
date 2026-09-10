namespace ConvenientSystem.Shared.Model.Common
{
    /// <summary>会话列表项：我方视角（对方信息 + 最后一条消息 + 未读数 + 屏蔽标记）。</summary>
    public class ChatConversationDto
    {
        public long ConversationId { get; set; }

        /// <summary>对方用户 Id。</summary>
        public Guid PeerId { get; set; }

        /// <summary>对方账号。</summary>
        public string PeerAccount { get; set; } = string.Empty;

        /// <summary>对方显示名称。</summary>
        public string? PeerDisplayName { get; set; }

        /// <summary>对方头像（data:image/...;base64）。</summary>
        public string? PeerAvatar { get; set; }

        /// <summary>最后一条消息预览。</summary>
        public string? LastMessageText { get; set; }

        /// <summary>最后一条消息时间。</summary>
        public DateTime? LastMessageTime { get; set; }

        /// <summary>最后一条消息是否我发送的（前端决定预览前缀"我："）。</summary>
        public bool LastFromMe { get; set; }

        /// <summary>未读数（水位之后且非我发送的消息数）。</summary>
        public int UnreadCount { get; set; }

        /// <summary>我是否免打扰该会话。</summary>
        public bool Muted { get; set; }

        /// <summary>我是否屏蔽了对方（列表与聊天区提示用）。</summary>
        public bool BlockedByMe { get; set; }

        /// <summary>对方是否屏蔽了我（双向拒收提示用）。</summary>
        public bool BlockedMe { get; set; }
    }

    /// <summary>通讯录联系人（含屏蔽标记；在线状态由 Api 层结合 OnlineUserTracker 填充）。</summary>
    public class ChatContactDto
    {
        public Guid UserId { get; set; }

        public string Account { get; set; } = string.Empty;

        public string? DisplayName { get; set; }

        public string? Avatar { get; set; }

        /// <summary>是否在线（Api 层填充）。</summary>
        public bool Online { get; set; }

        /// <summary>我是否屏蔽了对方。</summary>
        public bool BlockedByMe { get; set; }

        /// <summary>对方是否屏蔽了我。</summary>
        public bool BlockedMe { get; set; }
    }

    /// <summary>聊天消息（含发送者信息；IsMine 由前端按当前用户判断）。</summary>
    public class ChatMessageDto
    {
        public long Id { get; set; }

        public long ConversationId { get; set; }

        public Guid SenderId { get; set; }

        public string SenderAccount { get; set; } = string.Empty;

        public string? SenderDisplayName { get; set; }

        public string Content { get; set; } = string.Empty;

        public DateTime CreateTime { get; set; }
    }

    /// <summary>发送消息请求：指定对方用户（会话由双向归一键自动定位/创建）。</summary>
    public class ChatSendRequest
    {
        public Guid PeerId { get; set; }

        public string Content { get; set; } = string.Empty;
    }

    /// <summary>标记已读请求：把会话已读水位推进到指定消息 Id。</summary>
    public class ChatMarkReadRequest
    {
        public long ConversationId { get; set; }

        public long MessageId { get; set; }
    }

    /// <summary>总未读数（顶栏红点轮询）。</summary>
    public class ChatUnreadDto
    {
        public int Count { get; set; }
    }

    /// <summary>屏蔽名单项（我屏蔽的用户，含显示信息）。</summary>
    public class ChatBlockDto
    {
        public Guid UserId { get; set; }

        public string Account { get; set; } = string.Empty;

        public string? DisplayName { get; set; }

        public string? Avatar { get; set; }

        public DateTime CreateTime { get; set; }
    }

    /// <summary>打开会话结果：会话 Id + 对方信息（在线状态由 Api 层填充）。</summary>
    public class ChatOpenDto
    {
        public long ConversationId { get; set; }

        public Guid PeerId { get; set; }

        public string PeerAccount { get; set; } = string.Empty;

        public string? PeerDisplayName { get; set; }

        public string? PeerAvatar { get; set; }

        /// <summary>对方是否屏蔽了我（双向拒收提示用）。</summary>
        public bool BlockedMe { get; set; }

        /// <summary>我是否屏蔽了对方。</summary>
        public bool BlockedByMe { get; set; }

        /// <summary>对方已读水位（对方成员记录的 ReadMessageId；0=对方尚未读过任何消息）。</summary>
        public long PeerReadMessageId { get; set; }
    }
}

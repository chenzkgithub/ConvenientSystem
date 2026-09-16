namespace ConvenientSystem.Shared.Model.Common
{
    /// <summary>会话列表项：我方视角（对方信息 + 最后一条消息 + 未读数 + 屏蔽标记）。</summary>
    public class ChatConversationDto
    {
        public long ConversationId { get; set; }
    
        /// <summary>会话类型：0=单聊 1=群聊。</summary>
        public int ConversationType { get; set; }
    
        /// <summary>单聊：对方用户 Id；群聊：Guid.Empty。</summary>
        public Guid PeerId { get; set; }
    
        /// <summary>单聊：对方账号；群聊：空。</summary>
        public string PeerAccount { get; set; } = string.Empty;
    
        /// <summary>单聊：对方显示名称；群聊：空。</summary>
        public string? PeerDisplayName { get; set; }
    
        /// <summary>单聊：对方头像；群聊：群头像。</summary>
        public string? PeerAvatar { get; set; }
    
        /// <summary>群聊名称（单聊为空）。</summary>
        public string? GroupName { get; set; }
    
        /// <summary>群成员数（单聊为 2）。</summary>
        public int MemberCount { get; set; }
    
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
    
        /// <summary>单聊：我是否屏蔽了对方（列表与聊天区提示用）。</summary>
        public bool BlockedByMe { get; set; }
    
        /// <summary>单聊：对方是否屏蔽了我（双向拒收提示用）。</summary>
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

    /// <summary>聊天消息（含发送者信息；是否我发送由前端按 SenderId 判断）。</summary>
    public class ChatMessageDto
    {
        public long Id { get; set; }

        public long ConversationId { get; set; }

        public Guid SenderId { get; set; }

        public string SenderAccount { get; set; } = string.Empty;

        public string? SenderDisplayName { get; set; }

        /// <summary>消息正文：文本为纯文本；图片为相对路径；合并转发卡片为标题。</summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>消息类型：0=文本 1=图片 2=合并转发记录卡片。</summary>
        public int MsgType { get; set; }

        /// <summary>引用的原消息 Id（0=无引用）。</summary>
        public long QuoteId { get; set; }

        /// <summary>引用内容快照（原消息被单方面删除后仍可显示）。</summary>
        public string? QuoteText { get; set; }

        /// <summary>被引用消息的发送者 Id。</summary>
        public Guid? QuoteSenderId { get; set; }

        /// <summary>被引用消息的发送者显示名（引用块“xxx：”用）。</summary>
        public string? QuoteSenderName { get; set; }

        /// <summary>合并转发卡片指向的记录 Id（MsgType=2 时有效）。</summary>
        public long? RefRecordId { get; set; }

        /// <summary>被@的用户 Id 列表。</summary>
        public List<string> Mentions { get; set; } = new();

        public DateTime CreateTime { get; set; }
    }

    /// <summary>发送消息请求：单聊指定 PeerId（会话由双向归一键自动定位/创建）；群聊 PeerId 为空并传 ConversationId。</summary>
    public class ChatSendRequest
    {
        /// <summary>单聊：对方用户 Id；群聊：Guid.Empty。</summary>
        public Guid PeerId { get; set; }

        /// <summary>群聊消息须指定会话 Id；单聊传 0。</summary>
        public long ConversationId { get; set; }

        public string Content { get; set; } = string.Empty;

        /// <summary>消息类型：0=文本 1=图片（客户端只允许这两类；2=合并转发卡片仅服务端生成）。</summary>
        public int MsgType { get; set; }

        /// <summary>引用的原消息 Id（0=不引用）。</summary>
        public long QuoteId { get; set; }

        /// <summary>@提及的用户 Id 列表（群聊用；单聊可为空）。</summary>
        public List<string> Mentions { get; set; } = new();
    }

    /// <summary>转发消息请求：逐条或合并转发到目标联系人。</summary>
    public class ChatForwardRequest
    {
        /// <summary>要转发的源消息 Id 列表（须属同一会话且我方可见）。</summary>
        public List<long> MessageIds { get; set; } = new();

        /// <summary>目标联系人用户 Id。</summary>
        public Guid TargetPeerId { get; set; }

        /// <summary>true=合并为一条“聊天记录”卡片；false=逐条各发一条。</summary>
        public bool Merged { get; set; }
    }

    /// <summary>合并转发记录查看结果（只读快照）。</summary>
    public class ChatForwardRecordDto
    {
        public long Id { get; set; }

        /// <summary>卡片标题（如“张三和李四的聊天记录”）。</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>快照消息列表（保留原发送者名）。</summary>
        public List<ChatForwardItemDto> Items { get; set; } = new();
    }

    /// <summary>合并转发快照内的单条消息。</summary>
    public class ChatForwardItemDto
    {
        /// <summary>原发送者显示名。</summary>
        public string SenderName { get; set; } = string.Empty;

        /// <summary>消息类型：0=文本 1=图片（卡片不再嵌套卡片）。</summary>
        public int MsgType { get; set; }

        public string Content { get; set; } = string.Empty;

        public DateTime Time { get; set; }
    }

    /// <summary>图片上传结果：相对路径（消息 Content 直接使用，前端拼 /api/Common/Chat/Image?f= 访问）。</summary>
    public class ChatImageDto
    {
        /// <summary>相对路径：yyyyMM/文件名（不含根目录）。</summary>
        public string Path { get; set; } = string.Empty;
    }

    /// <summary>创建群聊请求：群名必填，成员至少 1 人（创建者自动加入）。</summary>
    public class ChatGroupCreateRequest
    {
        /// <summary>群聊名称（必填，最长 100 字符）。</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>初始成员用户 Id 列表（不含创建者；至少 1 人，最多 1000 人）。</summary>
        public List<Guid> MemberIds { get; set; } = new();
    }

    /// <summary>群成员信息。</summary>
    public class ChatGroupMemberDto
    {
        public Guid UserId { get; set; }

        public string Account { get; set; } = string.Empty;

        public string? DisplayName { get; set; }

        public string? Avatar { get; set; }

        /// <summary>是否在线。</summary>
        public bool Online { get; set; }

        /// <summary>成员角色：0=成员 1=群主。</summary>
        public int Role { get; set; }
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

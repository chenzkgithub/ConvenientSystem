using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// 即时聊天业务服务：企业通讯录模式单聊（无好友关系，屏蔽名单控制可见性）。
    /// 会话由双向归一 UserKey 定位（A->B 与 B->A 同一会话）；未读数基于成员已读水位计算。
    /// 实时推送（SignalR）由 Api 层 ChatController 结合 IHubContext 编排，本层只负责落库与查询。
    /// </summary>
    public interface IChatService
    {
        /// <summary>我的会话列表（未隐藏的，按最后消息时间倒序；含对方信息/最后一条/未读数/屏蔽标记）。</summary>
        List<ChatConversationDto> GetConversations(Guid userId);

        /// <summary>通讯录：全部启用用户（不含自己）+ 双向屏蔽标记（在线状态由 Api 层填充）。</summary>
        List<ChatContactDto> GetContacts(Guid userId);

        /// <summary>打开（或创建）与指定用户的会话：清我方隐藏标记，返回会话 Id 与对方信息。屏蔽双方仍可查看历史。</summary>
        ChatOpenDto OpenConversation(Guid userId, Guid peerId);

        /// <summary>会话历史消息（正序返回）：校验成员身份；beforeId&gt;0 时取该 Id 之前的一页（向上翻页）。</summary>
        List<ChatMessageDto> GetMessages(Guid userId, long conversationId, long beforeId, int limit);

        /// <summary>按 Id 查询单条消息：须属本会话且在我方清空水位之后；否则返回 null（用于引用定位，区分“未加载”与“已删除/不可见”）。</summary>
        ChatMessageDto? GetMessageById(Guid userId, long conversationId, long messageId);

        /// <summary>发送消息：单聊走 peerId；群聊 peerId 为 Empty，由 conversationId 定位会话。双向屏蔽仅对单聊生效；quoteId&gt;0 时引用本会话已有消息（快照固化）。</summary>
        ChatMessageDto SendMessage(Guid userId, Guid peerId, long conversationId, string content, long quoteId = 0, int msgType = 0, List<string>? mentions = null);

        /// <summary>创建群聊：创建者自动成为群主；返回会话列表项。</summary>
        ChatConversationDto CreateGroup(Guid creatorId, string name, List<Guid> memberIds);

        /// <summary>获取群聊成员列表（含在线状态由 Api 层填充；最小版本不返回角色也可，这里保留角色字段）。</summary>
        List<ChatGroupMemberDto> GetGroupMembers(Guid userId, long conversationId);

        /// <summary>推进会话已读水位到指定消息（只进不退；校验消息属于该会话）。返回是否发生推进（供推送已读回执）。</summary>
        bool MarkRead(Guid userId, long conversationId, long messageId);

        /// <summary>取会话对方的用户 Id（已读回执推送定向用；会话不存在或对方无效时返回 Guid.Empty）。</summary>
        Guid GetPeerId(Guid userId, long conversationId);

        /// <summary>总未读数（所有未隐藏会话中，水位之后且非我发送的消息数；顶栏红点轮询）。</summary>
        int GetUnreadTotal(Guid userId);

        /// <summary>屏蔽用户（不能屏蔽自己；幂等）。</summary>
        void BlockUser(Guid userId, Guid peerId);

        /// <summary>取消屏蔽（幂等）。</summary>
        void UnblockUser(Guid userId, Guid peerId);

        /// <summary>我屏蔽的名单（含用户显示信息）。</summary>
        List<ChatBlockDto> GetBlocks(Guid userId);

        /// <summary>隐藏（删除）会话：仅我方视角隐藏，收到新消息自动恢复显示。</summary>
        void HideConversation(Guid userId, long conversationId);

        /// <summary>设置会话免打扰（仍计未读，前端不提醒）。</summary>
        void SetMuted(Guid userId, long conversationId, bool muted);

        /// <summary>单方面清空我方聊天记录：设删除水位到会话最大消息 Id（消息本体保留，对方不受影响；未读一并归零）。</summary>
        void ClearMessagesMySide(Guid userId, long conversationId);

        /// <summary>逐条转发：源消息须属同一会话且我方可见，逐条复制到目标会话（发送者=我）；返回新生成的消息列表（供 Api 层推送双方）。</summary>
        List<ChatMessageDto> ForwardMessages(Guid userId, List<long> messageIds, Guid targetPeerId);

        /// <summary>合并转发：源消息快照固化为记录 + 目标会话插一条“聊天记录”卡片；返回卡片消息（供 Api 层推送双方）。</summary>
        ChatMessageDto ForwardMerged(Guid userId, List<long> messageIds, Guid targetPeerId);

        /// <summary>查看合并转发记录（快照只读）：创建者或收到过该卡片的人可看。</summary>
        ChatForwardRecordDto GetForwardRecord(Guid userId, long recordId);
    }
}

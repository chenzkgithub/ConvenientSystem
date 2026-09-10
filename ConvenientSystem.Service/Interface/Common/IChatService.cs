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

        /// <summary>发送消息：双向屏蔽校验（任一方屏蔽则拒收）-> 落库 -> 更新会话最后消息 -> 双方取消隐藏。</summary>
        ChatMessageDto SendMessage(Guid userId, Guid peerId, string content);

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
    }
}

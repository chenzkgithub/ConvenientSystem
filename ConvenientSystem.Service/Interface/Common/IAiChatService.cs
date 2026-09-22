using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Ai
{
    /// <summary>
    /// AI 对话：会话/消息/配额管理 + 流式生成（Send 同步段校验落库后 Task.Run 后台生成，
    /// chunk 经 IAiChunkNotifier 推送；SignalR 不可用时前端轮询 AiMessage.Status 兜底）。
    /// 能力开关与配额读 SysConfig（ai.enabled / ai.quota.dailyRequests / ai.quota.dailyTokensK）。
    /// </summary>
    public interface IAiChatService
    {
        /// <summary>助手抽屉头部状态（开关/配额/模型就绪），登录即可调用</summary>
        AiMyStatusDto GetMyStatus(Guid userId);

        /// <summary>我的会话列表（UpdateTime 倒序）</summary>
        List<AiConversationDto> GetConversations(Guid userId);

        /// <summary>某会话的消息（校验归属）</summary>
        List<AiMessageDto> GetMessages(Guid userId, long conversationId);

        /// <summary>删除会话（级联删消息，校验归属）</summary>
        void DeleteConversation(Guid userId, long conversationId);

        /// <summary>发送消息：同步段校验配额/落库并返回消息 Id；流式生成在后台执行</summary>
        AiSendResult Send(Guid userId, AiSendRequest request);

        /// <summary>重新生成：重置该 AI 回复（Status=0）并以之前的会话上下文重新生成；配额同 Send</summary>
        AiSendResult Regenerate(Guid userId, long messageId);

        /// <summary>停止生成（校验消息归属）</summary>
        void StopGeneration(Guid userId, long messageId);

        /// <summary>我的 AI 任务列表（create_task 工具创建的待办；待办在前，创建时间倒序）</summary>
        List<AiTaskDto> GetTasks(Guid userId);

        /// <summary>更新任务状态（0 待办 / 1 完成，校验归属）</summary>
        void UpdateTaskStatus(Guid userId, long taskId, int status);

        /// <summary>清理超保留期会话（供 Hangfire 定时任务调用；保留天数读 ai.conversation.retentionDays）</summary>
        int DeleteExpired();
    }
}

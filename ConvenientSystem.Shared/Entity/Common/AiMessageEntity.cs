using ConvenientSystem.Shared.Common;
using FreeSql.DataAnnotations;

namespace ConvenientSystem.Shared.Entity.Common
{
    /// <summary>
    /// AI 对话消息：user/assistant 双方记录；assistant 生成过程经 SignalR 流式推送，
    /// Status 标记生成进度（SignalR 不可用时前端轮询本字段判断完成）。
    /// </summary>
    [Table(Name = "AiMessage")]
    public class AiMessageEntity
    {
        [Column(IsPrimary = true, IsIdentity = true)]
        public long Id { get; set; }

        /// <summary>所属会话 Id</summary>
        public long ConversationId { get; set; }

        /// <summary>归属用户（Guid 字符串，会话归属校验用）</summary>
        public string UserId { get; set; } = string.Empty;

        /// <summary>消息角色：user / assistant（system 提示词不落库）</summary>
        public string Role { get; set; } = string.Empty;

        /// <summary>消息内容（assistant 为生成完成后完整内容）</summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>生成状态：0 生成中 / 1 完成 / 2 失败 / 3 已手动停止（user 消息恒为 1）</summary>
        public int Status { get; set; } = 1;

        /// <summary>输入 token 数（上游 usage，缺省按字符估算）</summary>
        public int TokensIn { get; set; }

        /// <summary>输出 token 数</summary>
        public int TokensOut { get; set; }

        /// <summary>生成耗时（ms）</summary>
        public int ElapsedMs { get; set; }

        /// <summary>失败/停止原因（Status=2/3 时有值）</summary>
        public string? Error { get; set; }

        public DateTime CreateTime { get; set; } = TimeHelper.Now;
    }
}

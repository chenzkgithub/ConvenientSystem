using ConvenientSystem.Shared.Common;
using FreeSql.DataAnnotations;

namespace ConvenientSystem.Shared.Entity.Common
{
    /// <summary>
    /// AI 对话会话：用户在助手抽屉中的多轮对话容器；标题取首条消息前缀，超保留期由定时任务清理。
    /// </summary>
    [Table(Name = "AiConversation")]
    public class AiConversationEntity
    {
        [Column(IsPrimary = true, IsIdentity = true)]
        public long Id { get; set; }

        /// <summary>归属用户（Guid 字符串）</summary>
        public string UserId { get; set; } = string.Empty;

        /// <summary>会话标题（首条消息前 20 字）</summary>
        public string Title { get; set; } = "新对话";

        /// <summary>本次会话使用的模型 Id（冗余记录，供审计）</summary>
        public int ModelId { get; set; }

        public DateTime UpdateTime { get; set; } = TimeHelper.Now;

        public DateTime CreateTime { get; set; } = TimeHelper.Now;
    }
}

using ConvenientSystem.Shared.Common;
using FreeSql.DataAnnotations;

namespace ConvenientSystem.Shared.Entity.Common
{
    /// <summary>
    /// AI 模型配置：管理员在系统配置页维护，支持多模型并存（OpenAI 兼容协议通吃）。
    /// ApiKey 经 DataProtection 加密存储，任何接口不回传明文（只带 HasKey 布尔）。
    /// </summary>
    [Table(Name = "AiModel")]
    public class AiModelEntity
    {
        [Column(IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>显示名（如 DeepSeek-V3 / Kimi / 本地Ollama）</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>OpenAI 兼容接口地址（如 https://api.deepseek.com/v1）</summary>
        public string BaseUrl { get; set; } = string.Empty;

        /// <summary>API Key（DataProtection 加密存储；本地 Ollama 可为空）</summary>
        public string ApiKey { get; set; } = string.Empty;

        /// <summary>模型标识（chat/completions 的 model 参数，如 deepseek-chat）</summary>
        public string ModelId { get; set; } = string.Empty;

        /// <summary>用途标签（逗号分隔；首期固定 chat）</summary>
        public string Scenes { get; set; } = "chat";

        /// <summary>是否全局默认模型（唯一；被删/禁用时自动落到首个启用模型）</summary>
        public bool IsDefault { get; set; }

        /// <summary>是否启用（禁用模型不参与助手下拉与默认切换）</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>上下文截断上限（字符；会话历史超长时保留最近消息截断头部）</summary>
        public int MaxContextChars { get; set; } = 60000;

        /// <summary>排序号</summary>
        public int SortOrder { get; set; }

        public DateTime CreateTime { get; set; } = TimeHelper.Now;
    }
}

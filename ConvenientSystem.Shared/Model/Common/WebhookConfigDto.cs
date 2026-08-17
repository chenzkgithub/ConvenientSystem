namespace ConvenientSystem.Shared.Model.Common
{
    /// <summary>
    /// 群 + 私聊机器人配置 DTO（对前端读写）。Secret 和 AppSecret 明文传输，后端负责加解密。
    /// 一条配置同时支持两种模式：
    ///   1. 群机器人 (EnableGroup=true): 需填 WebhookUrl + Secret
    ///   2. 私聊机器人 (EnablePrivate=true): 需填 AppKey + AppSecret + RecipientIds
    /// </summary>
    public class WebhookConfigDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string ProviderType { get; set; } = "dingtalk";
        
        /// <summary>群机器人 Webhook URL</summary>
        public string WebhookUrl { get; set; } = string.Empty;
        
        /// <summary>群机器人加签密钥</summary>
        public string? Secret { get; set; }

        /// <summary>AppKey（私聊模式用）</summary>
        public string? AppKey { get; set; }

        /// <summary>AppSecret（私聊模式用）</summary>
        public string? AppSecret { get; set; }

        /// <summary>接收者 ID 数组（JSON 或逗号分隔，私聊模式用）</summary>
        public string? RecipientIds { get; set; }

        /// <summary>是否启用群机器人发送</summary>
        public bool EnableGroup { get; set; } = true;

        /// <summary>是否启用私聊机器人发送</summary>
        public bool EnablePrivate { get; set; } = false;

        /// <summary>消息类型：纯文本 / 富文本卡片（仅群机器人生效）</summary>
        public bool UseCard { get; set; } = false;

        /// <summary>是否为默认机器人（自动推送只发给标记为默认的配置）</summary>
        public bool IsDefault { get; set; } = false;
        
        public bool Enabled { get; set; } = true;
        public DateTime CreateTime { get; set; }
        public DateTime UpdateTime { get; set; }
    }

    /// <summary>测试发送请求（针对某条已保存配置）。</summary>
    public class WebhookTestDto
    {
        public int Id { get; set; }
    }

    /// <summary>Webhook 发送结果（对前端）。</summary>
    public class WebhookSendResultDto
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public int CostMs { get; set; }
    }

    /// <summary>机器人发送日志 DTO</summary>
    public class WebhookLogDto
    {
        public int Id { get; set; }
        public int ConfigId { get; set; }
        public string ConfigName { get; set; } = string.Empty;
        public string ProviderType { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public int CostMs { get; set; }
        public DateTime CreateTime { get; set; }
    }
}
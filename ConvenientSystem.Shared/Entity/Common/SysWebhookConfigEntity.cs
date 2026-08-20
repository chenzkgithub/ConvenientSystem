using FreeSql.DataAnnotations;

namespace ConvenientSystem.Shared.Entity.Common
{
    /// <summary>
    /// 群机器人 + 私聊机器人统一配置表（见 db/init.sql dbo.SysWebhookConfig）。
    /// Secret 与 AppSecret 以 AES 密文存储。
    /// 一条配置支持两种模式：
    ///   1. 群机器人 (EnableGroup=true): WebhookUrl + Secret
    ///   2. 私聊机器人 (EnablePrivate=true): AppKey + AppSecret + RecipientIds
    /// 可两者都开启，则发送时群与私聊各发一次。
    /// </summary>
    [Table(Name = "SysWebhookConfig")]
    public class SysWebhookConfigEntity
    {
        [Column(IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>显示名称</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>服务商类型：dingtalk / wecom / feishu</summary>
        public string ProviderType { get; set; } = "dingtalk";

        /// <summary>群机器人 Webhook 地址（EnableGroup=true 时用）</summary>
        public string WebhookUrl { get; set; } = string.Empty;

        /// <summary>群机器人加签密钥（AES 密文存储，EnableGroup=true 时用，可空）</summary>
        public string? Secret { get; set; }

        /// <summary>AppKey（EnablePrivate=true 时用，钉钉应用 AppKey，同时作为私聊 robotCode）</summary>
        public string? AppKey { get; set; }

        /// <summary>AppSecret（AES 密文存储，EnablePrivate=true 时用，钉钉应用 AppSecret）</summary>
        public string? AppSecret { get; set; }

        /// <summary>接收者 ID 数组（JSON 格式 ["uid1","uid2",...] 或逗号分隔，EnablePrivate=true 时用）</summary>
        [Column(StringLength = -1)]
        public string? RecipientIds { get; set; }

        /// <summary>是否启用群机器人发送</summary>
        public bool EnableGroup { get; set; } = true;

        /// <summary>是否启用私聊机器人发送</summary>
        public bool EnablePrivate { get; set; } = false;

        /// <summary>是否使用富文本卡片消息（仅群机器人生效，私聊始终纯文本）</summary>
        public bool UseCard { get; set; } = false;

        /// <summary>是否为默认机器人（自动推送只发给标记为默认的配置；允许多个）</summary>
        public bool IsDefault { get; set; } = false;

        /// <summary>是否启用整个配置</summary>
        public bool Enabled { get; set; } = true;

        [Column(CanInsert = false, CanUpdate = false)]
        public DateTime CreateTime { get; set; }

        public DateTime UpdateTime { get; set; }
    }
}

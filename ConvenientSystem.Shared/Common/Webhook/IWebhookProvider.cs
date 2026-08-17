using ConvenientSystem.Shared.Entity.Common;

namespace ConvenientSystem.Shared.Common.Webhook
{
    /// <summary>
    /// 群机器人发送结果。
    /// </summary>
    public class WebhookSendResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public int CostMs { get; set; }

        public static WebhookSendResult Ok(int costMs) => new() { Success = true, CostMs = costMs };
        public static WebhookSendResult Fail(string error, int costMs) => new() { Success = false, ErrorMessage = error, CostMs = costMs };
    }

    /// <summary>
    /// 群机器人服务商抽象（策略模式）：钉钉 / 企业微信 / 飞书。
    /// 实现为无状态单例；配置（含已解密的 Secret）由调用方传入。
    /// </summary>
    public interface IWebhookProvider
    {
        /// <summary>服务商类型标识：dingtalk / wecom / feishu</summary>
        string ProviderType { get; }

        /// <summary>发送一条文本消息。cfg.Secret 已由调用方解密为明文。</summary>
        Task<WebhookSendResult> SendAsync(SysWebhookConfigEntity cfg, string title, string content);
    }
}

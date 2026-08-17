namespace ConvenientSystem.Shared.Common.Sms
{
    /// <summary>
    /// 短信发送结果
    /// </summary>
    public class SmsSendResult
    {
        /// <summary>是否成功</summary>
        public bool Success { get; set; }

        /// <summary>服务商返回的消息 ID（阿里云为 RequestId）</summary>
        public string? ProviderMsgId { get; set; }

        /// <summary>错误信息</summary>
        public string? ErrorMessage { get; set; }

        /// <summary>耗时毫秒</summary>
        public int CostMs { get; set; }
    }

    /// <summary>
    /// 短信服务商抽象接口（策略模式，便于后续扩展腾讯云/云片等）
    /// </summary>
    public interface ISmsProvider
    {
        /// <summary>服务商标识</summary>
        string Name { get; }

        /// <summary>
        /// 发送单条短信
        /// </summary>
        /// <param name="phone">手机号</param>
        /// <param name="content">短信内容（不含签名）</param>
        /// <param name="signature">签名</param>
        /// <returns>发送结果</returns>
        Task<SmsSendResult> SendAsync(string phone, string content, string signature);
    }
}

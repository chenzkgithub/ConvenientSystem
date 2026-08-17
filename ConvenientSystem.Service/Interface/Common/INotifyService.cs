using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// 群机器人通知业务服务：配置增删改查 + 测试发送 + 向所有启用配置广播。
    /// </summary>
    public interface INotifyService
    {
        /// <summary>获取全部机器人配置（Secret 明文返回，前端遮掩）。</summary>
        List<WebhookConfigDto> GetConfigs();

        /// <summary>已注册的服务商类型（dingtalk/wecom/feishu）。</summary>
        IReadOnlyCollection<string> GetProviderTypes();

        /// <summary>新增或更新配置（Secret 明文传入，服务端 AES 加密存储）。</summary>
        void Save(WebhookConfigDto dto);

        /// <summary>删除配置。</summary>
        void Delete(int id);

        /// <summary>对指定配置测试发送。</summary>
        Task<WebhookSendResultDto> TestAsync(int id);
    }
}

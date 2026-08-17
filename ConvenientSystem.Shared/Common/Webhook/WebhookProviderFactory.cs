namespace ConvenientSystem.Shared.Common.Webhook
{
    /// <summary>
    /// 群机器人 Provider 工厂：按 ProviderType 路由到对应实现（照抄 SmsProviderFactory 思路）。
    /// 启动时注册全部 IWebhookProvider，运行时按配置类型选择。
    /// </summary>
    public class WebhookProviderFactory
    {
        private readonly Dictionary<string, IWebhookProvider> _providers;

        public WebhookProviderFactory(IEnumerable<IWebhookProvider> providers)
        {
            _providers = providers.ToDictionary(p => p.ProviderType, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>按类型获取 Provider；未找到返回 null。</summary>
        public IWebhookProvider? Get(string providerType)
        {
            if (string.IsNullOrWhiteSpace(providerType)) return null;
            return _providers.TryGetValue(providerType, out var p) ? p : null;
        }

        /// <summary>全部已注册的服务商类型（含 -private 私聊实现，内部路由用）。</summary>
        public IReadOnlyCollection<string> GetRegisteredTypes() => _providers.Keys.ToList().AsReadOnly();

        /// <summary>基础服务商类型（不含 -private 后缀）：dingtalk / wecom / feishu，供前端下拉与配置存储。</summary>
        public IReadOnlyCollection<string> GetBaseTypes()
            => _providers.Keys.Where(t => !t.Contains('-')).ToList().AsReadOnly();
    }
}

using ConvenientSystem.Shared.Entity.Sms;
using FreeSql;

namespace ConvenientSystem.Shared.Common.Sms
{
    /// <summary>
    /// 短信 Provider 工厂：根据数据库配置动态选择服务商。
    /// 启动时注册所有 ISmsProvider 实现，运行时按 ProviderType 路由到对应实例。
    /// </summary>
    public class SmsProviderFactory : ISmsProviderFactory
    {
        private readonly IFreeSql _fsql;
        private readonly Dictionary<string, ISmsProvider> _providers;

        public SmsProviderFactory(IFreeSql fsql, IEnumerable<ISmsProvider> providers)
        {
            _fsql = fsql;
            _providers = providers.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>获取默认启用的 Provider；无默认时取第一条启用配置；未配置时返回第一个注册的 Provider</summary>
        public ISmsProvider GetProvider()
        {
            var config = _fsql.Select<SmsProviderConfigEntity>()
                .Where(c => c.Enabled)
                .OrderByDescending(c => c.IsDefault)
                .First();

            var type = config?.ProviderType ?? "aliyun";

            if (_providers.TryGetValue(type, out var provider))
                return provider;

            // 兜底：返回第一个
            return _providers.Values.FirstOrDefault()
                ?? throw new InvalidOperationException("未注册任何短信 Provider");
        }

        /// <summary>获取所有已注册的 Provider 名称</summary>
        public IReadOnlyCollection<string> GetRegisteredNames()
            => _providers.Keys.ToList().AsReadOnly();
    }
}

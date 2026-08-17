namespace ConvenientSystem.Shared.Common.Sms
{
    /// <summary>
    /// 短信 Provider 工厂契约：按数据库配置路由到具体服务商实现
    /// </summary>
    public interface ISmsProviderFactory
    {
        /// <summary>获取当前配置的 Provider；未配置时返回第一个注册的 Provider</summary>
        ISmsProvider GetProvider();

        /// <summary>获取所有已注册的 Provider 名称</summary>
        IReadOnlyCollection<string> GetRegisteredNames();
    }
}

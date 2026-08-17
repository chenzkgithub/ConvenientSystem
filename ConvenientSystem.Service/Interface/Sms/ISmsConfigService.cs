using ConvenientSystem.Shared.Model.Sms;

namespace ConvenientSystem.Service.Sms
{
    /// <summary>
    /// 短信配置业务服务：服务商密钥、配额上限与测试发送。
    /// </summary>
    public interface ISmsConfigService
    {
        /// <summary>获取全部服务商配置列表</summary>
        List<SmsProviderConfigDto> GetConfigs();

        /// <summary>新增或更新服务商配置（Id<=0 新增，否则更新）</summary>
        void Save(SmsProviderConfigDto dto);

        /// <summary>删除服务商配置</summary>
        void Delete(int id);

        /// <summary>获取已注册的服务商名称列表</summary>
        IReadOnlyCollection<string> GetProviderNames();

        /// <summary>获取配额配置与使用情况</summary>
        SmsQuotaDto GetQuota();

        /// <summary>保存每日/每月配额上限</summary>
        void SaveQuota(SmsQuotaDto dto);

        /// <summary>
        /// 测试发送。参数校验或频率超限时抛 BadRequestException；
        /// 发送过程中的异常不抛出，以 Success=false 的结果返回，便于前端展示服务商报错。
        /// </summary>
        Task<SmsTestSendResultDto> TestSendAsync(SmsTestSendRequest req);
    }
}

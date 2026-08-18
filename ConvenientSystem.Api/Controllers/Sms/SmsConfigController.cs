using ConvenientSystem.Api.Auth;
using ConvenientSystem.Shared.Model.Sms;
using ConvenientSystem.Service.Sms;
using Microsoft.AspNetCore.Mvc;

namespace ConvenientSystem.Api.Controllers.Sms
{
    /// <summary>
    /// 短信配置接口（列表 CRUD + 配额 + 测试发送）
    /// </summary>
    [Area("Sms")]
    [PermissionAuthorize("sms-config")]
    public class SmsConfigController : BaseController
    {
        private readonly ISmsConfigService _configService;

        public SmsConfigController(ISmsConfigService configService)
        {
            _configService = configService;
        }

        /// <summary>获取全部短信配置列表</summary>
        [HttpGet]
        public ActionResult<List<SmsProviderConfigDto>> List()
            => Ok(_configService.GetConfigs());

        /// <summary>新增或更新短信配置</summary>
        [HttpPost]
        [PermissionAuthorize("sms-config:create", "sms-config:edit")]
        public IActionResult Save([FromBody] SmsProviderConfigDto dto)
        {
            _configService.Save(dto);
            return Ok();
        }

        /// <summary>删除短信配置</summary>
        [HttpPost]
        [PermissionAuthorize("sms-config:delete")]
        public IActionResult Delete([FromBody] int id)
        {
            _configService.Delete(id);
            return Ok();
        }

        /// <summary>获取已注册的服务商列表</summary>
        [HttpGet]
        public ActionResult<IReadOnlyCollection<string>> GetProviders()
            => Ok(_configService.GetProviderNames());

        /// <summary>获取配额配置</summary>
        [HttpGet]
        public ActionResult<SmsQuotaDto> GetQuota()
            => Ok(_configService.GetQuota());

        /// <summary>保存配额配置</summary>
        [HttpPost]
        public IActionResult SaveQuota([FromBody] SmsQuotaDto dto)
        {
            _configService.SaveQuota(dto);
            return Ok();
        }

        /// <summary>测试发送</summary>
        [HttpPost]
        [PermissionAuthorize("sms-config:test-send")]
        public async Task<ActionResult<SmsTestSendResultDto>> TestSend([FromBody] SmsTestSendRequest req)
            => Ok(await _configService.TestSendAsync(req));
    }
}

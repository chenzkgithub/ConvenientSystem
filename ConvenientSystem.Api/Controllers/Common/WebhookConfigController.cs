using ConvenientSystem.Service.Common;
using ConvenientSystem.Api.Auth;
using ConvenientSystem.Shared.Model.Common;
using Microsoft.AspNetCore.Mvc;

namespace ConvenientSystem.Api.Controllers.Common
{
    /// <summary>
    /// 群机器人 Webhook 配置接口（增删改查 + 测试发送）。
    /// </summary>
    [Area("Common")]
    [PermissionAuthorize("webhook-config")]
    public class WebhookConfigController : BaseController
    {
        private readonly INotifyService _notifyService;

        public WebhookConfigController(INotifyService notifyService)
        {
            _notifyService = notifyService;
        }

        /// <summary>获取全部机器人配置</summary>
        [HttpGet]
        public ActionResult<List<WebhookConfigDto>> List()
            => Ok(_notifyService.GetConfigs());

        /// <summary>获取已注册的服务商类型</summary>
        [HttpGet]
        public ActionResult<IReadOnlyCollection<string>> GetProviderTypes()
            => Ok(_notifyService.GetProviderTypes());

        /// <summary>新增或更新配置</summary>
        [HttpPost]
        [PermissionAuthorize("webhook-config:create", "webhook-config:edit")]
        public IActionResult Save([FromBody] WebhookConfigDto dto)
        {
            _notifyService.Save(dto);
            return Ok();
        }

        /// <summary>删除配置</summary>
        [HttpPost]
        [PermissionAuthorize("webhook-config:delete")]
        public IActionResult Delete([FromBody] int id)
        {
            _notifyService.Delete(id);
            return Ok();
        }

        /// <summary>测试发送</summary>
        [HttpPost]
        [PermissionAuthorize("webhook-config:test-send")]
        public async Task<ActionResult<WebhookSendResultDto>> Test([FromBody] WebhookTestDto req)
            => Ok(await _notifyService.TestAsync(req.Id));
    }
}

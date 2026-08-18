using ConvenientSystem.Api.Auth;
using ConvenientSystem.Shared.Model.Email;
using ConvenientSystem.Service.Email;
using Microsoft.AspNetCore.Mvc;

namespace ConvenientSystem.Api.Controllers.Email
{
    /// <summary>
    /// 邮件配置接口（列表 CRUD + 测试发送）
    /// </summary>
    [Area("Email")]
    [PermissionAuthorize("email-config")]
    public class EmailConfigController : BaseController
    {
        private readonly IEmailConfigService _configService;

        public EmailConfigController(IEmailConfigService configService)
        {
            _configService = configService;
        }

        /// <summary>获取全部邮件配置列表</summary>
        [HttpGet]
        public ActionResult<List<EmailConfigDto>> List()
            => Ok(_configService.GetConfigs());

        /// <summary>新增或更新邮件配置</summary>
        [HttpPost]
        [PermissionAuthorize("email-config:create", "email-config:edit")]
        public IActionResult Save([FromBody] EmailConfigDto dto)
        {
            _configService.Save(dto);
            return Ok();
        }

        /// <summary>删除邮件配置</summary>
        [HttpPost]
        [PermissionAuthorize("email-config:delete")]
        public IActionResult Delete([FromBody] int id)
        {
            _configService.Delete(id);
            return Ok();
        }

        /// <summary>测试发送</summary>
        [HttpPost]
        [PermissionAuthorize("email-config:test-send")]
        public async Task<ActionResult<EmailTestSendResultDto>> TestSend([FromBody] EmailTestSendRequest req)
            => Ok(await _configService.TestSendAsync(req));
    }
}

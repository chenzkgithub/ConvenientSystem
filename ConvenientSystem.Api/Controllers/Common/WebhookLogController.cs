using ConvenientSystem.Api.Auth;
using ConvenientSystem.Shared.Model.Common;
using ConvenientSystem.Service.Common;
using Microsoft.AspNetCore.Mvc;

namespace ConvenientSystem.Api.Controllers.Common
{
    /// <summary>
    /// 机器人发送日志接口（分页查询）。
    /// </summary>
    [Area("Common")]
    [PermissionAuthorize("webhook-log")]
    public class WebhookLogController : BaseController
    {
        private readonly IWebhookLogService _logService;

        public WebhookLogController(IWebhookLogService logService)
        {
            _logService = logService;
        }

        /// <summary>查询日志列表</summary>
        [HttpGet]
        public ActionResult<PagedResult<WebhookLogDto>> List(
            [FromQuery] string? configName,
            [FromQuery] bool? success,
            [FromQuery] int page = 1,
            [FromQuery] int size = 20)
            => Ok(_logService.GetList(configName, success, page, size));
    }
}

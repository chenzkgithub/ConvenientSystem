using ConvenientSystem.Api.Auth;
using ConvenientSystem.Service.Common;
using ConvenientSystem.Shared.Model.Common;
using Microsoft.AspNetCore.Mvc;

namespace ConvenientSystem.Api.Controllers.Common
{
    /// <summary>
    /// 网站/API 监控接口：监控目标管理、探测日志查询与手动立即检测（"网站监控"菜单专用）
    /// </summary>
    [Area("Common")]
    [PermissionAuthorize("web-monitor")]
    public class WebMonitorController : BaseController
    {
        private readonly IWebMonitorService _webMonitorService;

        public WebMonitorController(IWebMonitorService webMonitorService)
        {
            _webMonitorService = webMonitorService;
        }

        /// <summary>查询全部监控目标（含最近探测状态）</summary>
        [HttpGet]
        public ActionResult<List<WebMonitorTargetDto>> List()
            => Ok(_webMonitorService.List());

        /// <summary>新增或编辑监控目标（Id 为空表示新增）</summary>
        [HttpPost]
        [PermissionAuthorize("web-monitor:create", "web-monitor:edit")]
        public ActionResult<int> Save([FromBody] WebMonitorTargetSaveDto dto)
            => Ok(_webMonitorService.Save(dto));

        /// <summary>删除监控目标及其探测日志</summary>
        [HttpDelete]
        [PermissionAuthorize("web-monitor:delete")]
        public ActionResult Delete([FromQuery] int id)
        {
            _webMonitorService.Delete(id);
            return Ok();
        }

        /// <summary>分页查询指定目标的探测日志（时间倒序）</summary>
        [HttpGet]
        public ActionResult<PagedResult<WebMonitorLogDto>> Logs(
            [FromQuery] int targetId,
            [FromQuery] int page = 1,
            [FromQuery] int size = 20)
            => Ok(_webMonitorService.GetLogs(targetId, page, size));

        /// <summary>立即对指定目标执行一次探测，返回本次探测结果</summary>
        [HttpPost]
        [PermissionAuthorize("web-monitor:check")]
        public async Task<ActionResult<WebMonitorLogDto>> Check([FromQuery] int id)
            => Ok(await _webMonitorService.CheckNow(id));

        /// <summary>监控健康度汇总（首页数据看板用）</summary>
        [HttpGet]
        public ActionResult<MonitorHealthDto> Health()
            => Ok(_webMonitorService.GetHealth());
    }
}

using ConvenientSystem.Api.Auth;
using ConvenientSystem.Api.Hubs;
using ConvenientSystem.Service.Common;
using ConvenientSystem.Shared.Model.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace ConvenientSystem.Api.Controllers.Common
{
    /// <summary>
    /// 系统通知管理接口（管理员专用）：通知列表 / 发布与编辑 / 删除。
    /// 挂 notice 菜单权限码，仅拥有“通知管理”菜单的角色可调用。
/// 新建发布后通过 ChatHub 推 NoticeCreated 广播，前端秒级刷新铃铛/弹卡片（编辑/删除不推，轮询兜底）。
    /// </summary>
    [Area("Common")]
    [PermissionAuthorize("notice")]
    public class NoticeManageController : BaseController
    {
        private readonly INoticeService _service;
        private readonly IHubContext<ChatHub> _hubContext;

        public NoticeManageController(INoticeService service, IHubContext<ChatHub> hubContext)
        {
            _service = service;
            _hubContext = hubContext;
        }

        /// <summary>全部通知列表（含发布人信息）。</summary>
        [HttpGet]
        public ActionResult<List<NoticeDto>> List()
            => Ok(_service.GetList());

        /// <summary>
        /// 发布新通知（按勾选开关联动推送）或编辑已有通知。
/// 仅新建发布时向全部在线连接推 NoticeCreated（无参数）；编辑不推（避免重复打扰）。前端各自拉取，
/// 定向通知的可见性过滤由拉取接口完成，不在推送侧复制。
        /// </summary>
        [HttpPost]
        [PermissionAuthorize("notice:publish")]
        public async Task<IActionResult> Save([FromBody] NoticeDto dto)
        {
            _service.Save(dto);
            if (dto.Id == 0)
                await _hubContext.Clients.All.SendAsync("NoticeCreated");
            return Ok();
        }

        /// <summary>删除通知（连同已读记录）。</summary>
        [HttpPost]
        [PermissionAuthorize("notice:delete")]
        public IActionResult Delete([FromQuery] int id)
        {
            _service.Delete(id);
            return Ok();
        }
    }
}

using ConvenientSystem.Api.Auth;
using ConvenientSystem.Service.Common;
using ConvenientSystem.Shared.Model.Common;
using Microsoft.AspNetCore.Mvc;

namespace ConvenientSystem.Api.Controllers.Common
{
    /// <summary>
    /// 系统通知管理接口（管理员专用）：通知列表 / 发布与编辑 / 删除。
    /// 挂 notice 菜单权限码，仅拥有“通知管理”菜单的角色可调用。
    /// </summary>
    [Area("Common")]
    [PermissionAuthorize("notice")]
    public class NoticeManageController : BaseController
    {
        private readonly INoticeService _service;

        public NoticeManageController(INoticeService service)
        {
            _service = service;
        }

        /// <summary>全部通知列表（含发布人信息）。</summary>
        [HttpGet]
        public ActionResult<List<NoticeDto>> List()
            => Ok(_service.GetList());

        /// <summary>发布新通知（按勾选开关联动推送）或编辑已有通知。</summary>
        [HttpPost]
        public IActionResult Save([FromBody] NoticeDto dto)
        {
            _service.Save(dto);
            return Ok();
        }

        /// <summary>删除通知（连同已读记录）。</summary>
        [HttpPost]
        public IActionResult Delete([FromBody] int id)
        {
            _service.Delete(id);
            return Ok();
        }
    }
}

using ConvenientSystem.Api.Auth;
using ConvenientSystem.Service.Common;
using ConvenientSystem.Shared.Model.Common;
using Microsoft.AspNetCore.Mvc;

namespace ConvenientSystem.Api.Controllers.Common
{
    /// <summary>
    /// 视图管理接口：维护视图注册表及其权限点。
    /// </summary>
    [Area("Common")]
    public class ViewController : BaseController
    {
        private readonly IViewService _viewService;

        public ViewController(IViewService viewService)
        {
            _viewService = viewService;
        }

        /// <summary>获取全部视图列表（含权限点），供视图管理页和菜单编辑下拉框使用。</summary>
        [HttpGet]
        public ActionResult<List<ViewDto>> GetViews() => Ok(_viewService.GetViews());

        /// <summary>新增或编辑视图。</summary>
        [HttpPost]
        [PermissionAuthorize("view-manage")]
        public ActionResult<ViewSaveResultDto> SaveView([FromBody] ViewSaveDto dto)
            => Ok(_viewService.SaveView(dto));

        /// <summary>删除视图及其权限点。</summary>
        [HttpDelete]
        [PermissionAuthorize("view-manage")]
        public IActionResult DeleteView([FromQuery] int id)
        {
            try
            {
                _viewService.DeleteView(id);
                return Ok(new { message = "删除成功" });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>新增或编辑权限点。</summary>
        [HttpPost]
        [PermissionAuthorize("view-manage")]
        public ActionResult<ViewSaveResultDto> SavePermission([FromBody] ViewPermissionSaveDto dto)
            => Ok(_viewService.SavePermission(dto));

        /// <summary>删除权限点。</summary>
        [HttpDelete]
        [PermissionAuthorize("view-manage")]
        public IActionResult DeletePermission([FromQuery] int id)
        {
            _viewService.DeletePermission(id);
            return Ok();
        }

        /// <summary>获取带视图权限点的菜单扁平列表（供权限设置页使用）。</summary>
        [HttpGet]
        public ActionResult<List<MenuPermFlatDto>> GetMenusWithViewPerms()
            => Ok(_viewService.GetMenusWithViewPerms());
    }
}

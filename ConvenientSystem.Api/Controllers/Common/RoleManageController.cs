using ConvenientSystem.Api.Auth;
using ConvenientSystem.Shared.Common.Security;
using ConvenientSystem.Shared.Model.Common;
using ConvenientSystem.Service.Common;
using Microsoft.AspNetCore.Mvc;

namespace ConvenientSystem.Api.Controllers.Common
{
    /// <summary>
    /// 角色管理接口：角色增删改查与可见菜单分配。需"角色管理"权限。
    /// 手机端（platform=app）改用白名单码 app-role-manage 校验（AppCode 分流），Web 端仍用菜单码。
    /// </summary>
    [Area("Common")]
    [PermissionAuthorize("role-manage", AppCode = AppPerm.RoleManage)]
    public class RoleManageController : BaseController
    {
        private readonly IRoleService _service;

        public RoleManageController(IRoleService service)
        {
            _service = service;
        }

        /// <summary>角色列表（含分配的菜单 Id）。</summary>
        [HttpGet]
        public ActionResult<List<RoleDto>> List() => Ok(_service.GetRoles());

        /// <summary>新增或更新角色。</summary>
        [HttpPost]
        [PermissionAuthorize("role-manage:add", "role-manage:edit", AppCode = AppPerm.RoleManage)]
        public IActionResult Save([FromBody] RoleSaveDto dto)
        {
            _service.SaveRole(dto);
            return Ok();
        }

        /// <summary>删除角色。</summary>
        [HttpPost]
        [PermissionAuthorize("role-manage:delete", AppCode = AppPerm.RoleManage)]
        public IActionResult Delete([FromQuery] int id)
        {
            _service.Delete(id);
            return Ok();
        }

        /// <summary>启用/停用角色。</summary>
        [HttpPost]
        public IActionResult ToggleEnabled([FromBody] RoleSetEnabledDto dto)
        {
            _service.ToggleEnabled(dto.Id, dto.Enabled);
            return Ok();
        }
    }
}

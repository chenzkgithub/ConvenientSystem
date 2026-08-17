using ConvenientSystem.Api.Auth;
using ConvenientSystem.Service.Common;
using ConvenientSystem.Shared.Model.Common;
using Microsoft.AspNetCore.Mvc;

namespace ConvenientSystem.Api.Controllers.Common
{
    /// <summary>
    /// 权限设置接口：以"permission"权限码独立鉴权，供左右布局的权限设置页调用。
    /// 与角色管理（role-manage）使用不同权限码，可单独授权给只需配权限的管理员。
    /// </summary>
    [Area("Common")]
    [PermissionAuthorize("permission")]
    public class PermissionController : BaseController
    {
        private readonly IRoleService _roleService;
        private readonly IMenuService _menuService;

        public PermissionController(IRoleService roleService, IMenuService menuService)
        {
            _roleService = roleService;
            _menuService = menuService;
        }

        /// <summary>角色列表（含已分配的菜单 Id，供权限设置页左侧列表使用）。</summary>
        [HttpGet]
        public ActionResult<List<RoleDto>> List() => Ok(_roleService.GetRoles());

        /// <summary>全部菜单扁平列表（供权限设置页右侧菜单树使用）。</summary>
        [HttpGet]
        public ActionResult<List<MenuFlatDto>> GetMenusFlat() => Ok(_menuService.GetMenusFlat());

        /// <summary>保存指定角色的菜单权限（仅更新菜单分配，不修改角色基本信息）。</summary>
        [HttpPost]
        public IActionResult Save([FromBody] RolePermissionsDto dto)
        {
            _roleService.SaveRolePermissions(dto.RoleId, dto.MenuIds);
            return Ok();
        }

        /// <summary>角色列表（含各角色下的用户），供权限设置左侧角色→用户树。</summary>
        [HttpGet]
        public ActionResult<List<RoleWithUsersDto>> ListWithUsers() => Ok(_roleService.GetRolesWithUsers());

        /// <summary>用户直接授权的菜单 Id 列表（不含角色继承的）。</summary>
        [HttpGet]
        public ActionResult<List<int>> GetUserPermissions([FromQuery] Guid userId)
            => Ok(_roleService.GetUserMenuIds(userId));

        /// <summary>保存用户级菜单授权（全量替换）。</summary>
        [HttpPost]
        public IActionResult SaveUserPermissions([FromBody] UserPermissionsDto dto)
        {
            _roleService.SaveUserPermissions(dto.UserId, dto.MenuIds);
            return Ok();
        }
    }
}

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
        private readonly IViewService _viewService;

        public PermissionController(IRoleService roleService, IMenuService menuService, IViewService viewService)
        {
            _roleService = roleService;
            _menuService = menuService;
            _viewService = viewService;
        }

        /// <summary>角色列表（含已分配的菜单 Id，供权限设置页左侧列表使用）。</summary>
        [HttpGet]
        public ActionResult<List<RoleDto>> List() => Ok(_roleService.GetRoles());

        /// <summary>全部菜单的扁平列表（含视图权限点），供权限设置页右侧树使用。</summary>
        [HttpGet]
        public ActionResult<List<MenuPermFlatDto>> GetMenusWithViewPerms()
            => Ok(_viewService.GetMenusWithViewPerms());

        /// <summary>保存指定角色的菜单权限与视图权限点。</summary>
        [HttpPost]
        public IActionResult Save([FromBody] RolePermissionsDto dto)
        {
            _roleService.SaveRolePermissions(dto.RoleId, dto.MenuIds);
            _viewService.SaveRoleViewPerms(dto.RoleId, dto.ViewPermIds);
            return Ok();
        }

        /// <summary>角色列表（含各角色下的用户），供权限设置左侧角色→用户树。</summary>
        [HttpGet]
        public ActionResult<List<RoleWithUsersDto>> ListWithUsers()
        {
            var roles = _roleService.GetRolesWithUsers();
            // 补充视图权限点 Id
            foreach (var role in roles)
                role.ViewPermIds = _viewService.GetRoleViewPermIds(role.Id);
            return Ok(roles);
        }

        /// <summary>用户直接授权的菜单 Id 列表 + 视图权限点 Id 列表。</summary>
        [HttpGet]
        public ActionResult<UserPermDetailDto> GetUserPermissions([FromQuery] Guid userId)
        {
            return Ok(new UserPermDetailDto
            {
                MenuIds = _roleService.GetUserMenuIds(userId),
                ViewPermIds = _viewService.GetUserViewPermIds(userId),
            });
        }

        /// <summary>保存用户级菜单授权与视图权限点（全量替换）。</summary>
        [HttpPost]
        public IActionResult SaveUserPermissions([FromBody] UserPermissionsDto dto)
        {
            _roleService.SaveUserPermissions(dto.UserId, dto.MenuIds);
            _viewService.SaveUserViewPerms(dto.UserId, dto.ViewPermIds);
            return Ok();
        }
    }

    /// <summary>用户权限详情（菜单 + 视图权限点）</summary>
    public class UserPermDetailDto
    {
        public List<int> MenuIds { get; set; } = new();
        public List<int> ViewPermIds { get; set; } = new();
    }
}

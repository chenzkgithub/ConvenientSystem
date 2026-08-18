using ConvenientSystem.Api.Auth;
using ConvenientSystem.Shared.Model.Common;
using ConvenientSystem.Service.Common;
using Microsoft.AspNetCore.Mvc;

namespace ConvenientSystem.Api.Controllers.Common
{
    /// <summary>
    /// 菜单控制器：菜单树的读取与保存。业务逻辑见 IMenuService。
    /// 读取按当前登录用户的可见菜单过滤；保存需"菜单管理"权限。
    /// </summary>
    [Area("Common")]
    public class MenuController : BaseController
    {
        private readonly IMenuService _menuService;

        public MenuController(IMenuService menuService)
        {
            _menuService = menuService;
        }

        /// <summary>
        /// 读取 SysMenu 表并按 ParentId/SortOrder 组装成菜单树（按当前用户可见菜单过滤）。
        /// 含 page 的为末级菜单，仅含子节点的为分组菜单。
        /// </summary>
        [HttpGet]
        public ActionResult<List<MenuNode>> GetMenus()
            => Ok(_menuService.GetMenus(CurrentUserId));

        /// <summary>读取全部菜单的扁平列表，供角色管理分配菜单使用。</summary>
        [HttpGet]
        [PermissionAuthorize("role-manage")]
        public ActionResult<List<MenuFlatDto>> GetMenusFlat()
            => Ok(_menuService.GetMenusFlat());

        /// <summary>
        /// 保存菜单树到 SysMenu 表（前端调用）：事务内全删全插，SortOrder 按数组顺序。
        /// </summary>
        [HttpPost]
        [PermissionAuthorize("menu-manage:save")]
        public ActionResult<MenuSaveResultDto> SaveMenus([FromBody] List<MenuNode> menus)
            => Ok(_menuService.SaveMenus(menus));

    }
}

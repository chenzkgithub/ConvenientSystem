using ConvenientSystem.Api.Auth;
using ConvenientSystem.Shared.Common.Security;
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

        /// <summary>
        /// 全量菜单树（菜单管理页专用）：不受当前用户角色授权过滤，
        /// 新增后尚未授权的菜单也返回，解决"保存成功但列表里看不到新菜单"。
        /// 权限与保存菜单一致（menu-manage:save），防止无管理权限的用户窥见全部菜单结构。
        /// </summary>
        [HttpGet]
        [PermissionAuthorize("menu-manage:save")]
        public ActionResult<List<MenuNode>> GetAllMenus()
            => Ok(_menuService.GetAllMenus());

        /// <summary>读取全部菜单的扁平列表，供角色管理分配菜单使用（手机端改用白名单码 app-role-manage 校验）。</summary>
        [HttpGet]
        [PermissionAuthorize("role-manage", AppCode = AppPerm.RoleManage)]
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

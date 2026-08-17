using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// 菜单业务服务：菜单树的读取（按用户权限过滤）与整体保存。
    /// </summary>
    public interface IMenuService
    {
        /// <summary>
        /// 读取菜单树；按用户角色的可见菜单过滤（含祖先分组）。
        /// 所有角色（包括管理员）统一按 SysRoleMenu 配置过滤，不做特殊放行。
        /// userId 为 null 或 0（未登录/底底登录）返回空列表。读取失败时返回空列表（不阻断前端渲染）。
        /// </summary>
        List<MenuNode> GetMenus(Guid? userId);

        /// <summary>整体保存菜单树；失败时通过返回值告知，不抛异常</summary>
        MenuSaveResultDto SaveMenus(List<MenuNode> menus);

        /// <summary>读取全部菜单的扁平列表（Id/ParentId/Title），供角色分配菜单的树选择。</summary>
        List<MenuFlatDto> GetMenusFlat();
    }
}

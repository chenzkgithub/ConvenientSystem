using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// 视图管理业务服务：视图（页面）定义及其权限点的 CRUD。
    /// </summary>
    public interface IViewService
    {
        /// <summary>获取全部视图列表（含各视图的权限点），按 SortOrder 排序。</summary>
        List<ViewDto> GetViews();

        /// <summary>新增或编辑视图（Id=0 时新增，否则编辑）。</summary>
        ViewSaveResultDto SaveView(ViewSaveDto dto);

        /// <summary>删除视图及其所有权限点。</summary>
        void DeleteView(int id);

        /// <summary>为视图新增一个权限点。</summary>
        ViewSaveResultDto SavePermission(ViewPermissionSaveDto dto);

        /// <summary>删除权限点。</summary>
        void DeletePermission(int id);

        /// <summary>获取全部菜单的扁平列表（含视图权限点），供权限设置页使用。</summary>
        List<MenuPermFlatDto> GetMenusWithViewPerms();

        /// <summary>获取指定角色的视图权限点 Id 列表。</summary>
        List<int> GetRoleViewPermIds(int roleId);

        /// <summary>保存角色的视图权限点授权（全量替换）。</summary>
        void SaveRoleViewPerms(int roleId, List<int> viewPermIds);

        /// <summary>获取指定用户的视图权限点 Id 列表（仅用户级直接授权）。</summary>
        List<int> GetUserViewPermIds(Guid userId);

        /// <summary>保存用户的视图权限点授权（全量替换）。</summary>
        void SaveUserViewPerms(Guid userId, List<int> viewPermIds);

        /// <summary>获取用户的所有视图权限码（角色+用户级并集），供登录时写入 JWT。</summary>
        List<string> GetViewPermCodes(Guid userId, List<int> roleIds);
    }
}

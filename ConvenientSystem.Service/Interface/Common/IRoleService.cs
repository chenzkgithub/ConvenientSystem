using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// 角色管理业务服务：角色增删改查与可见菜单分配。admin 角色为超级管理员，受保护不可删除。
    /// </summary>
    public interface IRoleService
    {
        /// <summary>角色列表（含分配的菜单 Id）。</summary>
        List<RoleDto> GetRoles();

        /// <summary>新增或更新角色，同时保存可见菜单分配。</summary>
        void SaveRole(RoleSaveDto dto);

        /// <summary>删除角色（连同菜单/用户关联）。</summary>
        void Delete(int id);

        /// <summary>单独保存角色的可见菜单（权限设置），不修改名称/编码/描述/启用状态。admin 角色不可操作。</summary>
        void SaveRolePermissions(int roleId, List<int> menuIds);

        /// <summary>启用/停用角色。受保护角色（admin、user）不可停用。</summary>
        void ToggleEnabled(int id, bool enabled);

        /// <summary>角色列表（含各角色下的用户），供权限设置左侧树。</summary>
        List<RoleWithUsersDto> GetRolesWithUsers();

        /// <summary>用户直接授权的菜单 Id 列表（不含角色继承的）。</summary>
        List<int> GetUserMenuIds(Guid userId);

        /// <summary>保存用户级菜单授权（全量替换）。</summary>
        void SaveUserPermissions(Guid userId, List<int> menuIds);
    }
}

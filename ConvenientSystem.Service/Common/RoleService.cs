using ConvenientSystem.Shared.Common.Exceptions;
using ConvenientSystem.Shared.Common.Security;
using ConvenientSystem.Shared.Entity.Common;
using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// 角色管理业务实现：角色与角色-菜单关联存储在本地配置库 ConvenientSystem（见 db/init.sql）。
    /// 所有角色（包括管理员）均可编辑，菜单与接口权限统一按 SysRoleMenu 配置。
    /// </summary>
    public class RoleService : IRoleService
    {
        private readonly ILogger<RoleService> _logger;
        private readonly IFreeSql _configDb;

        public RoleService(
            ILogger<RoleService> logger,
            [FromKeyedServices("ConvenientSystemDb")] IFreeSql configDb)
        {
            _logger = logger;
            _configDb = configDb;
        }

        public List<RoleDto> GetRoles()
        {
            var roles = _configDb.Select<SysRoleEntity>().OrderBy(r => r.Id).ToList();
            var roleMenus = _configDb.Select<SysRoleMenuEntity>().ToList();
            return roles.Select(r => new RoleDto
            {
                Id = r.Id,
                Name = r.Name,
                Code = r.Code,
                Description = r.Description,
                Enabled = r.Enabled,
                IsAdmin = r.IsAdmin,
                DataScope = r.DataScope,
                CreateTime = r.CreateTime,
                MenuIds = roleMenus.Where(rm => rm.RoleId == r.Id).Select(rm => rm.MenuId).ToList(),
            }).ToList();
        }

        public void SaveRole(RoleSaveDto dto)
        {
            var name = (dto.Name ?? string.Empty).Trim();
            var code = (dto.Code ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(name)) throw new BadRequestException("角色名称不能为空");
            if (string.IsNullOrEmpty(code)) throw new BadRequestException("角色编码不能为空");
            var dataScope = dto.DataScope is 0 or 1 ? dto.DataScope : 0;

            if (dto.Id == 0)
            {
                if (_configDb.Select<SysRoleEntity>().Any(r => r.Code == code))
                    throw new BadRequestException($"角色编码「{code}」已存在");

                _configDb.Transaction(() =>
                {
                    var newId = (int)_configDb.Insert(new SysRoleEntity
                    {
                        Name = name,
                        Code = code,
                        Description = dto.Description,
                        Enabled = dto.Enabled,
                        IsAdmin = dto.IsAdmin,
                        DataScope = dataScope,
                    }).ExecuteIdentity();
                    SaveRoleMenus(newId, dto.MenuIds);
                });
                _logger.LogInformation("新增角色 {Code}", code);
                return;
            }

            var role = _configDb.Select<SysRoleEntity>().Where(r => r.Id == dto.Id).First()
                ?? throw new NotFoundException("角色不存在");

            if (_configDb.Select<SysRoleEntity>().Any(r => r.Code == code && r.Id != dto.Id))
                throw new BadRequestException($"角色编码「{code}」已存在");

            _configDb.Transaction(() =>
            {
                _configDb.Update<SysRoleEntity>()
                    .Set(r => r.Name, name)
                    .Set(r => r.Code, code)
                    .Set(r => r.Description, dto.Description)
                    .Set(r => r.Enabled, dto.Enabled)
                    .Set(r => r.IsAdmin, dto.IsAdmin)
                    .Set(r => r.DataScope, dataScope)
                    .Where(r => r.Id == dto.Id)
                    .ExecuteAffrows();
                SaveRoleMenus(dto.Id, dto.MenuIds);
            });
            _logger.LogInformation("更新角色 {Code}(Id={Id})", code, dto.Id);
        }

        public void Delete(int id)
        {
            var role = _configDb.Select<SysRoleEntity>().Where(r => r.Id == id).First()
                ?? throw new NotFoundException("角色不存在");
            if (role.IsAdmin || string.Equals(role.Code, JwtHelper.AdminRole, StringComparison.OrdinalIgnoreCase))
                throw new BadRequestException("管理员角色不可删除，但可编辑其权限与状态");
            if (string.Equals(role.Code, JwtHelper.UserRole, StringComparison.OrdinalIgnoreCase))
                throw new BadRequestException("普通用户角色不可删除，但可编辑其权限与状态");

            _configDb.Transaction(() =>
            {
                _configDb.Delete<SysRoleMenuEntity>().Where(rm => rm.RoleId == id).ExecuteAffrows();
                _configDb.Delete<SysUserRoleEntity>().Where(ur => ur.RoleId == id).ExecuteAffrows();
                _configDb.Delete<SysRoleEntity>().Where(r => r.Id == id).ExecuteAffrows();
            });
            _logger.LogInformation("删除角色 Id={Id}", id);
        }

        /// <summary>全量替换角色的菜单分配（须在事务内调用）。</summary>
        private void SaveRoleMenus(int roleId, List<int> menuIds)
        {
            _configDb.Delete<SysRoleMenuEntity>().Where(rm => rm.RoleId == roleId).ExecuteAffrows();
            var distinct = (menuIds ?? new List<int>()).Distinct().ToList();
            if (distinct.Count == 0) return;
            _configDb.Insert(distinct.Select(mid => new SysRoleMenuEntity { RoleId = roleId, MenuId = mid }).ToList())
                .ExecuteAffrows();
        }

        public void SaveRolePermissions(int roleId, List<int> menuIds)
        {
            var role = _configDb.Select<SysRoleEntity>().Where(r => r.Id == roleId).First()
                ?? throw new NotFoundException("角色不存在");
            _configDb.Transaction(() => SaveRoleMenus(roleId, menuIds));
            _logger.LogInformation("更新角色菜单权限 Id={Id}，菜单数={Count}", roleId, menuIds?.Count ?? 0);
        }

        public void ToggleEnabled(int id, bool enabled)
        {
            var role = _configDb.Select<SysRoleEntity>().Where(r => r.Id == id).First()
                ?? throw new NotFoundException("角色不存在");
            _configDb.Update<SysRoleEntity>()
                .Set(r => r.Enabled, enabled)
                .Where(r => r.Id == id)
                .ExecuteAffrows();
            _logger.LogInformation("角色 {Code}(Id={Id}) 启用状态变更为 {Enabled}", role.Code, id, enabled);
        }

        /// <summary>角色列表（含各角色下的用户），供权限设置左侧角色→用户树。</summary>
        public List<RoleWithUsersDto> GetRolesWithUsers()
        {
            var roles = _configDb.Select<SysRoleEntity>().OrderBy(r => r.Id).ToList();
            var roleMenus = _configDb.Select<SysRoleMenuEntity>().ToList();
            var userRoles = _configDb.Select<SysUserRoleEntity>().ToList();
            var users = _configDb.Select<SysUserEntity>().ToList();
            var userById = users.ToDictionary(u => u.Id);

            return roles.Select(r => new RoleWithUsersDto
            {
                Id = r.Id,
                Name = r.Name,
                Code = r.Code,
                Description = r.Description,
                Enabled = r.Enabled,
                IsAdmin = r.IsAdmin,
                MenuIds = roleMenus.Where(rm => rm.RoleId == r.Id).Select(rm => rm.MenuId).ToList(),
                Users = userRoles
                    .Where(ur => ur.RoleId == r.Id && userById.ContainsKey(ur.UserId))
                    .Select(ur =>
                    {
                        var u = userById[ur.UserId];
                        return new UserBriefDto
                        {
                            Id = u.Id,
                            Account = u.Account,
                            DisplayName = u.DisplayName,
                            Avatar = u.Avatar,
                            Enabled = u.Enabled,
                        };
                    })
                    .OrderBy(u => u.DisplayName ?? u.Account)
                    .ToList(),
            }).ToList();
        }

        /// <summary>用户直接授权的菜单 Id 列表。</summary>
        public List<int> GetUserMenuIds(Guid userId)
        {
            return _configDb.Select<SysUserMenuEntity>()
                .Where(um => um.UserId == userId)
                .ToList(um => um.MenuId);
        }

        /// <summary>保存用户级菜单授权（全量替换）。</summary>
        public void SaveUserPermissions(Guid userId, List<int> menuIds)
        {
            _configDb.Transaction(() =>
            {
                _configDb.Delete<SysUserMenuEntity>().Where(um => um.UserId == userId).ExecuteAffrows();
                var distinct = (menuIds ?? new List<int>()).Distinct().ToList();
                if (distinct.Count > 0)
                    _configDb.Insert(distinct.Select(mid => new SysUserMenuEntity { UserId = userId, MenuId = mid }).ToList())
                        .ExecuteAffrows();
            });
            _logger.LogInformation("更新用户菜单权限 UserId={UserId}，菜单数={Count}", userId, menuIds?.Count ?? 0);
        }
    }
}

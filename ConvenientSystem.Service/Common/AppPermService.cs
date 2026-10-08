using ConvenientSystem.Shared.Common.Exceptions;
using ConvenientSystem.Shared.Common.Security;
using ConvenientSystem.Shared.Entity.Common;
using ConvenientSystem.Shared.Model.Common;
using FreeSql;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// 手机端权限业务实现：SysUserAppPerm 白名单（行式，一行=一个用户的一个权限点，无行=无权限）。
    /// 与 PC 端角色/菜单权限完全独立；内置 admin 不写表、恒读全量（防自锁）。
    /// </summary>
    public class AppPermService : IAppPermService
    {
        /// <summary>内置管理员账号（与 UserManageService 的保护口径一致）：恒全权限、禁止配置</summary>
        private const string BuiltInAdminAccount = "admin";

        private readonly ILogger<AppPermService> _logger;
        private readonly IFreeSql _configDb;

        public AppPermService(
            ILogger<AppPermService> logger,
            [FromKeyedServices("ConvenientSystemDb")] IFreeSql configDb)
        {
            _logger = logger;
            _configDb = configDb;
        }

        /// <summary>
        /// 构造某用户的手机端默认预置授权行（基础四项：登录/消息/好友/通知），
        /// 供注册（RegisterController）与管理端新建用户（UserManageService.SaveUser）批量插入。
        /// </summary>
        public static List<SysUserAppPermEntity> BuildDefaultRows(Guid userId, Guid? createdBy)
            => AppPerm.DefaultGrant
                .Select(code => new SysUserAppPermEntity { UserId = userId, PermCode = code, CreatedById = createdBy })
                .ToList();

        public List<AppPermPointDto> GetCatalog()
            => AppPerm.Catalog
                .Select(p => new AppPermPointDto { Code = p.Code, Name = p.Name, Description = p.Description, Group = p.Group })
                .ToList();

        public List<AppPermUserDto> GetUsers()
        {
            var users = _configDb.Select<SysUserEntity>()
                .Where(u => !u.IsDeleted)
                .OrderBy(u => u.Id)
                .ToList();

            // 管理员判定：任一 IsAdmin 角色的用户（含内置 admin 账号）
            var adminRoleIds = _configDb.Select<SysRoleEntity>()
                .Where(r => r.IsAdmin || r.Code == JwtHelper.AdminRole)
                .ToList(r => r.Id);
            var adminUserIds = adminRoleIds.Count == 0
                ? new HashSet<Guid>()
                : _configDb.Select<SysUserRoleEntity>()
                    .Where(ur => adminRoleIds.Contains(ur.RoleId))
                    .ToList(ur => ur.UserId)
                    .ToHashSet();

            var permRows = _configDb.Select<SysUserAppPermEntity>().ToList();

            return users.Select(u =>
            {
                var isAdmin = adminUserIds.Contains(u.Id)
                              || string.Equals(u.Account, BuiltInAdminAccount, StringComparison.OrdinalIgnoreCase);
                var codes = isAdmin
                    ? AppPerm.All.ToList()
                    : permRows.Where(p => p.UserId == u.Id).Select(p => p.PermCode).Distinct().ToList();
                return new AppPermUserDto
                {
                    UserId = u.Id,
                    Account = u.Account,
                    DisplayName = u.DisplayName,
                    Avatar = u.Avatar,
                    Enabled = u.Enabled,
                    IsAdmin = isAdmin,
                    PermCodes = codes,
                };
            }).ToList();
        }

        public void SaveUserPerms(Guid operatorId, Guid userId, List<string>? permCodes)
        {
            var user = _configDb.Select<SysUserEntity>().Where(u => u.Id == userId).First()
                ?? throw new NotFoundException("用户不存在");
            if (user.IsDeleted)
                throw new BadRequestException("该账号已被删除，无法配置权限");
            if (string.Equals(user.Account, BuiltInAdminAccount, StringComparison.OrdinalIgnoreCase))
                throw new BadRequestException("内置管理员默认拥有全部手机端权限，无需配置");

            // 仅接受目录内的权限码（忽略未知码，防脏数据）
            var target = (permCodes ?? new List<string>())
                .Where(c => AppPerm.All.Contains(c, StringComparer.Ordinal))
                .Distinct()
                .ToList();

            // 差量更新：新增插行（记录开通人与时间）、移除删行，已是目标状态的行不动（保留原审计字段）
            _configDb.Transaction(() =>
            {
                var existing = _configDb.Select<SysUserAppPermEntity>()
                    .Where(p => p.UserId == userId)
                    .ToList(p => p.PermCode);

                var toRemove = existing.Except(target, StringComparer.Ordinal).ToList();
                var toAdd = target.Except(existing, StringComparer.Ordinal).ToList();

                if (toRemove.Count > 0)
                    _configDb.Delete<SysUserAppPermEntity>()
                        .Where(p => p.UserId == userId && toRemove.Contains(p.PermCode))
                        .ExecuteAffrows();
                if (toAdd.Count > 0)
                    _configDb.Insert(toAdd
                            .Select(code => new SysUserAppPermEntity { UserId = userId, PermCode = code, CreatedById = operatorId })
                            .ToList())
                        .ExecuteAffrows();
            });

            _logger.LogInformation("手机端权限已更新：目标用户 {UserId}，操作人 {OperatorId}，共开通 {Count} 项",
                userId, operatorId, target.Count);
        }
    }
}

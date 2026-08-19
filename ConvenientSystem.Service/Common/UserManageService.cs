using System.Text.RegularExpressions;
using ConvenientSystem.Shared.Common;
using ConvenientSystem.Shared.Common.Exceptions;
using ConvenientSystem.Shared.Common.Security;
using ConvenientSystem.Shared.Entity.Common;
using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// 用户管理业务实现：用户与角色关联存储在本地配置库 ConvenientSystem（见 db/init.sql）。
    /// 密码统一经 PasswordHasher 哈希后存储；对内置 admin 账号做保护，避免误操作导致锁死。
    /// </summary>
    public class UserManageService : IUserManageService
    {
        private const string BuiltInAdminAccount = "admin";

        /// <summary>账号仅允许字母、数字、中文、_-.@（支持邮箱作为账号）</summary>
        private static readonly Regex AccountPattern = new(@"^[a-zA-Z0-9\u4e00-\u9fa5_.@-]+$", RegexOptions.Compiled);

        private readonly ILogger<UserManageService> _logger;
        private readonly IFreeSql _configDb;

        public UserManageService(
            ILogger<UserManageService> logger,
            [FromKeyedServices("ConvenientSystemDb")] IFreeSql configDb)
        {
            _logger = logger;
            _configDb = configDb;
        }

        public List<UserManageDto> GetUsers()
        {
            var users = _configDb.Select<SysUserEntity>().OrderBy(u => u.Id).ToList();
            var userRoles = _configDb.Select<SysUserRoleEntity>().ToList();
            var roles = _configDb.Select<SysRoleEntity>().ToList();
            var roleById = roles.ToDictionary(r => r.Id, r => r.Name);

            return users.Select(u =>
            {
                var myRoleIds = userRoles.Where(ur => ur.UserId == u.Id).Select(ur => ur.RoleId).ToList();
                return new UserManageDto
                {
                    Id = u.Id,
                    Account = u.Account,
                    DisplayName = u.DisplayName,
                    Avatar = u.Avatar,
                    Phone = u.Phone,
                    Email = u.Email,
                    Remark = u.Remark,
                    Enabled = u.Enabled,
                    IsDeleted = u.IsDeleted,
                    CreateTime = u.CreateTime,
                    RoleIds = myRoleIds,
                    RoleNames = myRoleIds.Where(roleById.ContainsKey).Select(id => roleById[id]).ToList(),
                };
            }).ToList();
        }

        public void SaveUser(UserSaveDto dto)
        {
            var account = (dto.Account ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(account))
                throw new BadRequestException("账号不能为空");
            if (!AccountPattern.IsMatch(account))
                throw new BadRequestException("账号不允许空格和特殊字符，仅支持字母、数字、中文、_-.@");

            // 与个人资料接口共用校验规则；管理员新建用户时允许显示名称为空（顶栏回退展示账号）。
            var displayName = string.IsNullOrWhiteSpace(dto.DisplayName)
                ? null
                : UserProfileValidator.NormalizeDisplayName(dto.DisplayName);
            var avatar = UserProfileValidator.NormalizeAvatar(dto.Avatar);
            var phone = UserProfileValidator.NormalizePhone(dto.Phone);
            var email = UserProfileValidator.NormalizeEmail(dto.Email);
            var remark = UserProfileValidator.NormalizeRemark(dto.Remark);

            if (dto.Id == Guid.Empty)
            {
                if (dto.RoleIds == null || dto.RoleIds.Count == 0)
                    throw new BadRequestException("新增用户必须分配至少一个角色");
                // 账户管理允许设置登录密码为空；为空时存储空字符串的哈希
                dto.Password ??= string.Empty;
                if (_configDb.Select<SysUserEntity>().Any(u => u.Account == account))
                    throw new BadRequestException($"账号「{account}」已存在");

                // 手机号/邮箱唯一性
                if (!string.IsNullOrEmpty(phone)
                    && _configDb.Select<SysUserEntity>().Any(u => u.Phone == phone))
                    throw new BadRequestException($"手机号「{phone}」已被其他账号绑定");
                if (!string.IsNullOrEmpty(email)
                    && _configDb.Select<SysUserEntity>().Any(u => u.Email == email))
                    throw new BadRequestException($"邮箱「{email}」已被其他账号绑定");

                _configDb.Transaction(() =>
                {
                    // 主键为顺序 GUID，新增时由应用层生成
                    var newId = SequentialGuid.NewId();
                    _configDb.Insert(new SysUserEntity
                    {
                        Id = newId,
                        Account = account,
                        DisplayName = displayName,
                        Avatar = avatar,
                        Phone = phone,
                        Email = email,
                        Remark = remark,
                        Password = PasswordHasher.Hash(dto.Password!),
                        Enabled = dto.Enabled,
                    }).ExecuteAffrows();
                    SaveUserRoles(newId, dto.RoleIds);
                });
                _logger.LogInformation("新增用户 {Account}", account);
                return;
            }

            var user = _configDb.Select<SysUserEntity>().Where(u => u.Id == dto.Id).First()
                ?? throw new NotFoundException("用户不存在");
            if (user.IsDeleted)
                throw new BadRequestException("该账号已被删除，无法编辑");

            // 账号变更需保证唯一。
            if (!string.Equals(user.Account, account, StringComparison.Ordinal)
                && _configDb.Select<SysUserEntity>().Any(u => u.Account == account && u.Id != dto.Id))
                throw new BadRequestException($"账号「{account}」已存在");

            // 手机号/邮箱唯一性：不允许绑定已被其他用户占用的手机号或邮箱
            if (!string.IsNullOrEmpty(phone)
                && _configDb.Select<SysUserEntity>().Any(u => u.Id != dto.Id && u.Phone == phone))
                throw new BadRequestException($"手机号「{phone}」已被其他账号绑定");
            if (!string.IsNullOrEmpty(email)
                && _configDb.Select<SysUserEntity>().Any(u => u.Id != dto.Id && u.Email == email))
                throw new BadRequestException($"邮箱「{email}」已被其他账号绑定");

            _configDb.Transaction(() =>
            {
                var update = _configDb.Update<SysUserEntity>()
                    .Set(u => u.Account, account)
                    .Set(u => u.DisplayName, displayName)
                    .Set(u => u.Avatar, avatar)
                    .Set(u => u.Phone, phone)
                    .Set(u => u.Email, email)
                    .Set(u => u.Remark, remark)
                    .Set(u => u.Enabled, dto.Enabled);
                // 账户管理允许设置登录密码为空；编辑时密码字段有值（包括空字符串）即更新
                update = update.Set(u => u.Password, PasswordHasher.Hash(dto.Password ?? string.Empty));
                update.Where(u => u.Id == dto.Id).ExecuteAffrows();

                SaveUserRoles(dto.Id, dto.RoleIds);
            });
            _logger.LogInformation("更新用户 {Account}(Id={Id})", account, dto.Id);
        }

        public void SetEnabled(SetEnabledDto dto)
        {
            var user = _configDb.Select<SysUserEntity>().Where(u => u.Id == dto.Id).First()
                ?? throw new NotFoundException("用户不存在");
            if (user.IsDeleted)
                throw new BadRequestException("该账号已被删除，无法操作");
            if (!dto.Enabled && string.Equals(user.Account, BuiltInAdminAccount, StringComparison.OrdinalIgnoreCase))
                throw new BadRequestException("内置管理员账号不可停用");

            _configDb.Update<SysUserEntity>()
                .Set(u => u.Enabled, dto.Enabled)
                .Where(u => u.Id == dto.Id)
                .ExecuteAffrows();
        }

        public void ResetPassword(ResetPasswordDto dto)
        {
            var user = _configDb.Select<SysUserEntity>().Where(u => u.Id == dto.Id).First()
                ?? throw new NotFoundException("用户不存在");
            if (user.IsDeleted)
                throw new BadRequestException("该账号已被删除，无法重置密码");

            // 账户管理允许重置登录密码为空
            var newPassword = dto.Password ?? string.Empty;
            var affected = _configDb.Update<SysUserEntity>()
                .Set(u => u.Password, PasswordHasher.Hash(newPassword))
                .Where(u => u.Id == dto.Id)
                .ExecuteAffrows();
            if (affected == 0) throw new NotFoundException("用户不存在");
        }

        public void Delete(Guid id)
        {
            var user = _configDb.Select<SysUserEntity>().Where(u => u.Id == id).First()
                ?? throw new NotFoundException("用户不存在");
            if (string.Equals(user.Account, BuiltInAdminAccount, StringComparison.OrdinalIgnoreCase))
                throw new BadRequestException("内置管理员账号不可删除");
            if (user.IsDeleted)
                return; // 已软删除，幂等处理

            _configDb.Update<SysUserEntity>()
                .Set(u => u.IsDeleted, true)
                .Set(u => u.Enabled, false)
                .Where(u => u.Id == id)
                .ExecuteAffrows();
            _logger.LogInformation("软删除用户 Id={Id} Account={Account}", id, user.Account);
        }

        /// <summary>全量替换用户的角色关联（须在事务内调用）。</summary>
        private void SaveUserRoles(Guid userId, List<int> roleIds)
        {
            _configDb.Delete<SysUserRoleEntity>().Where(ur => ur.UserId == userId).ExecuteAffrows();
            var distinct = (roleIds ?? new List<int>()).Distinct().ToList();
            if (distinct.Count == 0) return;
            _configDb.Insert(distinct.Select(rid => new SysUserRoleEntity { UserId = userId, RoleId = rid }).ToList())
                .ExecuteAffrows();
        }
    }
}

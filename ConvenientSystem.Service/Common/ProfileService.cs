using ConvenientSystem.Shared.Common;
using ConvenientSystem.Shared.Common.Exceptions;
using ConvenientSystem.Shared.Common.Security;
using ConvenientSystem.Shared.Entity.Common;
using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// 个人资料业务实现：读写本地配置库 ConvenientSystem 的 SysUser 表（见 db/init.sql）。
    /// 所有操作均以调用方自己的 userId 为条件，不接受外部传入的目标用户，避免越权改他人资料。
    /// 密码统一经 PasswordHasher 哈希存储；原密码校验兼容历史明文。
    /// </summary>
    public class ProfileService : IProfileService
    {
        private readonly ILogger<ProfileService> _logger;
        private readonly IFreeSql _configDb;

        public ProfileService(
            ILogger<ProfileService> logger,
            [FromKeyedServices("ConvenientSystemDb")] IFreeSql configDb)
        {
            _logger = logger;
            _configDb = configDb;
        }

        public async Task<ProfileDto> GetProfileAsync(Guid userId)
        {
            var user = await _configDb.Select<SysUserEntity>()
                .Where(u => u.Id == userId)
                .FirstAsync()
                ?? throw new NotFoundException("用户不存在");

            return new ProfileDto
            {
                Account = user.Account,
                DisplayName = user.DisplayName,
                Avatar = user.Avatar,
                Phone = user.Phone,
                Email = user.Email,
                Remark = user.Remark,
            };
        }

        public async Task SaveProfileAsync(Guid userId, ProfileSaveDto dto)
        {
            var displayName = UserProfileValidator.NormalizeDisplayName(dto?.DisplayName);
            var avatar = UserProfileValidator.NormalizeAvatar(dto?.Avatar);
            var phone = UserProfileValidator.NormalizePhone(dto?.Phone);
            var email = UserProfileValidator.NormalizeEmail(dto?.Email);
            var remark = UserProfileValidator.NormalizeRemark(dto?.Remark);

            // 手机号/邮箱唯一性：不允许绑定已被其他用户占用的手机号或邮箱
            if (!string.IsNullOrEmpty(phone)
                && await _configDb.Select<SysUserEntity>().AnyAsync(u => u.Id != userId && u.Phone == phone))
                throw new BadRequestException($"手机号「{phone}」已被其他账号绑定");

            if (!string.IsNullOrEmpty(email)
                && await _configDb.Select<SysUserEntity>().AnyAsync(u => u.Id != userId && u.Email == email))
                throw new BadRequestException($"邮箱「{email}」已被其他账号绑定");

            var affected = await _configDb.Update<SysUserEntity>()
                .Set(u => u.DisplayName, displayName)
                .Set(u => u.Avatar, avatar)
                .Set(u => u.Phone, phone)
                .Set(u => u.Email, email)
                .Set(u => u.Remark, remark)
                .Where(u => u.Id == userId)
                .ExecuteAffrowsAsync();
            if (affected == 0) throw new NotFoundException("用户不存在");

            _logger.LogInformation("用户 Id={Id} 修改了个人资料，显示名称 {DisplayName}", userId, displayName);
        }

        public async Task ChangePasswordAsync(Guid userId, ChangePasswordDto dto)
        {
            var newPassword = dto?.NewPassword ?? string.Empty;
            if (string.IsNullOrEmpty(newPassword))
                throw new BadRequestException("新密码不能为空");
            if (newPassword.Length < 6)
                throw new BadRequestException("新密码至少 6 位");

            var user = await _configDb.Select<SysUserEntity>()
                .Where(u => u.Id == userId)
                .FirstAsync()
                ?? throw new NotFoundException("用户不存在");

            if (!PasswordHasher.Verify(dto?.OldPassword ?? string.Empty, user.Password))
                throw new BadRequestException("原密码不正确");

            await _configDb.Update<SysUserEntity>()
                .Set(u => u.Password, PasswordHasher.Hash(newPassword))
                .Where(u => u.Id == userId)
                .ExecuteAffrowsAsync();

            _logger.LogInformation("用户 {Account}(Id={Id}) 修改了本人密码", user.Account, userId);
        }
    }
}

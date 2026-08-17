using ConvenientSystem.Shared.Common.Security;
using ConvenientSystem.Shared.Entity.Common;
using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// 锁屏业务实现：锁屏开关/超时从 UserConfig 表读取当前用户的个人配置，
    /// 未设置时使用硬编码默认值（开关=true，超时=120 秒）。
    /// 解锁密码校验当前登录用户的 SysUser 密码（PasswordHasher.Verify）。
    /// </summary>
    public class LockService : ILockService
    {
        private readonly ILogger<LockService> _logger;
        private readonly IFreeSql _configDb;
        private readonly ICurrentUser _currentUser;

        public LockService(
            ILogger<LockService> logger,
            [FromKeyedServices("ConvenientSystemDb")] IFreeSql configDb,
            ICurrentUser currentUser)
        {
            _logger = logger;
            _configDb = configDb;
            _currentUser = currentUser;
        }

        /// <summary>
        /// 读取前端运行所需的客户端配置。
        /// 锁屏开关/超时从 UserConfig 表读取当前用户的个人配置，未设置时用默认值。
        /// </summary>
        public AppConfigDto GetAppConfig()
        {
            var userId = _currentUser.UserId;

            // 默认值
            var enableLock = true;
            var lockTimeout = 120;

            // 从 UserConfig 表读取当前用户的个人配置
            if (userId != null)
            {
                try
                {
                    var userConfigs = _configDb.Select<UserConfigEntity>()
                        .Where(e => e.UserId == userId.Value
                            && (e.ConfigKey == "AppSettings.EnableLock" || e.ConfigKey == "AppSettings.LockTimeout"))
                        .ToList();
                    var enableLockStr = userConfigs.FirstOrDefault(e => e.ConfigKey == "AppSettings.EnableLock")?.ConfigValue;
                    var timeoutStr = userConfigs.FirstOrDefault(e => e.ConfigKey == "AppSettings.LockTimeout")?.ConfigValue;

                    if (!string.IsNullOrEmpty(enableLockStr))
                        enableLock = enableLockStr.Equals("true", StringComparison.OrdinalIgnoreCase);
                    if (int.TryParse(timeoutStr, out var t) && t > 0)
                        lockTimeout = t;
                }
                catch { /* UserConfig 表不存在或查询异常时使用默认值 */ }
            }

            return new AppConfigDto { EnableLock = enableLock, LockTimeout = lockTimeout };
        }

        public async Task<UnlockVerifyDto> VerifyUnlock(UnlockDto request)
        {
            var userId = _currentUser.UserId;
            if (userId == null)
            {
                _logger.LogWarning("锁屏解锁校验：未获取到当前用户 Id");
                return new UnlockVerifyDto { Ok = false };
            }

            try
            {
                var user = await _configDb.Select<SysUserEntity>()
                    .Where(u => u.Id == userId.Value)
                    .FirstAsync();
                if (user == null)
                    return new UnlockVerifyDto { Ok = false };

                // 账号未设密码时直接放行：不应把用户永久卡在锁屏上无法退出。
                // 锁屏密码就是账号密码，没设过密码意味着没有可校验的凭据。
                var ok = string.IsNullOrEmpty(user.Password)
                    || PasswordHasher.Verify(request?.password ?? "", user.Password);
                _logger.LogInformation("锁屏解锁校验 UserId={UserId}，结果：{Result}", userId, ok);
                return new UnlockVerifyDto { Ok = ok };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "锁屏解锁校验查询 SysUser 失败 UserId={UserId}", userId);
                return new UnlockVerifyDto { Ok = false };
            }
        }
    }
}

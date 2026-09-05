using ConvenientSystem.Shared.Common.Security;
using ConvenientSystem.Shared.Entity.Common;
using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// 登录业务实现：账号密码存储在本地配置库 ConvenientSystem 的 SysUser 表中（见 db/init.sql）。
    /// 密码以 PBKDF2 哈希存储，兼容历史明文（首次登录成功后自动升级为哈希）。
    /// 登录成功签发 JWT，载荷含用户角色与可见菜单权限码，供接口鉴权与菜单过滤。
    /// JWT 密钥由 JwtKeyHolder 单例提供（API 启动时一次性确定），签发方与验证方共用同一实例。
    /// </summary>
    public class LoginService : ILoginService
    {
        private readonly ILogger<LoginService> _logger;
        // 本地配置库 FreeSql（SysUser/SysMenu/SysRole 等所在库）
        private readonly IFreeSql _configDb;
        // 系统配置服务（读取 Security.SessionTimeoutMinutes 等 SysConfig 表中的配置）
        private readonly ISysConfigService _sysConfig;
        private readonly IViewService _viewService;
        // JWT 密钥持有者（单例）：API 启动时确定，签发与验证共用，避免密钥不一致
        private readonly JwtKeyHolder _jwtKey;

        public LoginService(
            ILogger<LoginService> logger,
            [FromKeyedServices("ConvenientSystemDb")] IFreeSql configDb,
            ISysConfigService sysConfig,
            IViewService viewService,
            JwtKeyHolder jwtKey)
        {
            _logger = logger;
            _configDb = configDb;
            _sysConfig = sysConfig;
            _viewService = viewService;
            _jwtKey = jwtKey;
        }

        public async Task<LoginDefaultDto> GetLoginDefaultAsync()
        {
            try
            {
                var user = await _configDb.Select<SysUserEntity>()
                    .Where(u => u.Enabled && !u.IsDeleted)
                    .OrderBy(u => u.Id)
                    .FirstAsync();
                if (user != null)
                {
                    // 已升级为哈希的密码不回填（回填哈希会导致登录失败），仅回填历史明文。
                    var password = PasswordHasher.IsHashed(user.Password) ? string.Empty : user.Password;
                    return new LoginDefaultDto { Account = user.Account, Password = password };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "读取 SysUser 默认账号失败");
            }
            // 数据库不可用/无账号时返回空，前端登录页不回填凭据
            return new LoginDefaultDto();
        }

        public async Task<LoginVerifyDto> VerifyLoginAsync(LoginDto request)
        {
            var reqAccount = request?.account ?? "";
            var reqPassword = request?.password ?? "";
            try
            {
                // 先按账号查找（不过滤 Enabled），再分别判断停用/密码错误。
                var user = await _configDb.Select<SysUserEntity>()
                    .Where(u => u.Account == reqAccount)
                    .FirstAsync();
        
                if (user == null)
                {
                    _logger.LogInformation("登录校验，账号：{Account}，结果：账号不存在", reqAccount);
                    return new LoginVerifyDto { Ok = false, Reason = "account_not_found" };
                }
        
                if (!user.Enabled || user.IsDeleted)
                {
                    _logger.LogInformation("登录校验，账号：{Account}，结果：账号已停用/已删除", reqAccount);
                    return new LoginVerifyDto { Ok = false, Reason = "account_disabled" };
                }
        
                if (!PasswordHasher.Verify(reqPassword, user.Password))
                {
                    _logger.LogInformation("登录校验，账号：{Account}，结果：密码错误", reqAccount);
                    return new LoginVerifyDto { Ok = false, Reason = "wrong_password" };
                }
        
                // 历史明文校验通过后自动升级为哈希存储。
                if (!PasswordHasher.IsHashed(user.Password))
                {
                    try
                    {
                        var hashed = PasswordHasher.Hash(reqPassword);
                        await _configDb.Update<SysUserEntity>()
                            .Set(u => u.Password, hashed)
                            .Where(u => u.Id == user.Id)
                            .ExecuteAffrowsAsync();
                        _logger.LogInformation("账号 {Account} 的明文密码已升级为哈希存储", reqAccount);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "升级账号 {Account} 密码哈希失败（不影响本次登录）", reqAccount);
                    }
                }
        
                var (roleCodes, menuCodes, isAdmin, dataScope) = await LoadPermissionsAsync(user.Id);
                var sessionTimeoutMinutes = ReadSessionTimeoutMinutes();
                // 0 表示会话永不过期（兼容历史行为），否则按配置时长签发 JWT
                TimeSpan? tokenLifetime = sessionTimeoutMinutes > 0 ? TimeSpan.FromMinutes(sessionTimeoutMinutes) : null;
                var token = JwtHelper.GenerateToken(_jwtKey.Key, user.Id, user.Account, user.DisplayName, roleCodes, menuCodes, lifetime: tokenLifetime, isAdmin: isAdmin, dataScope: dataScope);
                _logger.LogInformation("登录校验，账号：{Account}，结果：{Result}，会话超时：{Timeout}分钟", reqAccount, true, sessionTimeoutMinutes);
                return new LoginVerifyDto
                {
                    Ok = true,
                    UserId = user.Id,
                    Account = user.Account,
                    DisplayName = user.DisplayName,
                    Avatar = user.Avatar,
                    Token = token,
                    Roles = roleCodes,
                    MenuCodes = menuCodes,
                    SessionTimeoutMinutes = sessionTimeoutMinutes,
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "查询 SysUser 校验登录失败");
                return new LoginVerifyDto { Ok = false, Reason = "wrong_password" };
            }
        }
        
        public async Task<LoginStatusDto> CheckStatusAsync(Guid userId)
        {
            try
            {
                var user = await _configDb.Select<SysUserEntity>()
                    .Where(u => u.Id == userId)
                    .FirstAsync();
                return new LoginStatusDto { Enabled = user != null && user.Enabled && !user.IsDeleted };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "心跳检查用户状态失败 UserId={Id}，按启用处理", userId);
                // DB 连接失败时不强制退出
                return new LoginStatusDto { Enabled = true };
            }
        }

        /// <summary>查询用户的角色编码、可见菜单权限码（菜单 Name）、管理员标记以及数据范围（多角色取最宽松）。</summary>
        private async Task<(List<string> roleCodes, List<string> menuCodes, bool isAdmin, DataScope dataScope)> LoadPermissionsAsync(Guid userId)
        {
            var roleIds = await _configDb.Select<SysUserRoleEntity>()
                .Where(ur => ur.UserId == userId)
                .ToListAsync(ur => ur.RoleId);

            // 无角色不提前返回：用户可能只有用户级直接授权（SysUserMenu/SysUserViewPerm）。
            // roleIds 为空时各角色查询返回空集，不影响用户级授权合并。

            var roles = roleIds.Count == 0
                ? new List<(string Code, bool IsAdmin, int DataScope)>()
                : (await _configDb.Select<SysRoleEntity>()
                    .Where(r => roleIds.Contains(r.Id) && r.Enabled)
                    .ToListAsync(r => new { r.Code, r.IsAdmin, r.DataScope }))
                    .Select(r => (r.Code, r.IsAdmin, r.DataScope))
                    .ToList();

            var roleCodes = roles.Select(r => r.Code).ToList();
            var isAdmin = roles.Any(r => r.IsAdmin || string.Equals(r.Code, JwtHelper.AdminRole, StringComparison.OrdinalIgnoreCase));
            var dataScope = roles.Any() ? (DataScope)roles.Max(r => r.DataScope) : DataScope.Self;

            // 角色菜单（SysRoleMenu）+ 用户级额外授权（SysUserMenu）取并集。
            var roleMenuIds = roleIds.Count == 0
                ? new List<int>()
                : await _configDb.Select<SysRoleMenuEntity>()
                    .Where(rm => roleIds.Contains(rm.RoleId))
                    .ToListAsync(rm => rm.MenuId);

            var userMenuIds = await _configDb.Select<SysUserMenuEntity>()
                .Where(um => um.UserId == userId)
                .ToListAsync(um => um.MenuId);

            var menuIds = roleMenuIds.Union(userMenuIds).ToList();

            var menuCodes = menuIds.Count == 0
                ? new List<string>()
                : await _configDb.Select<SysMenuEntity>()
                    .Where(m => menuIds.Contains(m.Id) && m.Name != null && m.Name != "")
                    .ToListAsync(m => m.Name!);

            // 合并视图权限点码（角色+用户级授权并集）
            var viewPermCodes = _viewService.GetViewPermCodes(userId, roleIds);
            menuCodes.AddRange(viewPermCodes);

            return (roleCodes.Distinct().ToList(), menuCodes.Distinct().ToList(), isAdmin, dataScope);
        }

        /// <summary>读取会话超时时间（分钟）：优先 SysConfig 表 Security.SessionTimeoutMinutes，缺省 30 分钟，0 表示不自动退出。</summary>
        private int ReadSessionTimeoutMinutes()
        {
            var value = _sysConfig.GetValue("Security.SessionTimeoutMinutes");
            if (int.TryParse(value, out var minutes) && minutes >= 0)
                return minutes;
            return 30;
        }

    }
}

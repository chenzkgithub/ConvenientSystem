using ConvenientSystem.Shared.Common;
using ConvenientSystem.Shared.Common.Security;
using ConvenientSystem.Shared.Entity.Common;
using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// 用户个人配置服务：管理当前登录用户的个性化配置。
    /// 可个性化配置项清单硬编码在服务层（元数据 + 默认值），值从 UserConfig 表读取用户设置，
    /// 未设置时使用硬编码默认值。
    /// 存储于本地配置库 ConvenientSystem 的 UserConfig 表。
    /// </summary>
    public class UserConfigService : IUserConfigService
    {
        private const string MaskedValue = "••••••••";

        private readonly IFreeSql _configDb;
        private readonly ICurrentUser _currentUser;

        public UserConfigService(
            [FromKeyedServices("ConvenientSystemDb")] IFreeSql configDb,
            ICurrentUser currentUser)
        {
            _configDb = configDb;
            _currentUser = currentUser;
        }

        /// <summary>
        /// 可个性化配置项清单（硬编码元数据 + 默认值）。
        /// </summary>
        private static readonly (string Key, string DisplayName, string? Description, string InputType, string Category, int SortOrder, string DefaultValue, List<UserConfigOptionDto>? Options)[] ConfigMetadata =
        {
            ("AppSettings.EnableLock", "锁屏功能", "开启后空闲超时自动锁屏", "switch", "锁屏设置", 1, "true", null),
            ("AppSettings.LockTimeout", "锁屏超时(秒)", "无操作多久后自动锁屏", "number", "锁屏设置", 2, "120", null),
            ("UI.SidebarCollapsed", "侧栏折叠", "左侧菜单是否折叠", "switch", "界面偏好", 1, "true", null),
            ("UI.RememberTabs", "标签记忆", "刷新后是否恢复上次打开的标签", "switch", "界面偏好", 2, "true", null),
            ("UI.NavMode", "导航模式", "面包屑 / 多标签", "select", "界面偏好", 3, "breadcrumb", new List<UserConfigOptionDto>
            {
                new() { Value = "breadcrumb", Label = "面包屑" },
                new() { Value = "tabs", Label = "多标签" },
            }),
            ("UI.ThemeMode", "主题模式", "浅色 / 深色 / 跟随系统", "select", "界面偏好", 4, "light", new List<UserConfigOptionDto>
            {
                new() { Value = "light", Label = "浅色" },
                new() { Value = "dark", Label = "深色" },
                new() { Value = "system", Label = "跟随系统" },
            }),
            ("Lottery.RandomPick", "定时生成选号", "开启后每天 13:00 为当天开奖彩种自动生成 10 注随机选号，并发送邮件到您绑定的邮箱", "switch", "彩票设置", 1, "false", null),
        };

        public List<UserConfigGroupDto> GetMyConfig()
        {
            var userId = _currentUser.UserId;
            if (userId == null)
                return new List<UserConfigGroupDto>();

            // 查询当前用户已有的设置值
            Dictionary<string, string> overrideDict = new();
            try
            {
                var userOverrides = _configDb.Select<UserConfigEntity>()
                    .Where(e => e.UserId == userId.Value)
                    .ToList();
                overrideDict = userOverrides
                    .ToDictionary(e => e.ConfigKey, e => e.ConfigValue ?? string.Empty);
            }
            catch { /* UserConfig 表不存在时使用默认值 */ }

            // 合并元数据 + 用户设置值 / 硬编码默认值
            var items = ConfigMetadata.Select(m =>
            {
                var value = overrideDict.TryGetValue(m.Key, out var overrideVal)
                    ? overrideVal
                    : m.DefaultValue;
                return new UserConfigItemDto
                {
                    ConfigKey = m.Key,
                    // password 类型脱敏，防止接口直接泄露密钥；查看明文走 RevealValue（验证登录密码）
                    ConfigValue = m.InputType == "password" ? MaskedValue : value,
                    DisplayName = m.DisplayName,
                    Description = m.Description,
                    InputType = m.InputType,
                    Category = m.Category,
                    SortOrder = m.SortOrder,
                    Options = m.Options,
                };
            }).ToList();

            return items
                .GroupBy(i => i.Category)
                .Select(g => new UserConfigGroupDto
                {
                    Category = g.Key,
                    Items = g.OrderBy(i => i.SortOrder).ToList(),
                })
                .ToList();
        }

        public void UpdateBatch(List<UserConfigSaveDto> items)
        {
            var userId = _currentUser.UserId;
            if (userId == null) return;

            var validKeys = ConfigMetadata.Select(m => m.Key).ToHashSet();

            foreach (var item in items)
            {
                if (string.IsNullOrWhiteSpace(item.ConfigKey)) continue;
                if (!validKeys.Contains(item.ConfigKey)) continue;
                // 脱敏占位符不写入（password 类项未验证/未修改时前端会回传占位符）
                if (item.ConfigValue == MaskedValue) continue;

                var existing = _configDb.Select<UserConfigEntity>()
                    .Where(e => e.UserId == userId.Value && e.ConfigKey == item.ConfigKey)
                    .First();

                if (existing != null)
                {
                    _configDb.Update<UserConfigEntity>()
                        .Set(e => e.ConfigValue, item.ConfigValue ?? string.Empty)
                        .Set(e => e.UpdatedAt, TimeHelper.Now)
                        .Where(e => e.Id == existing.Id)
                        .ExecuteAffrows();
                }
                else
                {
                    _configDb.Insert(new UserConfigEntity
                    {
                        UserId = userId.Value,
                        ConfigKey = item.ConfigKey,
                        ConfigValue = item.ConfigValue ?? string.Empty,
                        CreatedAt = TimeHelper.Now,
                        UpdatedAt = TimeHelper.Now,
                    }).ExecuteAffrows();
                }
            }
        }

        /// <summary>获取当前用户 UI 偏好的扁平键值字典（含默认值），供前端登录时一次拉取。</summary>
        public Dictionary<string, string> GetUIPrefs()
        {
            var userId = _currentUser.UserId;
            // 先填充默认值
            var result = ConfigMetadata
                .Where(m => m.Key.StartsWith("UI."))
                .ToDictionary(m => m.Key, m => m.DefaultValue);

            if (userId == null) return result;

            try
            {
                var overrides = _configDb.Select<UserConfigEntity>()
                    .Where(e => e.UserId == userId.Value && e.ConfigKey.StartsWith("UI."))
                    .ToList();
                foreach (var o in overrides)
                    result[o.ConfigKey] = o.ConfigValue ?? string.Empty;
            }
            catch { /* UserConfig 表不存在时使用默认值 */ }

            return result;
        }

        /// <summary>读取当前用户的原始配置值（绕过元数据校验，供启动器等扩展功能使用）。</summary>
        public string? GetRawValue(string key)
        {
            var userId = _currentUser.UserId;
            if (userId == null) return null;
            try
            {
                return _configDb.Select<UserConfigEntity>()
                    .Where(e => e.UserId == userId.Value && e.ConfigKey == key)
                    .First(e => e.ConfigValue);
            }
            catch { return null; }
        }

        /// <summary>验证当前用户登录密码（SysUser 表 + PasswordHasher），供个人密钥类功能查看明文前校验。</summary>
        public bool VerifyLoginPassword(string password)
        {
            var userId = _currentUser.UserId;
            if (userId == null || string.IsNullOrEmpty(password)) return false;
            try
            {
                var user = _configDb.Select<SysUserEntity>()
                    .Where(u => u.Id == userId.Value)
                    .First();
                return user != null && user.Enabled && PasswordHasher.Verify(password, user.Password);
            }
            catch { return false; }
        }

        /// <summary>查看密码类个人配置明文：验证登录密码后返回原值；
        /// 仅允许元数据声明为 password 类型的 key，防止被用来读取任意原始配置。</summary>
        public string? RevealValue(string key, string password)
        {
            var userId = _currentUser.UserId;
            if (userId == null || string.IsNullOrWhiteSpace(key)) return null;
            if (!VerifyLoginPassword(password)) return null;

            var meta = ConfigMetadata.FirstOrDefault(m => m.Key == key);
            if (meta.Key != key || meta.InputType != "password") return null;

            try
            {
                var raw = _configDb.Select<UserConfigEntity>()
                    .Where(e => e.UserId == userId.Value && e.ConfigKey == key)
                    .First(e => e.ConfigValue);
                return raw ?? meta.DefaultValue;
            }
            catch { return null; }
        }

        /// <summary>保存当前用户的原始配置值（绕过元数据校验，upsert）。</summary>
        public void SetRawValue(string key, string value)
        {
            var userId = _currentUser.UserId;
            if (userId == null) return;

            var existing = _configDb.Select<UserConfigEntity>()
                .Where(e => e.UserId == userId.Value && e.ConfigKey == key)
                .First();

            if (existing != null)
            {
                _configDb.Update<UserConfigEntity>()
                    .Set(e => e.ConfigValue, value)
                    .Set(e => e.UpdatedAt, TimeHelper.Now)
                    .Where(e => e.Id == existing.Id)
                    .ExecuteAffrows();
            }
            else
            {
                _configDb.Insert(new UserConfigEntity
                {
                    UserId = userId.Value,
                    ConfigKey = key,
                    ConfigValue = value,
                    CreatedAt = TimeHelper.Now,
                    UpdatedAt = TimeHelper.Now,
                }).ExecuteAffrows();
            }
        }
    }
}

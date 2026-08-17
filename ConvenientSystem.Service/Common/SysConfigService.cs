using ConvenientSystem.Shared.Common.Security;
using ConvenientSystem.Shared.Entity.Common;
using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// 系统配置服务：管理可界面维护的键值对配置。
    /// 存储于本地配置库 ConvenientSystem 的 SysConfig 表。
    /// GetValue 从 SysConfig 表读取，DB 不可用时返回 null，由调用方自行兜底。
    /// GetAll 返回时对 password 类型脱敏，RevealValue 验证用户密码后返回明文。
    /// </summary>
    public class SysConfigService : ISysConfigService
    {
        private const string MaskedValue = "••••••••";

        private readonly IFreeSql _configDb;

        public SysConfigService(
            [FromKeyedServices("ConvenientSystemDb")] IFreeSql configDb)
        {
            _configDb = configDb;
        }

        public List<SysConfigGroupDto> GetAll()
        {
            var entities = _configDb.Select<SysConfigEntity>()
                .OrderBy(e => e.SortOrder)
                .OrderBy(e => e.Id)
                .ToList();

            return entities
                .GroupBy(e => e.Category)
                .Select(g => new SysConfigGroupDto
                {
                    Category = g.Key,
                    Items = g.Select(e => new SysConfigItemDto
                    {
                        Id = e.Id,
                        ConfigKey = e.ConfigKey,
                        // password 类型脱敏，防止接口直接泄露密钥
                        ConfigValue = e.InputType == "password" ? MaskedValue : e.ConfigValue,
                        Category = e.Category,
                        DisplayName = e.DisplayName,
                        Description = e.Description,
                        InputType = e.InputType,
                        TabGroup = e.TabGroup,
                        SortOrder = e.SortOrder,
                    }).ToList()
                })
                .ToList();
        }

        public void UpdateBatch(List<SysConfigUpdateDto> items)
        {
            foreach (var item in items)
            {
                if (string.IsNullOrWhiteSpace(item.ConfigKey)) continue;
                // 脱敏占位符不写入
                if (item.ConfigValue == MaskedValue) continue;
                _configDb.Update<SysConfigEntity>()
                    .Set(e => e.ConfigValue, item.ConfigValue ?? string.Empty)
                    .Set(e => e.UpdatedAt, DateTime.UtcNow)
                    .Where(e => e.ConfigKey == item.ConfigKey)
                    .ExecuteAffrows();
            }
        }

        /// <summary>获取单个配置值：从 SysConfig 表读取，DB 不可用时返回 null</summary>
        public string? GetValue(string key)
        {
            try
            {
                var entity = _configDb.Select<SysConfigEntity>()
                    .Where(e => e.ConfigKey == key)
                    .First();
                if (entity != null && !string.IsNullOrEmpty(entity.ConfigValue))
                    return entity.ConfigValue;
            }
            catch { /* 表不存在或查询异常时返回 null，由调用方自行兜底 */ }
            return null;
        }

        /// <summary>查看敏感配置明文：验证当前用户登录密码后返回明文值</summary>
        public string? RevealValue(string key, string password, Guid userId)
        {
            if (string.IsNullOrWhiteSpace(key) || userId == Guid.Empty)
                return null;

            // 验证用户登录密码
            var user = _configDb.Select<SysUserEntity>()
                .Where(u => u.Id == userId)
                .First();
            if (user == null || !user.Enabled)
                return null;

            if (!PasswordHasher.Verify(password, user.Password))
                return null;

            // 密码正确，返回配置明文
            var entity = _configDb.Select<SysConfigEntity>()
                .Where(e => e.ConfigKey == key)
                .First();
            return entity?.ConfigValue;
        }
    }
}

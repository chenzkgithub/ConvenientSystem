using System.Text.RegularExpressions;
using ConvenientSystem.Shared.Common;
using ConvenientSystem.Shared.Entity.Common;
using FreeSql;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// 手机端用户设置业务实现：AppUserSetting 账号级 KV（聊天背景图/主题/默认彩种等）。
    /// key 由前端自治命名，本层只做格式与长度校验（防脏数据入库），不做业务白名单——
    /// 新增设置项由前端直接存新键，后端与表结构零改动。
    /// </summary>
    public partial class AppSettingService : IAppSettingService
    {
        private readonly IFreeSql _fsql;

        /// <summary>key 格式：字母数字下划线中划线，1-50 字符（chatbg_123 / theme_mode 等）</summary>
        [GeneratedRegex("^[A-Za-z0-9_-]{1,50}$")]
        private static partial Regex KeyPattern();

        public AppSettingService([FromKeyedServices("ConvenientSystemDb")] IFreeSql fsql)
        {
            _fsql = fsql;
        }

        public Dictionary<string, string> GetAll(Guid userId)
        {
            return _fsql.Select<AppUserSettingEntity>()
                .Where(s => s.UserId == userId)
                .ToList()
                .ToDictionary(s => s.SettingKey, s => s.SettingValue);
        }

        public void Save(Guid userId, string key, string value)
        {
            value ??= string.Empty;
            if (userId == Guid.Empty) throw new ArgumentException("用户身份无效");
            if (string.IsNullOrWhiteSpace(key) || !KeyPattern().IsMatch(key))
                throw new ArgumentException("设置键格式不合法");
            if (value.Length > 1000)
                throw new ArgumentException("设置值过长（≤1000 字符）");

            // 空值=删除该行（恢复默认），不在表中留存空串行
            if (value.Length == 0)
            {
                _fsql.Delete<AppUserSettingEntity>()
                    .Where(s => s.UserId == userId && s.SettingKey == key)
                    .ExecuteAffrows();
                return;
            }

            _fsql.InsertOrUpdate<AppUserSettingEntity>()
                .SetSource(new AppUserSettingEntity
                {
                    UserId = userId,
                    SettingKey = key,
                    SettingValue = value,
                    UpdateTime = TimeHelper.Now,
                })
                .ExecuteAffrows();
        }
    }
}

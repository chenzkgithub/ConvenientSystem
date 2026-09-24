using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// 用户个人配置服务：管理当前登录用户的个性化配置。
    /// 可个性化配置项清单在服务层硬编码（元数据），值从 UserConfig 表读取用户覆盖值，
    /// 未覆盖时回退 SysConfig 全局值。
    /// </summary>
    public interface IUserConfigService
    {
        /// <summary>获取当前用户的配置（合并全局默认值 + 用户覆盖值，按分组返回）</summary>
        List<UserConfigGroupDto> GetMyConfig();

        /// <summary>批量 upsert 当前用户配置（password 类项的脱敏占位符会被跳过）</summary>
        void UpdateBatch(List<UserConfigSaveDto> items);

        /// <summary>查看密码类个人配置明文：验证当前用户登录密码后返回原值；
        /// 仅允许元数据声明为 password 类型的 key，密码错误或 key 不合法返回 null。</summary>
        string? RevealValue(string key, string password);

        /// <summary>验证当前用户登录密码（供个人密钥类功能查看明文前校验）</summary>
        bool VerifyLoginPassword(string password);

        /// <summary>获取当前用户 UI 偏好的扁平键值字典（含默认值）</summary>
        Dictionary<string, string> GetUIPrefs();

        /// <summary>读取当前用户的原始配置值（绕过元数据校验，供启动器等扩展功能使用）</summary>
        string? GetRawValue(string key);

        /// <summary>保存当前用户的原始配置值（绕过元数据校验）</summary>
        void SetRawValue(string key, string value);
    }
}

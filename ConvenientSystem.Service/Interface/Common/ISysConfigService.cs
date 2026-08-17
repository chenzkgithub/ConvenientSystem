using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// 系统配置服务：管理可界面维护的键值对配置。
    /// 其他服务通过 GetValue 从 SysConfig 表读取配置（DB 不可用时返回 null）。
    /// </summary>
    public interface ISysConfigService
    {
        /// <summary>获取所有配置（按分组返回）</summary>
        List<SysConfigGroupDto> GetAll();

        /// <summary>批量更新配置值</summary>
        void UpdateBatch(List<SysConfigUpdateDto> items);

        /// <summary>获取单个配置值（从 SysConfig 表读取，DB 不可用时返回 null）</summary>
        string? GetValue(string key);

        /// <summary>查看敏感配置明文：验证当前用户登录密码后返回明文值</summary>
        string? RevealValue(string key, string password, Guid userId);
    }
}

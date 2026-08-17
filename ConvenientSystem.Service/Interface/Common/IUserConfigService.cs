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

        /// <summary>批量 upsert 当前用户配置</summary>
        void UpdateBatch(List<UserConfigSaveDto> items);
    }
}

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// 手机端用户设置业务：AppUserSetting 账号级 KV 的全量读取与单项幂等保存。
    /// 手机端登录后调 GetAll 拉回本地实现设置跨设备同步（聊天背景图/主题/默认彩种等）。
    /// </summary>
    public interface IAppSettingService
    {
        /// <summary>该用户的全部设置（键值对；无任何设置返回空字典）。</summary>
        Dictionary<string, string> GetAll(Guid userId);

        /// <summary>
        /// 单项幂等保存：已存在则覆盖值，不存在则插入；value 为空串时删除该行（恢复默认）。
        /// key 不合法或 value 超长抛 ArgumentException（Controller 转 400）。
        /// </summary>
        void Save(Guid userId, string key, string value);
    }
}

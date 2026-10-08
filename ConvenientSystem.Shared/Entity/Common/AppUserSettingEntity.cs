using ConvenientSystem.Shared.Common;
using FreeSql.DataAnnotations;

namespace ConvenientSystem.Shared.Entity.Common
{
    /// <summary>
    /// 手机端用户设置表（账号级 KV：一行=一个用户的一个设置项）。
    /// 换设备登录后由手机端拉全量回本地，实现设置跨设备同步；键由前端自治命名。
    /// 当前键：chatbg_default / chatbg_{conversationId}=聊天背景图（图片相对路径）、
    /// theme_mode=主题、lottery_active_type=彩票页默认彩种。
    /// </summary>
    [Table(Name = "AppUserSetting")]
    public class AppUserSettingEntity
    {
        /// <summary>用户 Id（SysUser.Id，GUID）</summary>
        [Column(IsPrimary = true)]
        public Guid UserId { get; set; }

        /// <summary>设置键（限字母数字下划线中划线，≤50 字符）</summary>
        [Column(IsPrimary = true)]
        public string SettingKey { get; set; } = string.Empty;

        /// <summary>设置值（≤1000 字符；保存空串=删除该行即恢复默认）</summary>
        public string SettingValue { get; set; } = string.Empty;

        public DateTime UpdateTime { get; set; } = TimeHelper.Now;
    }
}

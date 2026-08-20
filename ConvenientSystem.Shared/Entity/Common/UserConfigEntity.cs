using FreeSql.DataAnnotations;

namespace ConvenientSystem.Shared.Entity.Common
{
    /// <summary>
    /// 用户个人配置表（本地配置库 ConvenientSystem，见 db/init.sql）。
    /// 存储用户级键值对配置，覆盖全局 SysConfig 中的同名键。
    /// 每用户每键唯一（UQ_UserConfig_UserId_Key）。
    /// </summary>
    [Table(Name = "UserConfig")]
    public class UserConfigEntity
    {
        [Column(IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>关联 SysUser.Id</summary>
        public Guid UserId { get; set; }

        /// <summary>配置键，如 AppSettings.EnableLock</summary>
        public string ConfigKey { get; set; } = string.Empty;

        /// <summary>配置值</summary>
        public string? ConfigValue { get; set; }

        /// <summary>创建时间</summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>更新时间</summary>
        public DateTime? UpdatedAt { get; set; }
    }
}

using FreeSql.DataAnnotations;

namespace ConvenientSystem.Shared.Entity.Common
{
    /// <summary>
    /// 系统配置表（本地配置库 ConvenientSystem，见 db/init.sql）。
    /// 存储可界面维护的键值对配置：翻译 API 密钥、日志保留天数等。
    /// </summary>
    [Table(Name = "SysConfig")]
    public class SysConfigEntity
    {
        [Column(IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>配置键，如 BaiduTranslate.AppId</summary>
        public string ConfigKey { get; set; } = string.Empty;

        /// <summary>配置值</summary>
        public string ConfigValue { get; set; } = string.Empty;

        /// <summary>分组：翻译服务、系统安全、日志管理、系统配置</summary>
        public string Category { get; set; } = string.Empty;

        /// <summary>显示名称</summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>描述说明</summary>
        public string? Description { get; set; }

        /// <summary>输入类型：text / password / number / switch</summary>
        public string InputType { get; set; } = "text";

        /// <summary>页签分组：system（系统配置）/ thirdparty（第三方配置）</summary>
        public string TabGroup { get; set; } = "system";

        /// <summary>排序号</summary>
        public int SortOrder { get; set; }

        /// <summary>系统内置（不可删除，仅可改值）</summary>
        public bool IsSystem { get; set; } = true;

        /// <summary>创建时间</summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>更新时间</summary>
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}

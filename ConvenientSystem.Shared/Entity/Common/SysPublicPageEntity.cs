using FreeSql.DataAnnotations;

namespace ConvenientSystem.Shared.Entity.Common
{
    /// <summary>
    /// 外部公开页面表（本地配置库 ConvenientSystem，见 db/init.sql）。
    /// 存储免登录（standalone=1）可直接访问的公开页面配置。
    /// </summary>
    [Table(Name = "SysPublicPage")]
    public class SysPublicPageEntity
    {
        [Column(IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>路由路径，如 /lottery-trend</summary>
        public string PageKey { get; set; } = string.Empty;

        /// <summary>显示名称</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>Vue 组件路径，如 /src/common/views/PublicLotteryTrendView.vue</summary>
        public string Component { get; set; } = string.Empty;

        /// <summary>描述说明</summary>
        public string? Description { get; set; }

        /// <summary>是否启用</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>排序号</summary>
        public int SortOrder { get; set; }

        /// <summary>创建时间</summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>更新时间</summary>
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}

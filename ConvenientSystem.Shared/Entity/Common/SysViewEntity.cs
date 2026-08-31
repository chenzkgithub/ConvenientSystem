using FreeSql.DataAnnotations;

namespace ConvenientSystem.Shared.Entity.Common
{
    /// <summary>
    /// 视图注册表：定义系统中所有可维护权限点的页面视图。
    /// 与 SysMenu 解耦——菜单删除不影响视图定义与权限点授权。
    /// </summary>
    [Table(Name = "SysView")]
    public class SysViewEntity
    {
        [Column(IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>权限码（唯一，如 user-manage）</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>显示名称（如 用户管理）</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>Vue 组件路径（如 /src/common/views/UserManageView.vue）</summary>
        public string? Component { get; set; }

        /// <summary>路由地址（如 /user-manage）</summary>
        public string? RoutePath { get; set; }

        /// <summary>说明</summary>
        public string? Description { get; set; }

        /// <summary>是否启用</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>排序号</summary>
        public int SortOrder { get; set; }
    }
}

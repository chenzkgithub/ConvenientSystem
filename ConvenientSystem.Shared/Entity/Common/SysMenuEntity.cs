using FreeSql.DataAnnotations;

namespace ConvenientSystem.Shared.Entity.Common
{
    /// <summary>
    /// 菜单表（本地配置库 ConvenientSystem，见 db/init.sql）
    /// </summary>
    [Table(Name = "dbo.SysMenu")]
    public class SysMenuEntity
    {
        [Column(IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>父菜单 Id，NULL 为顶层</summary>
        public int? ParentId { get; set; }

        /// <summary>菜单标题</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>末级菜单链接/内部路由；分组菜单为 NULL</summary>
        public string? Page { get; set; }

        /// <summary>是否在悬浮按钮菜单显示</summary>
        public bool IsFloat { get; set; }

        /// <summary>是否在侧栏/首页显示</summary>
        public bool Visible { get; set; } = true;

        /// <summary>是否外部链接</summary>
        public bool IsExternal { get; set; }

        /// <summary>是否允许在菜单管理中编辑</summary>
        public bool Editable { get; set; } = true;

        /// <summary>是否启用（停用后不在侧栏/首页显示，也不可在权限管理中分配）</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>内部路由名称</summary>
        public string? Name { get; set; }

        /// <summary>内部路由 Vue 组件路径</summary>
        public string? Component { get; set; }

        /// <summary>同级排序号</summary>
        public int SortOrder { get; set; }
    }
}

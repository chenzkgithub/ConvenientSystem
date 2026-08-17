namespace ConvenientSystem.Shared.Model.Common
{
    /// <summary>
    /// 菜单节点（存储于本地数据库 SysMenu 表，可多级嵌套）。
    /// </summary>
    public class MenuNode
    {
        /// <summary>菜单 Id（前端回传，用于保存时维护旧 Id → 新 Id 映射；新增时为 null）</summary>
        public int? id { get; set; }

        /// <summary>菜单标题</summary>
        public string title { get; set; } = string.Empty;

        /// <summary>末级菜单对应的页面标识；分组菜单为空。</summary>
        public string? page { get; set; }

        /// <summary>子菜单</summary>
        public List<MenuNode> children { get; set; } = new();

        /// <summary>是否在悬浮按钮菜单中显示（true=显示，false=不显示）</summary>
        public bool @float { get; set; } = false;

        /// <summary>是否在主界面侧栏和首页中显示（true=显示，false=隐藏），默认 true</summary>
        public bool visible { get; set; } = true;

        /// <summary>是否为外部链接（true=外链，false=内部路由），默认 false</summary>
        public bool external { get; set; } = false;

        /// <summary>内部路由名称（仅内部链接有意义）</summary>
        public string? name { get; set; }

        /// <summary>内部路由对应的 Vue 组件路径（如 /src/yunhan/views/AttendanceView.vue）</summary>
        public string? component { get; set; }

        /// <summary>是否允许在菜单管理中编辑（true=允许，false=不允许），默认 true</summary>
        public bool editable { get; set; } = true;

        /// <summary>是否启用（停用后不在侧栏/首页显示，也不可在权限管理中分配），默认 true</summary>
        public bool enabled { get; set; } = true;
    }

    /// <summary>
    /// 菜单保存结果：保存失败也返回 200，由前端读取 ok/msg 提示，因此不走全局异常过滤器。
    /// </summary>
    public class MenuSaveResultDto
    {
        public bool ok { get; set; }
        public string? msg { get; set; }
    }
}

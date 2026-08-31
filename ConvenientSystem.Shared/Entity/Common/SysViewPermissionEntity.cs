using FreeSql.DataAnnotations;

namespace ConvenientSystem.Shared.Entity.Common
{
    /// <summary>
    /// 视图权限点表：定义视图下可精细授权的按钮/动作。
    /// </summary>
    [Table(Name = "SysViewPermission")]
    public class SysViewPermissionEntity
    {
        [Column(IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>所属视图 Id</summary>
        public int ViewId { get; set; }

        /// <summary>权限码（如 user-manage:add）</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>显示名称（如 新增用户）</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>排序号</summary>
        public int SortOrder { get; set; }

        /// <summary>是否启用</summary>
        public bool Enabled { get; set; } = true;
    }
}

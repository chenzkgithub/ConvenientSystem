namespace ConvenientSystem.Shared.Model.Common
{
    /// <summary>视图列表项（供视图管理页面使用）</summary>
    public class ViewDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string? Component { get; set; }
        public string? RoutePath { get; set; }
        public string? Description { get; set; }
        public bool Enabled { get; set; }
        public int SortOrder { get; set; }

        /// <summary>该视图下的权限点列表</summary>
        public List<ViewPermissionDto> Permissions { get; set; } = new();
    }

    /// <summary>视图权限点</summary>
    public class ViewPermissionDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public int SortOrder { get; set; }
        public bool Enabled { get; set; }
    }

    /// <summary>新增/编辑视图请求</summary>
    public class ViewSaveDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string? Component { get; set; }
        public string? RoutePath { get; set; }
        public string? Description { get; set; }
        public bool Enabled { get; set; } = true;
    }

    /// <summary>新增/编辑视图权限点请求</summary>
    public class ViewPermissionSaveDto
    {
        public int Id { get; set; }
        public int ViewId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
    }

    /// <summary>视图保存结果</summary>
    public class ViewSaveResultDto
    {
        public bool Ok { get; set; }
        public string? Msg { get; set; }
    }

    /// <summary>带视图权限点的菜单树节点（供权限设置页使用）</summary>
    public class MenuPermFlatDto
    {
        public int Id { get; set; }
        public int? ParentId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Name { get; set; }
        public byte Type { get; set; }

        /// <summary>关联视图的权限点（仅当该菜单的 Name 匹配 SysView.Name 时有值）</summary>
        public List<ViewPermNodeDto>? ViewPerms { get; set; }
    }

    /// <summary>视图权限点节点（权限树中的叶子）</summary>
    public class ViewPermNodeDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
    }
}

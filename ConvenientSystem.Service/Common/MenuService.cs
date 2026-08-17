using ConvenientSystem.Shared.Entity.Common;
using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// 菜单业务实现：菜单树存储在本地配置库 ConvenientSystem 的 SysMenu 表中（见 db/init.sql）。
    /// 读取按用户可见菜单过滤（SysUserRole → SysRoleMenu）；读取异常直接抛出由全局异常过滤器处理，
    /// 不再吞掉异常返回空列表。
    /// </summary>
    public class MenuService : IMenuService
    {
        private readonly ILogger<MenuService> _logger;
        // 本地配置库 FreeSql（SysUser/SysMenu/SysDataSource 所在库）
        private readonly IFreeSql _configDb;

        public MenuService(
            ILogger<MenuService> logger,
            [FromKeyedServices("ConvenientSystemDb")] IFreeSql configDb)
        {
            _logger = logger;
            _configDb = configDb;
        }

        public List<MenuNode> GetMenus(Guid? userId)
        {
            var entities = _configDb.Select<SysMenuEntity>()
                .OrderBy(m => m.SortOrder)
                .OrderBy(m => m.Id)
                .ToList();

            // 所有角色（包括管理员）统一按 SysRoleMenu 配置的菜单过滤（含祖先分组，保证父级不缺失）。
            if (!userId.HasValue || userId.Value == Guid.Empty)
                return new List<MenuNode>();
            var allowed = ResolveVisibleMenuIds(entities, userId.Value);
            entities = entities.Where(m => allowed.Contains(m.Id)).ToList();

            // 先按行转成 (Id, ParentId, 节点)，再按 ParentId 挂到父节点下组装成树
            var rows = entities.Select(m => (m.Id, m.ParentId, Node: new MenuNode
            {
                id = m.Id,
                title = m.Title,
                page = m.Page,
                @float = m.IsFloat,
                visible = m.Visible,
                external = m.IsExternal,
                editable = m.Editable,
                enabled = m.Enabled,
                name = m.Name,
                component = m.Component,
            })).ToList();

            var byId = rows.ToDictionary(r => r.Id, r => r.Node);
            var roots = new List<MenuNode>();
            foreach (var (_, parentId, node) in rows)
            {
                if (parentId is not null && byId.TryGetValue(parentId.Value, out var parent))
                    parent.children.Add(node);
                else
                    roots.Add(node);
            }
            return roots;
        }

        public List<MenuFlatDto> GetMenusFlat()
        {
            return _configDb.Select<SysMenuEntity>()
                .Where(m => m.Enabled)
                .OrderBy(m => m.SortOrder)
                .OrderBy(m => m.Id)
                .ToList()
                .Select(m => new MenuFlatDto { Id = m.Id, ParentId = m.ParentId, Title = m.Title })
                .ToList();
        }

        /// <summary>
        /// 计算用户可见的菜单 Id 集合：先取角色授权的菜单，再向上补齐所有祖先分组，
        /// 使被授权的末级菜单在树中仍能显示其父级分组。
        /// </summary>
        private HashSet<int> ResolveVisibleMenuIds(List<SysMenuEntity> allMenus, Guid userId)
        {
            var roleIds = _configDb.Select<SysUserRoleEntity>()
                .Where(ur => ur.UserId == userId)
                .ToList(ur => ur.RoleId);
            if (roleIds.Count == 0) return new HashSet<int>();

            var granted = _configDb.Select<SysRoleMenuEntity>()
                .Where(rm => roleIds.Contains(rm.RoleId))
                .ToList(rm => rm.MenuId);

            var parentOf = allMenus.ToDictionary(m => m.Id, m => m.ParentId);
            var visible = new HashSet<int>();
            foreach (var id in granted)
            {
                var cur = (int?)id;
                while (cur is not null && visible.Add(cur.Value))
                    cur = parentOf.TryGetValue(cur.Value, out var p) ? p : null;
            }
            return visible;
        }

        public MenuSaveResultDto SaveMenus(List<MenuNode> menus)
        {
            try
            {
                // oldId → newId 映射，用于保存后更新 SysRoleMenu
                var idMap = new Dictionary<int, int>();

                _configDb.Transaction(() =>
                {
                    // 先收集所有旧 Id，以便在删除前备份 SysRoleMenu
                    var oldRoleMenus = _configDb.Select<SysRoleMenuEntity>().ToList();

                    _configDb.Delete<SysMenuEntity>().Where("1=1").ExecuteAffrows();

                    for (var i = 0; i < menus.Count; i++)
                        InsertMenu(menus[i], null, i + 1, idMap);

                    // 根据旧 Id → 新 Id 映射重建 SysRoleMenu
                    var newRoleMenus = oldRoleMenus
                        .Where(rm => idMap.ContainsKey(rm.MenuId))
                        .Select(rm => new SysRoleMenuEntity { RoleId = rm.RoleId, MenuId = idMap[rm.MenuId] })
                        .ToList();
                    // 安全护栏：原本存在角色-菜单关联但映射全部失败，说明提交的菜单 Id 已过期，
                    // 继续执行会清空全部角色权限，此处中止并回滚整个事务
                    if (oldRoleMenus.Count > 0 && newRoleMenus.Count == 0)
                        throw new InvalidOperationException("提交的菜单数据已过期（Id 映射失败），为防止角色菜单权限丢失已中止保存，请刷新页面后重试");

                    _configDb.Delete<SysRoleMenuEntity>().Where("1=1").ExecuteAffrows();
                    if (newRoleMenus.Count > 0)
                        _configDb.Insert(newRoleMenus).ExecuteAffrows();
                });
                _logger.LogInformation("菜单已保存到 SysMenu，共 {Count} 个顶层菜单", menus.Count);
                return new MenuSaveResultDto { ok = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "保存菜单到 SysMenu 失败");
                return new MenuSaveResultDto { ok = false, msg = ex.Message };
            }
        }

        /// <summary>
        /// 递归插入菜单节点及其子节点，自增 Id 作为子节点的 ParentId（须在 Transaction 内调用）。
        /// 同时将旧 Id → 新 Id 记录到 idMap，供更新 SysRoleMenu 使用。
        /// </summary>
        private void InsertMenu(MenuNode node, int? parentId, int sortOrder, Dictionary<int, int> idMap)
        {
            var newId = (int)_configDb.Insert(new SysMenuEntity
            {
                ParentId = parentId,
                Title = node.title ?? string.Empty,
                Page = string.IsNullOrWhiteSpace(node.page) ? null : node.page,
                IsFloat = node.@float,
                Visible = node.visible,
                IsExternal = node.external,
                Editable = node.editable,
                Enabled = node.enabled,
                Name = string.IsNullOrWhiteSpace(node.name) ? null : node.name,
                Component = string.IsNullOrWhiteSpace(node.component) ? null : node.component,
                SortOrder = sortOrder,
            }).ExecuteIdentity();

            // 记录旧 Id → 新 Id 映射
            if (node.id is > 0)
                idMap[node.id.Value] = newId;

            if (node.children is { Count: > 0 })
            {
                for (var i = 0; i < node.children.Count; i++)
                    InsertMenu(node.children[i], newId, i + 1, idMap);
            }
        }
    }
}

-- 检查 admin 角色的菜单权限
SELECT m.Id, m.ParentId, m.Title, m.Name, m.Component, m.RoutePath
FROM SysMenu m
JOIN SysRoleMenu rm ON rm.MenuId = m.Id
JOIN SysRole r ON rm.RoleId = r.Id
WHERE r.Code = 'admin' AND m.Name IN ('web-package', 'build-manager', 'universal-build', 'build-release')
ORDER BY m.Id;
GO
-- 检查所有构建发布下的菜单
SELECT m.Id, m.ParentId, m.Title, m.Name, m.Component, m.SortOrder
FROM SysMenu m
WHERE m.ParentId = (SELECT Id FROM SysMenu WHERE Title = N'构建发布' AND ParentId IS NULL)
   OR m.Title = N'构建发布'
ORDER BY m.SortOrder;
GO
-- 检查 SysMenu 表中 web-package 的完整信息
SELECT Id, ParentId, Title, Name, Component, RoutePath, Visible, Enabled, SortOrder
FROM SysMenu WHERE Name = 'web-package';
GO

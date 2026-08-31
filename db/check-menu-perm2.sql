-- 检查 admin 角色是否有 web-package 等菜单权限
SELECT m.Id, m.ParentId, m.Title, m.Name, m.Component
FROM SysMenu m
JOIN SysRoleMenu rm ON rm.MenuId = m.Id
JOIN SysRole r ON rm.RoleId = r.Id
WHERE r.Code = 'admin' AND m.Name IN ('web-package', 'build-manager', 'universal-build', 'build-release')
ORDER BY m.Id;
GO
-- 检查 admin 角色的所有构建相关菜单
SELECT m.Id, m.ParentId, m.Title, m.Name
FROM SysMenu m
JOIN SysRoleMenu rm ON rm.MenuId = m.Id
JOIN SysRole r ON rm.RoleId = r.Id
WHERE r.Code = 'admin' AND (m.ParentId = 63 OR m.Id = 63)
ORDER BY m.SortOrder;
GO

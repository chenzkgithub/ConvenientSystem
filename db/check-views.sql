-- 检查 web-package 和 universal-build 视图/权限是否存在
SELECT 'VIEWS' AS Section, Id, Name, Title FROM SysView WHERE Name IN ('web-package', 'universal-build', 'build-manager') ORDER BY Id;
GO
SELECT 'PERMS' AS Section, Id, ViewId, Name, Title FROM SysViewPermission WHERE Name LIKE 'web-package%' OR Name LIKE 'universal-build%' OR Name LIKE 'build-manager%' ORDER BY Id;
GO
SELECT 'ROLE_PERMS' AS Section, rvp.RoleId, vp.Name
FROM SysRoleViewPerm rvp
JOIN SysViewPermission vp ON rvp.ViewPermId = vp.Id
JOIN SysRole r ON rvp.RoleId = r.Id
WHERE r.Code = 'admin' AND (vp.Name LIKE 'web-package%' OR vp.Name LIKE 'universal-build%' OR vp.Name LIKE 'build-manager%')
ORDER BY vp.Id;
GO

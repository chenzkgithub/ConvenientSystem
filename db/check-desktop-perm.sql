-- 检查 desktop-package 权限是否存在
SELECT vp.Id, vp.ViewId, vp.Name, vp.Title, vp.Enabled
FROM SysViewPermission vp
WHERE vp.Name LIKE 'desktop-package%'
ORDER BY vp.Id;
GO

-- 检查 admin 角色是否有 desktop-package 权限
SELECT rvp.RoleId, r.Code AS RoleCode, rvp.ViewPermId, vp.Name AS PermName
FROM SysRoleViewPerm rvp
JOIN SysRole r ON rvp.RoleId = r.Id
JOIN SysViewPermission vp ON rvp.ViewPermId = vp.Id
WHERE r.Code = 'admin' AND vp.Name LIKE 'desktop-package%';
GO

-- 检查 SysView 中是否有 desktop-package 视图
SELECT Id, Name, Title, Enabled FROM SysView WHERE Name = 'desktop-package';
GO
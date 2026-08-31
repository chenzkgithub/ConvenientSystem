-- 补充 admin 角色缺失的视图权限点（幂等，只插入不存在的）
INSERT INTO dbo.SysRoleViewPerm (RoleId, ViewPermId)
SELECT r.Id, vp.Id
FROM dbo.SysRole r CROSS JOIN dbo.SysViewPermission vp
WHERE r.Code = N'admin'
  AND NOT EXISTS (SELECT 1 FROM dbo.SysRoleViewPerm rvp WHERE rvp.RoleId = r.Id AND rvp.ViewPermId = vp.Id);
GO

-- 验证：admin 是否有 desktop-package 权限
SELECT rvp.RoleId, r.Code AS RoleCode, rvp.ViewPermId, vp.Name AS PermName
FROM SysRoleViewPerm rvp
JOIN SysRole r ON rvp.RoleId = r.Id
JOIN SysViewPermission vp ON rvp.ViewPermId = vp.Id
WHERE r.Code = 'admin' AND vp.Name LIKE 'desktop-package%';
GO
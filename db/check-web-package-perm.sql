-- ① 检查 SysMenu 中 Name='web-package' 的菜单
SELECT Id, Title, Name, ParentId, Enabled FROM SysMenu WHERE Name = 'web-package';
GO

-- ② 检查 admin 角色是否分配了该菜单（SysRoleMenu）
SELECT rm.RoleId, r.Code AS RoleCode, rm.MenuId, m.Name AS MenuName
FROM SysRoleMenu rm
JOIN SysRole r ON rm.RoleId = r.Id
JOIN SysMenu m ON rm.MenuId = m.Id
WHERE r.Code = 'admin' AND m.Name = 'web-package';
GO

-- ③ 检查 SysViewPermission 中 Name='web-package'（无冒号的页面级权限码）
SELECT vp.Id, vp.ViewId, vp.Name, vp.Title, vp.Enabled
FROM SysViewPermission vp WHERE vp.Name = 'web-package';
GO

-- ④ 检查 admin 角色的视图权限中是否有 web-package（无冒号）
SELECT rvp.RoleId, r.Code AS RoleCode, rvp.ViewPermId, vp.Name AS PermName
FROM SysRoleViewPerm rvp
JOIN SysRole r ON rvp.RoleId = r.Id
JOIN SysViewPermission vp ON rvp.ViewPermId = vp.Id
WHERE r.Code = 'admin' AND vp.Name = 'web-package';
GO

-- ⑤ 模拟 LoginService.LoadPermissionsAsync：admin 最终拿到的 menuCodes
-- 菜单码部分
SELECT DISTINCT m.Name AS MenuCode
FROM SysRoleMenu rm
JOIN SysRole r ON rm.RoleId = r.Id
JOIN SysMenu m ON rm.MenuId = m.Id
WHERE r.Code = 'admin' AND m.Name IS NOT NULL AND m.Name != ''
  AND r.Enabled = 1
GO

-- 视图权限码部分
SELECT DISTINCT vp.Name AS ViewPermCode
FROM SysRoleViewPerm rvp
JOIN SysRole r ON rvp.RoleId = r.Id
JOIN SysViewPermission vp ON rvp.ViewPermId = vp.Id
WHERE r.Code = 'admin' AND vp.Enabled = 1 AND r.Enabled = 1
  AND vp.Name LIKE 'web-package%'
ORDER BY vp.Name;
GO

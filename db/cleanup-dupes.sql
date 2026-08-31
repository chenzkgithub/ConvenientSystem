-- 清理重复的 build-manager:execute 权限（保留 Id=77，删除 Id=1074 和 1075）
-- 先删除 SysRoleViewPerm 中的引用
DELETE FROM SysRoleViewPerm WHERE ViewPermId IN (1074, 1075);
GO
-- 删除重复的权限点
DELETE FROM SysViewPermission WHERE Id IN (1074, 1075);
GO
-- 验证清理结果
SELECT vp.Id, vp.ViewId, vp.Name, vp.Enabled FROM SysViewPermission vp
WHERE vp.Name LIKE 'build-manager%' ORDER BY vp.Id;
GO
-- 验证 admin 角色权限
SELECT vp.Id, vp.Name FROM SysViewPermission vp
JOIN SysRoleViewPerm rvp ON rvp.ViewPermId = vp.Id
JOIN SysRole r ON rvp.RoleId = r.Id
WHERE r.Code = 'admin' AND (vp.Name LIKE 'web-package%' OR vp.Name LIKE 'build-manager%' OR vp.Name LIKE 'universal-build%')
ORDER BY vp.Id;
GO

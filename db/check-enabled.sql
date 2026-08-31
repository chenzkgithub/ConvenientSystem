-- 检查视图权限的 Enabled 字段
SELECT vp.Id, vp.ViewId, vp.Name, vp.Title, vp.Enabled
FROM SysViewPermission vp
WHERE vp.Name LIKE 'web-package%' OR vp.Name LIKE 'universal-build%' OR vp.Name LIKE 'build-manager%'
ORDER BY vp.Id;
GO
-- 检查视图的 Enabled 字段
SELECT v.Id, v.Name, v.Title, v.Enabled FROM SysView v
WHERE v.Name IN ('web-package', 'universal-build', 'build-manager')
ORDER BY v.Id;
GO

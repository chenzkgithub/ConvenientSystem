-- 013 新建"日志"分组，将审计日志、错误日志、实时日志归入其下

DECLARE @SysMgmtId INT = (SELECT TOP 1 Id FROM dbo.SysMenu WHERE Title = N'系统管理' AND ParentId IS NULL);

-- 1. 创建"日志"分组（幂等）
IF NOT EXISTS (SELECT 1 FROM dbo.SysMenu WHERE Name = N'log-group') AND @SysMgmtId IS NOT NULL
    INSERT INTO dbo.SysMenu (ParentId, Title, Page, IsFloat, Visible, IsExternal, Editable, Enabled, Name, Component, SortOrder)
    VALUES (@SysMgmtId, N'日志', NULL, 0, 1, 0, 1, 1, N'log-group', NULL, 14);

-- 2. 将三个日志菜单移入"日志"分组
DECLARE @LogGroupId INT = (SELECT TOP 1 Id FROM dbo.SysMenu WHERE Name = N'log-group');
IF @LogGroupId IS NOT NULL
BEGIN
    UPDATE dbo.SysMenu SET ParentId = @LogGroupId, SortOrder = 1 WHERE Name = N'audit-log';
    UPDATE dbo.SysMenu SET ParentId = @LogGroupId, SortOrder = 2 WHERE Name = N'error-log';
    UPDATE dbo.SysMenu SET ParentId = @LogGroupId, SortOrder = 3 WHERE Name = N'log-viewer';
END

-- 3. 为管理员角色授予"日志"分组菜单
IF @LogGroupId IS NOT NULL
    AND NOT EXISTS (SELECT 1 FROM dbo.SysRoleMenu rm WHERE rm.RoleId = 1 AND rm.MenuId = @LogGroupId)
    INSERT INTO dbo.SysRoleMenu (RoleId, MenuId) VALUES (1, @LogGroupId);
GO

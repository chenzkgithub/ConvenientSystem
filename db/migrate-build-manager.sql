-- 注册本地构建与发布工具页面
-- 执行条件： ConvenientSystem 已初始化，存在 SysView/SysMenu/SysViewPermission/SysRoleViewPerm 表

DECLARE @viewId INT;
DECLARE @permExecuteId INT = 77;
DECLARE @permPublishId INT = 78;

-- 1. 注册视图
IF NOT EXISTS (SELECT 1 FROM dbo.SysView WHERE Name = N'build-manager')
BEGIN
    INSERT INTO dbo.SysView (Name, Title, Component, RoutePath, SortOrder)
    VALUES (N'build-manager', N'构建与发布', N'/src/common/views/BuildManagerView.vue', N'/build-manager', 40);
END

SELECT @viewId = Id FROM dbo.SysView WHERE Name = N'build-manager';

-- 2. 注册权限点
IF NOT EXISTS (SELECT 1 FROM dbo.SysViewPermission WHERE Id = @permExecuteId)
BEGIN
    SET IDENTITY_INSERT dbo.SysViewPermission ON;
    INSERT INTO dbo.SysViewPermission (Id, ViewId, Name, Title, SortOrder)
    VALUES (@permExecuteId, @viewId, N'build-manager:execute', N'执行构建', 1);
    SET IDENTITY_INSERT dbo.SysViewPermission OFF;
END

IF NOT EXISTS (SELECT 1 FROM dbo.SysViewPermission WHERE Id = @permPublishId)
BEGIN
    SET IDENTITY_INSERT dbo.SysViewPermission ON;
    INSERT INTO dbo.SysViewPermission (Id, ViewId, Name, Title, SortOrder)
    VALUES (@permPublishId, @viewId, N'build-manager:publish', N'发布到服务器', 2);
    SET IDENTITY_INSERT dbo.SysViewPermission OFF;
END

-- 3. 注册菜单（放到"开发工具"分组下，Id=24）
IF NOT EXISTS (SELECT 1 FROM dbo.SysMenu WHERE Name = N'build-manager')
BEGIN
    INSERT INTO dbo.SysMenu (ParentId, Title, Page, IsFloat, Visible, IsExternal, Editable, Enabled, Name, Component, SortOrder, Type)
    VALUES (24, N'构建与发布', N'/build-manager', 1, 1, 0, 1, 1, N'build-manager', N'/src/common/views/BuildManagerView.vue', 5, 1);
END

-- 4. admin 角色自动拥有该视图权限点
INSERT INTO dbo.SysRoleViewPerm (RoleId, ViewPermId)
SELECT r.Id, vp.Id
FROM dbo.SysRole r CROSS JOIN dbo.SysViewPermission vp
WHERE r.Code = N'admin'
  AND vp.ViewId = @viewId
  AND NOT EXISTS (SELECT 1 FROM dbo.SysRoleViewPerm rvp WHERE rvp.RoleId = r.Id AND rvp.ViewPermId = vp.Id);

-- 5. admin 角色自动拥有该菜单（否则菜单管理/左侧菜单树都看不到）
INSERT INTO dbo.SysRoleMenu (RoleId, MenuId)
SELECT r.Id, m.Id
FROM dbo.SysRole r CROSS JOIN dbo.SysMenu m
WHERE r.Code = N'admin'
  AND m.Name = N'build-manager'
  AND NOT EXISTS (SELECT 1 FROM dbo.SysRoleMenu rm WHERE rm.RoleId = r.Id AND rm.MenuId = m.Id);
GO

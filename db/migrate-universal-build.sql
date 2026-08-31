-- 注册通用构建发布工具页面
-- 执行条件： ConvenientSystem 已初始化，存在 SysView/SysMenu/SysViewPermission/SysRoleViewPerm 表

DECLARE @viewId INT;
DECLARE @permExecuteId INT;
DECLARE @devToolsMenuId INT;

-- 找到"开发工具"分组 Id（兼容不同环境）
SELECT TOP 1 @devToolsMenuId = Id FROM dbo.SysMenu WHERE Title = N'开发工具' AND Type = 0 ORDER BY Id;

IF @devToolsMenuId IS NULL
BEGIN
    RAISERROR(N'未找到"开发工具"菜单分组，请先确认菜单基础数据已初始化', 16, 1);
    RETURN;
END

-- 1. 注册视图
IF NOT EXISTS (SELECT 1 FROM dbo.SysView WHERE Name = N'universal-build')
BEGIN
    INSERT INTO dbo.SysView (Name, Title, Component, RoutePath, SortOrder)
    VALUES (N'universal-build', N'通用构建发布', N'/src/common/views/UniversalBuildView.vue', N'/universal-build', 41);
END

SELECT @viewId = Id FROM dbo.SysView WHERE Name = N'universal-build';

IF @viewId IS NULL
BEGIN
    RAISERROR(N'SysView 注册失败', 16, 1);
    RETURN;
END

-- 2. 注册权限点（动态取下一个可用 Id，避免冲突）
IF NOT EXISTS (SELECT 1 FROM dbo.SysViewPermission WHERE Name = N'universal-build:execute')
BEGIN
    SELECT @permExecuteId = ISNULL(MAX(Id), 0) + 1 FROM dbo.SysViewPermission;

    SET IDENTITY_INSERT dbo.SysViewPermission ON;
    INSERT INTO dbo.SysViewPermission (Id, ViewId, Name, Title, SortOrder)
    VALUES (@permExecuteId, @viewId, N'universal-build:execute', N'执行构建', 1);
    SET IDENTITY_INSERT dbo.SysViewPermission OFF;
END
ELSE
BEGIN
    SELECT @permExecuteId = Id FROM dbo.SysViewPermission WHERE Name = N'universal-build:execute';
END

-- 3. 注册菜单（放到"开发工具"分组下）
IF NOT EXISTS (SELECT 1 FROM dbo.SysMenu WHERE Name = N'universal-build')
BEGIN
    INSERT INTO dbo.SysMenu (ParentId, Title, Page, IsFloat, Visible, IsExternal, Editable, Enabled, Name, Component, SortOrder, Type)
    VALUES (@devToolsMenuId, N'通用构建发布', N'/universal-build', 1, 1, 0, 1, 1, N'universal-build', N'/src/common/views/UniversalBuildView.vue', 6, 1);
END

-- 4. admin 角色自动拥有该视图权限点
INSERT INTO dbo.SysRoleViewPerm (RoleId, ViewPermId)
SELECT r.Id, vp.Id
FROM dbo.SysRole r CROSS JOIN dbo.SysViewPermission vp
WHERE r.Code = N'admin'
  AND vp.ViewId = @viewId
  AND NOT EXISTS (SELECT 1 FROM dbo.SysRoleViewPerm rvp WHERE rvp.RoleId = r.Id AND rvp.ViewPermId = vp.Id);

-- 5. admin 角色自动拥有该菜单
INSERT INTO dbo.SysRoleMenu (RoleId, MenuId)
SELECT r.Id, m.Id
FROM dbo.SysRole r CROSS JOIN dbo.SysMenu m
WHERE r.Code = N'admin'
  AND m.Name = N'universal-build'
  AND NOT EXISTS (SELECT 1 FROM dbo.SysRoleMenu rm WHERE rm.RoleId = r.Id AND rm.MenuId = m.Id);
GO

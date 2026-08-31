-- 迁移脚本：新建"构建发布"一级菜单，将 Web版本管理、构建与发布、通用构建发布移入
-- 执行条件：ConvenientSystem 已初始化，SysView/SysMenu/SysViewPermission/SysRoleViewPerm/SysRoleMenu 表存在

DECLARE @buildGroupId INT;
DECLARE @buildManagerViewId INT;
DECLARE @permExecuteId INT;
DECLARE @permPublishId INT;

-- ========== 1. 查找或创建一级菜单"构建发布" ==========
SELECT TOP 1 @buildGroupId = Id FROM dbo.SysMenu WHERE Title = N'构建发布' AND ParentId IS NULL ORDER BY Id;

IF @buildGroupId IS NULL
BEGIN
    INSERT INTO dbo.SysMenu (ParentId, Title, Page, IsFloat, Visible, IsExternal, Editable, Enabled, Name, Component, SortOrder, Type)
    VALUES (NULL, N'构建发布', NULL, 0, 1, 0, 1, 1, NULL, NULL, 5, 0);
    SET @buildGroupId = SCOPE_IDENTITY();
    PRINT N'已创建一级菜单"构建发布"，Id=' + CAST(@buildGroupId AS NVARCHAR(20));
END

-- ========== 2. 常用工具到知识库的 SortOrder +1（为构建发布腾出位置 5） ==========
UPDATE dbo.SysMenu SET SortOrder = SortOrder + 1
WHERE ParentId IS NULL AND SortOrder >= 5 AND Id <> @buildGroupId;

-- ========== 3. 注册 build-manager 视图（如果尚未注册） ==========
SELECT TOP 1 @buildManagerViewId = Id FROM dbo.SysView WHERE Name = N'build-manager' ORDER BY Id;

IF @buildManagerViewId IS NULL
BEGIN
    INSERT INTO dbo.SysView (Name, Title, Component, RoutePath, SortOrder)
    VALUES (N'build-manager', N'构建与发布', N'/src/common/views/BuildManagerView.vue', N'/build-manager', 41);
    SET @buildManagerViewId = SCOPE_IDENTITY();
    PRINT N'已注册视图 build-manager，Id=' + CAST(@buildManagerViewId AS NVARCHAR(20));
END

-- ========== 4. 注册 build-manager 权限点（如果尚未注册） ==========
-- 注意：init.sql 中 Id=78/79 已预留给 build-manager 权限点
IF NOT EXISTS (SELECT 1 FROM dbo.SysViewPermission WHERE Name = N'build-manager:execute')
BEGIN
    SELECT @permExecuteId = Id FROM dbo.SysViewPermission WHERE Name = N'build-manager:execute';
END
IF @permExecuteId IS NULL
BEGIN
    INSERT INTO dbo.SysViewPermission (ViewId, Name, Title, SortOrder)
    VALUES (@buildManagerViewId, N'build-manager:execute', N'执行构建', 1);
    SET @permExecuteId = SCOPE_IDENTITY();
END

IF NOT EXISTS (SELECT 1 FROM dbo.SysViewPermission WHERE Name = N'build-manager:publish')
BEGIN
    INSERT INTO dbo.SysViewPermission (ViewId, Name, Title, SortOrder)
    VALUES (@buildManagerViewId, N'build-manager:publish', N'发布到服务器', 2);
    SET @permPublishId = SCOPE_IDENTITY();
END

-- ========== 5. admin 角色自动拥有 build-manager 权限点 ==========
INSERT INTO dbo.SysRoleViewPerm (RoleId, ViewPermId)
SELECT r.Id, vp.Id
FROM dbo.SysRole r CROSS JOIN dbo.SysViewPermission vp
WHERE r.Code = N'admin'
  AND vp.ViewId = @buildManagerViewId
  AND NOT EXISTS (SELECT 1 FROM dbo.SysRoleViewPerm rvp WHERE rvp.RoleId = r.Id AND rvp.ViewPermId = vp.Id);

-- ========== 6. 移动 Web版本管理到构建发布下 ==========
UPDATE dbo.SysMenu SET ParentId = @buildGroupId, SortOrder = 1
WHERE Name = N'web-package' AND ParentId = 1;
PRINT N'Web版本管理已移至构建发布下';

-- ========== 7. 移动构建与发布到构建发布下（如果存在） ==========
IF NOT EXISTS (SELECT 1 FROM dbo.SysMenu WHERE Name = N'build-manager')
BEGIN
    INSERT INTO dbo.SysMenu (ParentId, Title, Page, IsFloat, Visible, IsExternal, Editable, Enabled, Name, Component, SortOrder, Type)
    VALUES (@buildGroupId, N'构建与发布', N'/build-manager', 1, 1, 0, 1, 1, N'build-manager', N'/src/common/views/BuildManagerView.vue', 2, 1);
    PRINT N'已创建菜单"构建与发布"';
END
ELSE
BEGIN
    UPDATE dbo.SysMenu SET ParentId = @buildGroupId, SortOrder = 2
    WHERE Name = N'build-manager';
    PRINT N'构建与发布已移至构建发布下';
END

-- ========== 8. 移动通用构建发布到构建发布下 ==========
UPDATE dbo.SysMenu SET ParentId = @buildGroupId, SortOrder = 3
WHERE Name = N'universal-build';
PRINT N'通用构建发布已移至构建发布下';

-- ========== 9. admin 角色自动拥有新菜单 ==========
INSERT INTO dbo.SysRoleMenu (RoleId, MenuId)
SELECT r.Id, m.Id
FROM dbo.SysRole r CROSS JOIN dbo.SysMenu m
WHERE r.Code = N'admin'
  AND (m.Id = @buildGroupId OR m.Name IN (N'web-package', N'build-manager', N'universal-build'))
  AND NOT EXISTS (SELECT 1 FROM dbo.SysRoleMenu rm WHERE rm.RoleId = r.Id AND rm.MenuId = m.Id);

PRINT N'迁移完成：构建发布一级菜单已创建，三个子菜单已移入';
GO

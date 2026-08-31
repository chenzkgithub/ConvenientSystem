-- ============================================================================
-- 迁移脚本：新增桌面安装包自更新功能所需的数据库对象
-- 适用场景：已存在的数据库升级到支持桌面程序自更新。
-- 执行方式：sqlcmd -S <server> -d ConvenientSystem -U <user> -P <pwd> -i migrate-desktop-update.sql
-- 幂等性：  已存在时不会重复创建/插入。
-- ============================================================================
USE ConvenientSystem;
GO

-- 1. 桌面安装包版本表
IF OBJECT_ID(N'dbo.DesktopPackage') IS NULL
BEGIN
    CREATE TABLE dbo.DesktopPackage (
        Id              INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Version         NVARCHAR(50)      NOT NULL,
        FileName        NVARCHAR(200)     NOT NULL,
        FileSize        BIGINT            NOT NULL DEFAULT 0,
        Description     NVARCHAR(500)     NULL,
        IsActive        BIT               NOT NULL DEFAULT 0,
        CreateTime      DATETIME          NOT NULL DEFAULT GETDATE(),
        CreatedById     UNIQUEIDENTIFIER  NULL
    );
END
GO

-- 2. 桌面安装包视图注册（与 Web版本管理 同页面，作为页签）
IF NOT EXISTS (SELECT 1 FROM dbo.SysView WHERE Name = N'desktop-package')
BEGIN
    SET IDENTITY_INSERT dbo.SysView ON;
    INSERT INTO dbo.SysView (Id, Name, Title, Component, RoutePath, SortOrder)
    VALUES (39, N'desktop-package', N'桌面安装包', N'/src/common/views/WebPackageView.vue', N'/web-package', 39);
    SET IDENTITY_INSERT dbo.SysView OFF;
END
GO

-- 3. 桌面安装包权限点
IF NOT EXISTS (SELECT 1 FROM dbo.SysViewPermission WHERE Name = N'desktop-package')
BEGIN
    SET IDENTITY_INSERT dbo.SysViewPermission ON;
    INSERT INTO dbo.SysViewPermission (Id, ViewId, Name, Title, SortOrder) VALUES
        (73, 39, N'desktop-package',          N'查看桌面安装包', 0),
        (74, 39, N'desktop-package:upload',   N'上传安装包', 1),
        (75, 39, N'desktop-package:activate', N'激活安装包', 2),
        (76, 39, N'desktop-package:delete',   N'删除安装包', 3);
    SET IDENTITY_INSERT dbo.SysViewPermission OFF;
END
GO

-- 4. admin 角色自动获得所有视图权限点（包含新增的 desktop-package 权限）
INSERT INTO dbo.SysRoleViewPerm (RoleId, ViewPermId)
SELECT r.Id, vp.Id
FROM dbo.SysRole r CROSS JOIN dbo.SysViewPermission vp
WHERE r.Code = N'admin'
  AND NOT EXISTS (SELECT 1 FROM dbo.SysRoleViewPerm rvp WHERE rvp.RoleId = r.Id AND rvp.ViewPermId = vp.Id);
GO

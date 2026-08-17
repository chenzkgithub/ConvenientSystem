-- 012_add_sysuser_menu.sql
-- 用户级菜单授权表：在角色权限之外，为用户额外授予菜单（加法模型，不做 deny）

IF OBJECT_ID(N'dbo.SysUserMenu') IS NULL
BEGIN
    CREATE TABLE dbo.SysUserMenu (
        Id     INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        UserId UNIQUEIDENTIFIER NOT NULL,
        MenuId INT NOT NULL,
        CONSTRAINT UQ_SysUserMenu UNIQUE (UserId, MenuId)
    );
END
GO

EXEC dbo.usp_AddTableComment N'SysUserMenu', N'用户菜单授权表（角色权限之外的额外授予）';
EXEC dbo.usp_AddColumnComment N'SysUserMenu', N'Id',     N'主键';
EXEC dbo.usp_AddColumnComment N'SysUserMenu', N'UserId', N'用户 Id（SysUser.Id，GUID）';
EXEC dbo.usp_AddColumnComment N'SysUserMenu', N'MenuId', N'菜单 Id（SysMenu.Id）';
GO

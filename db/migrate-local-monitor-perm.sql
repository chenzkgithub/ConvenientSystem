-- ============================================================================
-- 迁移脚本：为本机监控"清理磁盘"按钮权限点补授权给 admin 角色
-- 适用场景：在主机监控重构为本机监控后，已有数据库的 admin 角色需要获得
--          local-monitor:clean-disk 权限，否则前端不会显示"清理磁盘"按钮。
-- 执行方式：sqlcmd -S <server> -d ConvenientSystem -U <user> -P <pwd> -i migrate-local-monitor-perm.sql
-- 幂等性：  已授权时不会重复插入。
-- ============================================================================
USE ConvenientSystem;
GO

-- 确保 local-monitor:clean-disk 视图权限点存在（如不存在则补充，避免旧库缺失）
IF NOT EXISTS (SELECT 1 FROM dbo.SysViewPermission WHERE Name = N'local-monitor:clean-disk')
BEGIN
    DECLARE @viewId INT;
    SELECT @viewId = Id FROM dbo.SysView WHERE Name = N'local-monitor';

    IF @viewId IS NOT NULL
    BEGIN
        SET IDENTITY_INSERT dbo.SysViewPermission ON;
        INSERT INTO dbo.SysViewPermission (Id, ViewId, Name, Title, SortOrder)
        VALUES (61, @viewId, N'local-monitor:clean-disk', N'清理磁盘', 1);
        SET IDENTITY_INSERT dbo.SysViewPermission OFF;
    END
END
GO

-- 为 admin 角色补充分配 local-monitor:clean-disk 权限点
INSERT INTO dbo.SysRoleViewPerm (RoleId, ViewPermId)
SELECT r.Id, vp.Id
FROM dbo.SysRole r CROSS JOIN dbo.SysViewPermission vp
WHERE r.Code = N'admin'
  AND vp.Name = N'local-monitor:clean-disk'
  AND NOT EXISTS (SELECT 1 FROM dbo.SysRoleViewPerm rvp WHERE rvp.RoleId = r.Id AND rvp.ViewPermId = vp.Id);
GO

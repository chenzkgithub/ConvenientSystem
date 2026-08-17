-- 007 修复菜单（按 Name/Title 匹配，不依赖固定 Id）

-- 1. 删除短信发送任务菜单
DELETE FROM dbo.SysRoleMenu WHERE MenuId IN (SELECT Id FROM dbo.SysMenu WHERE Name = N'sms-task');
DELETE FROM dbo.SysMenu WHERE Name = N'sms-task';
GO

-- 2. 删除邮件定时任务菜单
DELETE FROM dbo.SysRoleMenu WHERE MenuId IN (SELECT Id FROM dbo.SysMenu WHERE Name = N'email-task');
DELETE FROM dbo.SysMenu WHERE Name = N'email-task';
GO

-- 3. 移动群机器人菜单到机器人一级菜单下
UPDATE dbo.SysMenu SET ParentId = 44, SortOrder = 1 WHERE Name = N'webhook-config' AND ParentId IS NOT NULL;
GO

-- 4. 机器人菜单 SortOrder 调整为 7（紧接邮件通知 SortOrder=6 之后）
--    当前 >=7 的顶层菜单各 +1
UPDATE dbo.SysMenu SET SortOrder = SortOrder + 1 WHERE ParentId IS NULL AND SortOrder >= 7;
GO

-- 5. 设置机器人菜单 SortOrder=7
UPDATE dbo.SysMenu SET SortOrder = 7 WHERE Id = 44;
GO

-- 6. 短信管理子菜单 SortOrder 重排（删除 sms-task 后 1,3,4 → 1,2,3）
UPDATE dbo.SysMenu SET SortOrder = 2 WHERE Name = N'sms-log' AND ParentId = (SELECT Id FROM dbo.SysMenu WHERE Title = N'短信管理');
UPDATE dbo.SysMenu SET SortOrder = 3 WHERE Name = N'sms-config' AND ParentId = (SELECT Id FROM dbo.SysMenu WHERE Title = N'短信管理');
GO

-- 7. 邮件通知子菜单 SortOrder 重排（删除 email-task 后 1,3 → 1,2）
UPDATE dbo.SysMenu SET SortOrder = 2 WHERE Name = N'email-log' AND ParentId = (SELECT Id FROM dbo.SysMenu WHERE Title = N'邮件通知');
GO

-- 8. 系统管理子菜单 SortOrder 重排（移除 webhook-config 后重排）
UPDATE m SET SortOrder = newSort.NewSortOrder
FROM dbo.SysMenu m
INNER JOIN (
    SELECT Id, ROW_NUMBER() OVER (ORDER BY SortOrder) AS NewSortOrder
    FROM dbo.SysMenu
    WHERE ParentId = (SELECT Id FROM dbo.SysMenu WHERE Title = N'系统管理')
) newSort ON m.Id = newSort.Id;
GO

-- 9. admin 角色补齐机器人菜单（44, 45, 群机器人）
INSERT INTO dbo.SysRoleMenu (RoleId, MenuId)
SELECT r.Id, m.Id
FROM dbo.SysRole r CROSS JOIN dbo.SysMenu m
WHERE r.Code = N'admin'
  AND m.Id IN (44, 45)
  AND NOT EXISTS (SELECT 1 FROM dbo.SysRoleMenu rm WHERE rm.RoleId = r.Id AND rm.MenuId = m.Id);
GO

-- 10. admin 角色补齐群机器人菜单
INSERT INTO dbo.SysRoleMenu (RoleId, MenuId)
SELECT r.Id, m.Id
FROM dbo.SysRole r CROSS JOIN dbo.SysMenu m
WHERE r.Code = N'admin'
  AND m.Name = N'webhook-config'
  AND NOT EXISTS (SELECT 1 FROM dbo.SysRoleMenu rm WHERE rm.RoleId = r.Id AND rm.MenuId = m.Id);
GO

-- 11. 普通用户角色授予发送日志权限
INSERT INTO dbo.SysRoleMenu (RoleId, MenuId)
SELECT r.Id, m.Id
FROM dbo.SysRole r CROSS JOIN dbo.SysMenu m
WHERE r.Code = N'user'
  AND m.Id = 45
  AND NOT EXISTS (SELECT 1 FROM dbo.SysRoleMenu rm WHERE rm.RoleId = r.Id AND rm.MenuId = m.Id);
GO

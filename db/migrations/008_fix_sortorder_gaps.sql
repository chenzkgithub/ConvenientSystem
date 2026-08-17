-- 008 修复 SortOrder 间隙
UPDATE dbo.SysMenu SET SortOrder = 2 WHERE Name = N'sms-log';
UPDATE dbo.SysMenu SET SortOrder = 3 WHERE Name = N'sms-config';
UPDATE dbo.SysMenu SET SortOrder = 2 WHERE Name = N'email-log';
UPDATE dbo.SysMenu SET SortOrder = 6 WHERE Name = N'online-users';
UPDATE dbo.SysMenu SET SortOrder = 7 WHERE Name = N'error-log';
UPDATE dbo.SysMenu SET SortOrder = 8 WHERE Name = N'notice';
GO

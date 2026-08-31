-- 检查构建发布父菜单的完整信息
SELECT Id, ParentId, Title, Name, Component, Visible, Enabled, SortOrder, Type
FROM SysMenu WHERE Title = N'构建发布' AND ParentId IS NULL;
GO
-- 检查所有一级菜单的 Name
SELECT Id, Title, Name, SortOrder FROM SysMenu WHERE ParentId IS NULL ORDER BY SortOrder;
GO

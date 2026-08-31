SELECT Id, Title, SortOrder FROM SysMenu WHERE ParentId IS NULL ORDER BY SortOrder;
GO
SELECT Id, ParentId, Title, Name, SortOrder FROM SysMenu WHERE ParentId = (SELECT Id FROM SysMenu WHERE Title = N'构建发布' AND ParentId IS NULL) ORDER BY SortOrder;
GO

-- 新增菜单：系统运行大盘、定时任务管理、实时日志
-- 放在"系统管理"分组下

DECLARE @SysMgmtId INT = (SELECT TOP 1 Id FROM dbo.SysMenu WHERE Title = N'系统管理' AND ParentId IS NULL);

-- 1. 系统运行大盘
IF NOT EXISTS (SELECT 1 FROM dbo.SysMenu WHERE Name = N'system-dashboard') AND @SysMgmtId IS NOT NULL
    INSERT INTO dbo.SysMenu (ParentId, Title, Page, IsFloat, Visible, IsExternal, Editable, Enabled, Name, Component, SortOrder)
    VALUES (@SysMgmtId, N'系统大盘', N'/system-dashboard', 0, 1, 0, 1, 1, N'system-dashboard', N'/src/common/views/SystemDashboardView.vue', 11);

-- 2. 定时任务管理
IF NOT EXISTS (SELECT 1 FROM dbo.SysMenu WHERE Name = N'hangfire-jobs') AND @SysMgmtId IS NOT NULL
    INSERT INTO dbo.SysMenu (ParentId, Title, Page, IsFloat, Visible, IsExternal, Editable, Enabled, Name, Component, SortOrder)
    VALUES (@SysMgmtId, N'定时任务', N'/hangfire-jobs', 0, 1, 0, 1, 1, N'hangfire-jobs', N'/src/common/views/HangfireJobsView.vue', 12);

-- 3. 实时日志
IF NOT EXISTS (SELECT 1 FROM dbo.SysMenu WHERE Name = N'log-viewer') AND @SysMgmtId IS NOT NULL
    INSERT INTO dbo.SysMenu (ParentId, Title, Page, IsFloat, Visible, IsExternal, Editable, Enabled, Name, Component, SortOrder)
    VALUES (@SysMgmtId, N'实时日志', N'/log-viewer', 0, 1, 0, 1, 1, N'log-viewer', N'/src/common/views/LogViewerView.vue', 13);
GO

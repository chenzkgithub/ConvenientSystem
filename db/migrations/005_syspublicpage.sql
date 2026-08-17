/* =============================================================
   005_syspublicpage.sql
   ---------------------------------------------------------------
   新建 SysPublicPage 表（外部公开页面配置管理），
   迁移 3 条现有硬编码路由为种子数据，新增管理菜单。
   幂等可重复执行。
   ============================================================= */

USE [ConvenientSystem];
GO

-- 建表
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'SysPublicPage')
BEGIN
    CREATE TABLE dbo.SysPublicPage (
        Id          INT IDENTITY(1,1)   NOT NULL PRIMARY KEY,
        PageKey     NVARCHAR(100)       NOT NULL,
        Title       NVARCHAR(100)       NOT NULL,
        Component   NVARCHAR(200)       NOT NULL,
        Description NVARCHAR(500)       NULL,
        Enabled     BIT                 NOT NULL DEFAULT 1,
        SortOrder   INT                 NOT NULL DEFAULT 0,
        CreatedAt   DATETIME2           NOT NULL DEFAULT SYSUTCDATETIME(),
        UpdatedAt   DATETIME2           NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_SysPublicPage_PageKey UNIQUE (PageKey)
    );
END
GO

-- 表与列注释
IF NOT EXISTS (
    SELECT 1 FROM sys.extended_properties ep
    JOIN sys.objects o ON ep.major_id = o.object_id
    WHERE ep.name = N'MS_Description' AND ep.class = 1 AND ep.minor_id = 0
      AND o.name = N'SysPublicPage' AND SCHEMA_NAME(o.schema_id) = N'dbo'
)
    EXEC sys.sp_addextendedproperty
        @name = N'MS_Description', @value = N'外部公开页面表（免登录页面配置管理，访问链接带 public=1）',
        @level0type = N'SCHEMA', @level0name = N'dbo',
        @level1type = N'TABLE',  @level1name = N'SysPublicPage';
GO

EXEC dbo.usp_AddColumnComment N'SysPublicPage', N'PageKey',     N'路由路径，如 /lottery-trend';
EXEC dbo.usp_AddColumnComment N'SysPublicPage', N'Title',       N'显示名称';
EXEC dbo.usp_AddColumnComment N'SysPublicPage', N'Component',   N'Vue 组件路径，如 /src/common/views/PublicLotteryTrendView.vue';
EXEC dbo.usp_AddColumnComment N'SysPublicPage', N'Description', N'描述说明';
EXEC dbo.usp_AddColumnComment N'SysPublicPage', N'Enabled',     N'是否启用';
EXEC dbo.usp_AddColumnComment N'SysPublicPage', N'SortOrder',   N'排序号';
EXEC dbo.usp_AddColumnComment N'SysPublicPage', N'CreatedAt',   N'创建时间';
EXEC dbo.usp_AddColumnComment N'SysPublicPage', N'UpdatedAt',   N'更新时间';
GO

-- 种子数据（幂等，已存在的不覆盖）
IF NOT EXISTS (SELECT 1 FROM dbo.SysPublicPage)
BEGIN
    INSERT INTO dbo.SysPublicPage (PageKey, Title, Component, Description, Enabled, SortOrder) VALUES
    (N'/lottery-trend',           N'走势图',     N'/src/common/views/PublicLotteryTrendView.vue',    N'彩票走势图公开访问页',     1, 1),
    (N'/lottery-result-summary',  N'开奖结果汇总', N'/src/common/views/LotteryResultSummaryView.vue', N'开奖结果汇总详情页',     1, 2),
    (N'/lottery-analysis',        N'智能分析',   N'/src/common/views/LotteryAnalysisView.vue',        N'彩票智能分析公开访问页', 1, 3);
END
GO

-- 外部页面管理菜单幂等补齐
IF NOT EXISTS (SELECT 1 FROM dbo.SysMenu WHERE Name = N'sys-public-page')
BEGIN
    DECLARE @PublicPageParentId INT = (SELECT TOP 1 Id FROM dbo.SysMenu WHERE Title = N'系统管理');
    IF @PublicPageParentId IS NOT NULL
        INSERT INTO dbo.SysMenu (ParentId, Title, Page, IsFloat, Visible, IsExternal, Editable, Name, Component, SortOrder)
        VALUES (@PublicPageParentId, N'外部页面', N'/sys-public-page', 0, 1, 0, 0, N'sys-public-page', N'/src/common/views/SysPublicPageView.vue', 9);
END
GO

-- admin 角色关联全部菜单（幂等，仅补齐缺失项）
INSERT INTO dbo.SysRoleMenu (RoleId, MenuId)
SELECT r.Id, m.Id
FROM dbo.SysRole r CROSS JOIN dbo.SysMenu m
WHERE r.Code = N'admin'
  AND NOT EXISTS (SELECT 1 FROM dbo.SysRoleMenu rm WHERE rm.RoleId = r.Id AND rm.MenuId = m.Id);
GO

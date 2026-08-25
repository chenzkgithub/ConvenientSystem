/* =============================================================
   ConvenientSystem 本地数据库初始化脚本（唯一维护入口）
   ---------------------------------------------------------------
   - 本文件维护本项目全部数据库对象与初始数据，幂等可重复执行；
   - 后续所有表结构变更、初始数据调整都追加/修改在本文件中；
   - 本机为 SQL Server LocalDB（无完整版实例），执行方式（Windows 认证）：
       sqllocaldb start MSSQLLocalDB
       sqlcmd -S (localdb)\MSSQLLocalDB -E -i db\init.sql -f 65001
     若使用完整版 SQL Server，将 -S 改为 localhost 即可，
     并同步修改 appsettings.json 中 ConnectionStrings:ConvenientSystemDb 的 Server。
   ============================================================= */

-- ========== 建库 ==========
IF DB_ID(N'ConvenientSystem') IS NULL
    CREATE DATABASE [ConvenientSystem];
GO
USE [ConvenientSystem];
GO

-- ========== 创建 root 账号（供 API 连接使用，非 SA） ==========
USE master;
IF NOT EXISTS (SELECT 1 FROM sys.sql_logins WHERE name = N'root')
    CREATE LOGIN root WITH PASSWORD = N'root', CHECK_POLICY = OFF;
GO
USE [ConvenientSystem];
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'root')
    CREATE USER root FOR LOGIN root;
GO
ALTER ROLE db_owner ADD MEMBER root;
GO

-- ========== 注释维护辅助存储过程（幂等） ==========
CREATE OR ALTER PROCEDURE dbo.usp_AddTableComment
    @tableName NVARCHAR(128),
    @comment   NVARCHAR(1000)
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (
        SELECT 1 FROM sys.extended_properties ep
        JOIN sys.objects o ON ep.major_id = o.object_id
        WHERE ep.name = N'MS_Description' AND ep.class = 1 AND ep.minor_id = 0
          AND o.name = @tableName AND SCHEMA_NAME(o.schema_id) = N'dbo'
    )
        EXEC sys.sp_addextendedproperty
            @name = N'MS_Description', @value = @comment,
            @level0type = N'SCHEMA', @level0name = N'dbo',
            @level1type = N'TABLE',  @level1name = @tableName;
    ELSE
        EXEC sys.sp_updateextendedproperty
            @name = N'MS_Description', @value = @comment,
            @level0type = N'SCHEMA', @level0name = N'dbo',
            @level1type = N'TABLE',  @level1name = @tableName;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_AddColumnComment
    @tableName  NVARCHAR(128),
    @columnName NVARCHAR(128),
    @comment    NVARCHAR(1000)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @objectId INT = OBJECT_ID(N'dbo.' + @tableName);
    IF @objectId IS NULL RETURN;
    DECLARE @columnId INT = COLUMNPROPERTY(@objectId, @columnName, 'ColumnId');
    IF @columnId IS NULL RETURN;
    IF NOT EXISTS (
        SELECT 1 FROM sys.extended_properties
        WHERE name = N'MS_Description' AND class = 1
          AND major_id = @objectId AND minor_id = @columnId
    )
        EXEC sys.sp_addextendedproperty
            @name = N'MS_Description', @value = @comment,
            @level0type = N'SCHEMA', @level0name = N'dbo',
            @level1type = N'TABLE',  @level1name = @tableName,
            @level2type = N'COLUMN', @level2name = @columnName;
    ELSE
        EXEC sys.sp_updateextendedproperty
            @name = N'MS_Description', @value = @comment,
            @level0type = N'SCHEMA', @level0name = N'dbo',
            @level1type = N'TABLE',  @level1name = @tableName,
            @level2type = N'COLUMN', @level2name = @columnName;
END
GO

-- ========== 1. 登录用户表 ==========
IF OBJECT_ID(N'dbo.SysUser') IS NULL
BEGIN
    CREATE TABLE dbo.SysUser (
        Id          UNIQUEIDENTIFIER   NOT NULL DEFAULT NEWSEQUENTIALID() PRIMARY KEY,
        Account     NVARCHAR(50)       NOT NULL,
        Password    NVARCHAR(200)      NOT NULL,
        DisplayName NVARCHAR(50)       NULL,
        Avatar      NVARCHAR(MAX)      NULL,
        Phone       NVARCHAR(20)       NULL,
        Email       NVARCHAR(100)      NULL,
        Remark      NVARCHAR(200)      NULL,
        Enabled     BIT                NOT NULL DEFAULT 1,
        IsDeleted   BIT                NOT NULL DEFAULT 0,
        CreateTime  DATETIME           NOT NULL DEFAULT GETDATE(),
        CONSTRAINT UQ_SysUser_Account UNIQUE (Account)
    );
END
GO

EXEC dbo.usp_AddTableComment N'SysUser', N'系统登录用户表';
EXEC dbo.usp_AddColumnComment N'SysUser', N'Id',          N'主键';
EXEC dbo.usp_AddColumnComment N'SysUser', N'Account',     N'登录账号';
EXEC dbo.usp_AddColumnComment N'SysUser', N'Password',    N'登录密码（加密存储）';
EXEC dbo.usp_AddColumnComment N'SysUser', N'DisplayName', N'显示名称';
EXEC dbo.usp_AddColumnComment N'SysUser', N'Avatar',      N'头像（data:image/...;base64 内联图片，前端已压缩）';
EXEC dbo.usp_AddColumnComment N'SysUser', N'Phone',       N'手机号';
EXEC dbo.usp_AddColumnComment N'SysUser', N'Email',       N'邮箱';
EXEC dbo.usp_AddColumnComment N'SysUser', N'Remark',      N'备注/个人简介';
EXEC dbo.usp_AddColumnComment N'SysUser', N'Enabled',     N'是否启用';
EXEC dbo.usp_AddColumnComment N'SysUser', N'CreateTime',  N'创建时间';
GO

-- 初始账号（密码 admin 的 PBKDF2 哈希，避免明文存储；首版可用 PasswordHasher.Verify 校验）
IF NOT EXISTS (SELECT 1 FROM dbo.SysUser)
    INSERT INTO dbo.SysUser (Account, Password, DisplayName) VALUES (N'admin', N'pbkdf2$100000$weNyoN62VQm4jyrAnmBTIQ==$bWOhLMXFgid8uBAXdFiCopECopJiwrRqpkTNDE/9mYA=', N'管理员');
GO

-- ========== 2. SQL 查询工具数据源表（原 datasources.xml） ==========
IF OBJECT_ID(N'dbo.SysDataSource') IS NULL
BEGIN
    CREATE TABLE dbo.SysDataSource (
        Id               INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Name             NVARCHAR(100)     NOT NULL,
        ConnectionString NVARCHAR(1000)    NOT NULL,
        DbType           NVARCHAR(20)      NOT NULL DEFAULT N'sqlserver',
        CreateTime       DATETIME          NOT NULL DEFAULT GETDATE(),
        CreatedById      UNIQUEIDENTIFIER  NULL,
        CONSTRAINT UQ_SysDataSource_Name_CreatedById UNIQUE (Name, CreatedById)
    );
END
GO

EXEC dbo.usp_AddTableComment N'SysDataSource', N'SQL 查询数据源配置表';
EXEC dbo.usp_AddColumnComment N'SysDataSource', N'Id',               N'主键';
EXEC dbo.usp_AddColumnComment N'SysDataSource', N'Name',             N'数据源显示名称';
EXEC dbo.usp_AddColumnComment N'SysDataSource', N'ConnectionString', N'数据库连接字符串';
EXEC dbo.usp_AddColumnComment N'SysDataSource', N'DbType',           N'数据库类型（sqlserver/mysql/postgresql/oracle/sqlite/clickhouse）';
EXEC dbo.usp_AddColumnComment N'SysDataSource', N'CreateTime',       N'创建时间';
EXEC dbo.usp_AddColumnComment N'SysDataSource', N'CreatedById',      N'创建人用户 Id（GUID，关联 SysUser.Id，列表关联展示账号与姓名）';
GO

-- 初始数据源（迁移自 datasources.xml；按名称逐条补齐，已存在的不覆盖，可重复执行；创建人关联内置管理员 admin）
-- 注意：ConvenientSystemDb 为程序内置数据源（连接串取自 appsettings 的 ConvenientSystemDb，指向 master 库），不落库
INSERT INTO dbo.SysDataSource (Name, ConnectionString, DbType, CreatedById)
SELECT s.Name, s.ConnectionString, s.DbType, u.Id
FROM (VALUES
    (N'YhSystemDb',           N'server = 192.168.16.7; user id = sa; password =yh!@#2021.com; database = yh_system;MultipleActiveResultSets=True;Encrypt=False;TrustServerCertificate=False;pooling=true;min pool size=5;max pool size=512;Connection Timeout=30;', N'sqlserver'),
    (N'192.168.16.214',       N'server = 192.168.16.214; user id = erpsystem; password =YunHanERP@996.com; database = master', N'sqlserver'),
    (N'clickh_order_base_v6', N'Compress=False;BufferSize=32768;SocketTimeout=10000;CheckCompressedHash=False;Encrypt=False;Compressor=lz4;Host=192.168.16.76;Database=clickh_order_base;Username=yunhanmy;Password=123.com', N'clickhouse')
) AS s(Name, ConnectionString, DbType)
CROSS JOIN (SELECT TOP 1 Id FROM dbo.SysUser WHERE Account = N'admin') u
WHERE NOT EXISTS (SELECT 1 FROM dbo.SysDataSource d WHERE d.Name = s.Name);
GO

-- ========== 3. 菜单表（原 menus.xml） ==========
IF OBJECT_ID(N'dbo.SysMenu') IS NULL
BEGIN
    CREATE TABLE dbo.SysMenu (
        Id         INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        ParentId   INT               NULL,
        Title      NVARCHAR(100)     NOT NULL,
        Page       NVARCHAR(1000)    NULL,
        IsFloat    BIT               NOT NULL DEFAULT 0,
        Visible    BIT               NOT NULL DEFAULT 1,
        IsExternal BIT               NOT NULL DEFAULT 0,
        Editable   BIT               NOT NULL DEFAULT 1,
        Enabled    BIT               NOT NULL DEFAULT 1,
        Name       NVARCHAR(100)     NULL,
        Component  NVARCHAR(200)     NULL,
        SortOrder  INT               NOT NULL DEFAULT 0,
        Type       TINYINT           NOT NULL DEFAULT 1
    );
END
GO

EXEC dbo.usp_AddTableComment N'SysMenu', N'系统菜单配置表';
EXEC dbo.usp_AddColumnComment N'SysMenu', N'Id',         N'主键';
EXEC dbo.usp_AddColumnComment N'SysMenu', N'ParentId',   N'父菜单 Id，NULL 为顶层分组';
EXEC dbo.usp_AddColumnComment N'SysMenu', N'Title',      N'菜单标题';
EXEC dbo.usp_AddColumnComment N'SysMenu', N'Page',       N'末级菜单链接/内部路由；分组菜单为 NULL';
EXEC dbo.usp_AddColumnComment N'SysMenu', N'IsFloat',    N'是否在悬浮按钮菜单显示';
EXEC dbo.usp_AddColumnComment N'SysMenu', N'Visible',    N'是否在侧栏/首页显示';
EXEC dbo.usp_AddColumnComment N'SysMenu', N'IsExternal', N'是否外部链接';
EXEC dbo.usp_AddColumnComment N'SysMenu', N'Editable',   N'是否允许在菜单管理中编辑';
EXEC dbo.usp_AddColumnComment N'SysMenu', N'Enabled',    N'是否启用（停用后不在侧栏/首页显示，也不可在权限管理中分配）';
EXEC dbo.usp_AddColumnComment N'SysMenu', N'Name',       N'内部路由名称';
EXEC dbo.usp_AddColumnComment N'SysMenu', N'Component',  N'内部路由 Vue 组件路径';
EXEC dbo.usp_AddColumnComment N'SysMenu', N'SortOrder',  N'同级排序号';
EXEC dbo.usp_AddColumnComment N'SysMenu', N'Type',       N'节点类型：0=Group 1=Page 2=Button';
GO

-- 初始菜单（从本地数据库导出，保持与线上完全一致）
IF NOT EXISTS (SELECT 1 FROM dbo.SysMenu)
BEGIN
    SET IDENTITY_INSERT dbo.SysMenu ON;

    INSERT INTO dbo.SysMenu (Id, ParentId, Title, Page, IsFloat, Visible, IsExternal, Editable, Enabled, Name, Component, SortOrder, Type) VALUES
    -- 系统管理
    ( 1, NULL, N'系统管理', NULL, 0, 1, 0, 0, 1, NULL, NULL, 1, 0),
    ( 2, 1, N'系统配置', N'/sys-config', 0, 1, 0, 0, 1, N'sys-config', N'/src/common/views/SysConfigView.vue', 1, 1),
    ( 3, 1, N'个人配置', N'/personal-config', 0, 1, 0, 0, 1, N'personal-config', N'/src/common/views/PersonalConfigView.vue', 2, 1),
    ( 4, 1, N'视图管理', N'/view-manage', 0, 1, 0, 0, 1, N'view-manage', N'/src/common/views/ViewManageView.vue', 3, 1),
    ( 5, 1, N'菜单管理', N'/menu-manage', 0, 1, 0, 0, 1, N'menu-manage', N'/src/common/views/MenuManageView.vue', 4, 1),
    ( 6, 1, N'用户管理', N'/user-manage', 0, 1, 0, 0, 1, N'user-manage', N'/src/common/views/UserManageView.vue', 5, 1),
    ( 7, 1, N'角色管理', N'/role-manage', 0, 1, 0, 0, 1, N'role-manage', N'/src/common/views/RoleManageView.vue', 6, 1),
    ( 8, 1, N'权限设置', N'/permission', 0, 1, 0, 0, 1, N'permission', N'/src/common/views/PermissionView.vue', 7, 1),
    ( 9, 1, N'在线用户', N'/online-users', 0, 1, 0, 0, 1, N'online-users', N'/src/common/views/UserOnlineView.vue', 8, 1),
    (10, 1, N'外部页面', N'/sys-public-page', 0, 1, 0, 0, 1, N'sys-public-page', N'/src/common/views/SysPublicPageView.vue', 9, 1),
    (11, 1, N'通知管理', N'/notice', 0, 1, 0, 0, 1, N'notice', N'/src/common/views/NoticeManageView.vue', 10, 1),
    (12, 1, N'系统大盘', N'/system-dashboard', 0, 1, 0, 1, 1, N'system-dashboard', N'/src/common/views/SystemDashboardView.vue', 11, 1),
    (13, 1, N'定时任务', N'/hangfire-jobs', 0, 1, 0, 1, 1, N'hangfire-jobs', N'/src/common/views/HangfireJobsView.vue', 12, 1),
    -- 日志
    (14, NULL, N'日志', NULL, 0, 1, 0, 1, 1, N'log-group', NULL, 2, 1),
    (15, 14, N'审计日志', N'/audit-log', 0, 1, 0, 0, 1, N'audit-log', N'/src/common/views/AuditLogView.vue', 1, 1),
    (16, 14, N'错误日志', N'/error-log', 0, 1, 0, 0, 1, N'error-log', N'/src/common/views/ErrorLogView.vue', 2, 1),
    (17, 14, N'实时日志', N'/log-viewer', 0, 1, 0, 1, 1, N'log-viewer', N'/src/common/views/LogViewerView.vue', 3, 1),
    -- 昀晗
    (18, NULL, N'昀晗', NULL, 0, 1, 0, 1, 1, NULL, NULL, 3, 0),
    (19, 18, N'考勤查询', N'/attendance', 1, 1, 0, 1, 1, N'attendance', N'/src/yunhan/views/AttendanceView.vue', 1, 1),
    (20, 18, N'项目管理', N'http://192.168.16.240:7003/yhproj/sheetIndex', 1, 1, 1, 1, 1, NULL, NULL, 3, 1),
    (21, 18, N'ERP系统', NULL, 0, 1, 0, 1, 1, NULL, NULL, 3, 0),
    (22, 21, N'测试-ERP系统', N'http://192.168.16.240:8000/', 1, 1, 1, 1, 1, NULL, NULL, 1, 1),
    (23, 21, N'正式-ERP系统', N'http://192.168.16.240:8001/', 1, 1, 1, 1, 1, NULL, NULL, 2, 1),
    -- 开发工具
    (24, NULL, N'开发工具', NULL, 0, 1, 0, 1, 1, NULL, NULL, 4, 0),
    (25, 24, N'代码编辑器', N'/code-editor', 1, 1, 0, 1, 1, N'code-editor', N'/src/common/views/CodeEditorView.vue', 1, 1),
    (26, 24, N'开发工具集', N'/dev-tools', 1, 1, 0, 1, 1, N'dev-tools', N'/src/common/views/DevToolsView.vue', 2, 1),
    (27, 24, N'SQL查询', N'/sql-query', 1, 1, 0, 1, 1, N'sql-query', N'/src/common/views/SqlQueryView.vue', 3, 1),
    (28, 24, N'命名转换', N'/code-naming', 1, 1, 0, 1, 1, N'code-naming', N'/src/common/views/CodeNamingView.vue', 4, 1),
    -- 构建发布（一级菜单）
    (61, NULL, N'构建发布', NULL, 0, 1, 0, 1, 1, NULL, NULL, 5, 0),
    (59, 61, N'Web版本管理', N'/web-package', 0, 1, 0, 0, 1, N'web-package', N'/src/common/views/WebPackageView.vue', 1, 1),
    (62, 61, N'构建与发布', N'/build-manager', 1, 1, 0, 1, 1, N'build-manager', N'/src/common/views/BuildManagerView.vue', 2, 1),
    (60, 61, N'通用构建发布', N'/universal-build', 1, 1, 0, 1, 1, N'universal-build', N'/src/common/views/UniversalBuildView.vue', 3, 1),
    -- 常用工具（外链）
    (29, NULL, N'常用工具', NULL, 0, 1, 0, 1, 1, NULL, NULL, 6, 0),
    (30, 29, N'有道词典', N'https://note.youdao.com/web/#/file/WEBf6433cf7e1e375c6ce4268cefeff88ea/note/WEBa6c175a7e3dbc9e5540d70f3316691e2/', 1, 1, 1, 1, 1, NULL, NULL, 1, 1),
    (31, 29, N'云效', N'https://devops.aliyun.com/workbench', 1, 1, 1, 1, 1, NULL, NULL, 2, 1),
    (32, 29, N'百度翻译', N'https://fanyi.baidu.com/mtpe-individual/transText?ext_channel=Aldtype01&from=auto&to=zh&query=#/', 1, 1, 1, 1, 1, NULL, NULL, 3, 1),
    (33, 29, N'deepseek', N'https://chat.deepseek.com/a/chat/s/e0d4cb7c-ae35-47f9-81b7-7ec67d905ffd', 1, 1, 1, 1, 1, NULL, NULL, 4, 1),
    (34, 29, N'豆包', N'https://www.doubao.com/chat/38435646902862338?channel=dbweb_sem_pinz_pinp_zongh_tongy_tongy_ocpc_faxx_9', 1, 1, 1, 1, 1, NULL, NULL, 5, 1),
    (35, 29, N'json格式化', N'https://www.sojson.com/', 1, 1, 1, 1, 1, NULL, NULL, 6, 1),
    -- 国家公益事业
    (36, NULL, N'国家公益事业', NULL, 0, 1, 0, 1, 1, NULL, NULL, 7, 0),
    (37, 36, N'双色球', N'/lottery?type=SSQ', 1, 1, 0, 1, 1, N'lottery-ssq', N'/src/common/views/LotteryView.vue', 1, 1),
    (38, 36, N'大乐透', N'/lottery', 1, 1, 0, 1, 1, N'lottery', N'/src/common/views/LotteryView.vue', 2, 1),
    (39, 36, N'排列五', N'/lottery?type=PL5', 1, 1, 0, 1, 1, N'lottery-pl5', N'/src/common/views/LotteryView.vue', 3, 1),
    (40, 36, N'福彩3D', N'/lottery?type=FC3D', 1, 1, 0, 1, 1, N'lottery-fc3d', N'/src/common/views/LotteryView.vue', 4, 1),
    (41, 36, N'选号记录', N'/lottery-records', 0, 1, 0, 1, 1, N'lottery-records', N'/src/common/views/LotteryRecordsView.vue', 5, 1),
    (42, 36, N'智能分析', N'/lottery-analysis', 0, 1, 0, 1, 1, N'lottery-analysis', N'/src/common/views/LotteryAnalysisView.vue', 6, 1),
    -- 短信管理
    (43, NULL, N'短信管理', NULL, 0, 1, 0, 1, 1, NULL, NULL, 8, 0),
    (44, 43, N'模板管理', N'/sms-template', 0, 1, 0, 1, 1, N'sms-template', N'/src/sms/views/SmsTemplateView.vue', 1, 1),
    (45, 43, N'发送日志', N'/sms-log', 0, 1, 0, 1, 1, N'sms-log', N'/src/sms/views/SmsLogView.vue', 2, 1),
    (46, 43, N'系统配置', N'/sms-config', 0, 1, 0, 1, 1, N'sms-config', N'/src/sms/views/SmsConfigView.vue', 3, 1),
    -- 邮件通知
    (47, NULL, N'邮件通知', NULL, 0, 1, 0, 1, 1, NULL, NULL, 9, 0),
    (48, 47, N'邮件配置', N'/email-config', 0, 1, 0, 1, 1, N'email-config', N'/src/email/views/EmailConfigView.vue', 1, 1),
    (49, 47, N'发送日志', N'/email-log', 0, 1, 0, 1, 1, N'email-log', N'/src/email/views/EmailLogView.vue', 2, 1),
    -- 机器人
    (50, NULL, N'机器人', NULL, 0, 1, 0, 1, 1, NULL, NULL, 10, 0),
    (51, 50, N'群机器人', N'/webhook-config', 0, 1, 0, 0, 1, N'webhook-config', N'/src/notify/views/WebhookConfigView.vue', 1, 1),
    (52, 50, N'发送日志', N'/webhook-log', 0, 1, 0, 1, 1, N'webhook-log', N'/src/notify/views/WebhookLogView.vue', 2, 1),
    -- 任务调度（独立浮窗）
    (53, NULL, N'任务调度', N'/hangfire', 1, 1, 0, 0, 1, N'hangfire', N'/src/common/views/HangfireView.vue', 11, 1),
    -- 监控
    (54, NULL, N'监控', NULL, 0, 1, 0, 1, 1, NULL, NULL, 12, 0),
    (55, 54, N'网站监控', N'/web-monitor', 0, 1, 0, 1, 1, N'web-monitor', N'/src/common/views/WebMonitorView.vue', 1, 1),
    (56, 54, N'本机监控', N'/local-monitor', 1, 1, 0, 1, 1, N'local-monitor', N'/src/common/views/LocalMonitorView.vue', 2, 1),
    -- 知识库
    (57, NULL, N'知识库', NULL, 0, 1, 0, 1, 1, NULL, NULL, 13, 0),
    (58, 57, N'Python', N'/python-knowledge', 0, 1, 0, 1, 1, N'python-knowledge', N'/src/common/views/PythonKnowledgeView.vue', 1, 1),
    -- Web版本管理、构建与发布、通用构建发布已移至构建发布一级菜单下

    SET IDENTITY_INSERT dbo.SysMenu OFF;
END
GO

-- ========== 3.5 Web 前端版本包表 ==========
IF OBJECT_ID(N'dbo.WebPackage') IS NULL
BEGIN
    CREATE TABLE dbo.WebPackage (
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

EXEC dbo.usp_AddTableComment N'WebPackage', N'Web 前端版本包（桌面客户端更新用）';
EXEC dbo.usp_AddColumnComment N'WebPackage', N'Id',          N'主键';
EXEC dbo.usp_AddColumnComment N'WebPackage', N'Version',     N'版本号（如 1.0.0）';
EXEC dbo.usp_AddColumnComment N'WebPackage', N'FileName',    N'存储文件名';
EXEC dbo.usp_AddColumnComment N'WebPackage', N'FileSize',    N'文件大小（字节）';
EXEC dbo.usp_AddColumnComment N'WebPackage', N'Description', N'更新说明';
EXEC dbo.usp_AddColumnComment N'WebPackage', N'IsActive',    N'是否当前激活版本（桌面端下载此版本）';
EXEC dbo.usp_AddColumnComment N'WebPackage', N'CreateTime',  N'上传时间';
EXEC dbo.usp_AddColumnComment N'WebPackage', N'CreatedById', N'上传人用户 Id（GUID，关联 SysUser.Id）';
GO

-- ========== 3.6 桌面安装包版本表 ==========
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

EXEC dbo.usp_AddTableComment N'DesktopPackage', N'桌面安装包版本（桌面客户端自更新用）';
EXEC dbo.usp_AddColumnComment N'DesktopPackage', N'Id',          N'主键';
EXEC dbo.usp_AddColumnComment N'DesktopPackage', N'Version',     N'版本号（如 1.0.0）';
EXEC dbo.usp_AddColumnComment N'DesktopPackage', N'FileName',    N'存储文件名';
EXEC dbo.usp_AddColumnComment N'DesktopPackage', N'FileSize',    N'文件大小（字节）';
EXEC dbo.usp_AddColumnComment N'DesktopPackage', N'Description', N'更新说明';
EXEC dbo.usp_AddColumnComment N'DesktopPackage', N'IsActive',    N'是否当前激活版本（桌面端下载此版本）';
EXEC dbo.usp_AddColumnComment N'DesktopPackage', N'CreateTime',  N'上传时间';
EXEC dbo.usp_AddColumnComment N'DesktopPackage', N'CreatedById', N'上传人用户 Id（GUID，关联 SysUser.Id）';
GO

-- ========== 4. SQL 快捷输入表 ==========
IF OBJECT_ID(N'dbo.SysSqlSnippet') IS NULL
BEGIN
    CREATE TABLE dbo.SysSqlSnippet (
        Id         INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Shortcut   NVARCHAR(50)      NOT NULL,
        Expansion  NVARCHAR(2000)    NOT NULL,
        Remark     NVARCHAR(200)     NULL,
        SortOrder  INT               NOT NULL DEFAULT 0,
        CreateTime DATETIME          NOT NULL DEFAULT GETDATE(),
        CreatedById UNIQUEIDENTIFIER NULL,
        CONSTRAINT UQ_SysSqlSnippet_Shortcut UNIQUE (Shortcut)
    );
END
GO

EXEC dbo.usp_AddTableComment N'SysSqlSnippet', N'SQL 快捷输入配置表';
EXEC dbo.usp_AddColumnComment N'SysSqlSnippet', N'Id',        N'主键';
EXEC dbo.usp_AddColumnComment N'SysSqlSnippet', N'Shortcut',  N'快捷输入缩写（如 sf）';
EXEC dbo.usp_AddColumnComment N'SysSqlSnippet', N'Expansion', N'展开内容（如 SELECT * FROM ）';
EXEC dbo.usp_AddColumnComment N'SysSqlSnippet', N'Remark',    N'备注说明';
EXEC dbo.usp_AddColumnComment N'SysSqlSnippet', N'SortOrder', N'排序号';
EXEC dbo.usp_AddColumnComment N'SysSqlSnippet', N'CreateTime',N'创建时间';
EXEC dbo.usp_AddColumnComment N'SysSqlSnippet', N'CreatedById', N'创建人用户 Id（GUID，关联 SysUser.Id）';
GO

-- 初始快捷输入示例（创建人关联内置管理员 admin）
INSERT INTO dbo.SysSqlSnippet (Shortcut, Expansion, Remark, SortOrder, CreatedById)
SELECT s.Shortcut, s.Expansion, s.Remark, s.SortOrder, u.Id
FROM (VALUES
    (N'sf',  N'SELECT * FROM ',   N'查询全部字段', 1),
    (N'sc',  N'SELECT COUNT(*) FROM ', N'查询总数', 2),
    (N'st',  N'SELECT TOP 100 * FROM ', N'查询前100条', 3),
    (N'wh',  N'WHERE 1=1',        N'条件子句', 4),
    (N'ob',  N'ORDER BY ',        N'排序', 5),
    (N'gb',  N'GROUP BY ',        N'分组', 6),
    (N'ij',  N'INNER JOIN ',      N'内连接', 7),
    (N'lj',  N'LEFT JOIN ',       N'左连接', 8)
) AS s(Shortcut, Expansion, Remark, SortOrder)
CROSS JOIN (SELECT TOP 1 Id FROM dbo.SysUser WHERE Account = N'admin') u
WHERE NOT EXISTS (SELECT 1 FROM dbo.SysSqlSnippet d WHERE d.Shortcut = s.Shortcut);
GO

-- SQL 查询收藏表
IF OBJECT_ID(N'dbo.SysSqlFavorite') IS NULL
BEGIN
    CREATE TABLE dbo.SysSqlFavorite (
        Id         INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Name       NVARCHAR(100)     NOT NULL,
        SqlContent NVARCHAR(MAX)     NOT NULL,
        Remark     NVARCHAR(500)     NULL,
        DataSource NVARCHAR(100)     NULL,
        SortOrder  INT               NOT NULL DEFAULT 0,
        CreateTime DATETIME          NOT NULL DEFAULT GETDATE(),
        CreatedById UNIQUEIDENTIFIER NULL
    );
END
GO

EXEC dbo.usp_AddTableComment N'SysSqlFavorite', N'SQL 查询收藏表';
EXEC dbo.usp_AddColumnComment N'SysSqlFavorite', N'Id',         N'主键';
EXEC dbo.usp_AddColumnComment N'SysSqlFavorite', N'Name',       N'收藏名称';
EXEC dbo.usp_AddColumnComment N'SysSqlFavorite', N'SqlContent', N'SQL 内容';
EXEC dbo.usp_AddColumnComment N'SysSqlFavorite', N'Remark',     N'备注说明';
EXEC dbo.usp_AddColumnComment N'SysSqlFavorite', N'DataSource', N'绑定的数据源名称';
EXEC dbo.usp_AddColumnComment N'SysSqlFavorite', N'SortOrder',  N'排序号';
EXEC dbo.usp_AddColumnComment N'SysSqlFavorite', N'CreateTime', N'创建时间';
EXEC dbo.usp_AddColumnComment N'SysSqlFavorite', N'CreatedById', N'创建人用户 Id（GUID）';
GO

-- ========== 短信管理模块 ==========

-- 1. 短信模板表
IF OBJECT_ID(N'dbo.SmsTemplate') IS NULL
BEGIN
    CREATE TABLE dbo.SmsTemplate (
        Id          INT IDENTITY(1,1)  NOT NULL PRIMARY KEY,
        Name        NVARCHAR(100)      NOT NULL,
        Content     NVARCHAR(500)      NOT NULL,
        Signature   NVARCHAR(50)       NOT NULL DEFAULT N'zk',
        Category    NVARCHAR(50)       NOT NULL DEFAULT N'通知',
        Enabled     BIT                NOT NULL DEFAULT 1,
        CreateTime  DATETIME           NOT NULL DEFAULT GETDATE(),
        UpdateTime  DATETIME           NOT NULL DEFAULT GETDATE(),
        CreatedById UNIQUEIDENTIFIER   NULL
    );
END
GO

EXEC dbo.usp_AddTableComment N'SmsTemplate', N'短信模板表';
EXEC dbo.usp_AddColumnComment N'SmsTemplate', N'Id',        N'主键';
EXEC dbo.usp_AddColumnComment N'SmsTemplate', N'Name',      N'模板名称';
EXEC dbo.usp_AddColumnComment N'SmsTemplate', N'Content',   N'模板内容（支持 {姓名} {公司} 变量）';
EXEC dbo.usp_AddColumnComment N'SmsTemplate', N'Signature', N'短信签名';
EXEC dbo.usp_AddColumnComment N'SmsTemplate', N'Category',  N'分类：营销/通知/提醒';
EXEC dbo.usp_AddColumnComment N'SmsTemplate', N'Enabled',   N'是否启用';
EXEC dbo.usp_AddColumnComment N'SmsTemplate', N'CreateTime',N'创建时间';
EXEC dbo.usp_AddColumnComment N'SmsTemplate', N'UpdateTime',N'更新时间';
EXEC dbo.usp_AddColumnComment N'SmsTemplate', N'CreatedById', N'创建人用户 Id（GUID，关联 SysUser.Id，列表关联展示账号与姓名）';
GO

-- 2. 短信任务表
IF OBJECT_ID(N'dbo.SmsTask') IS NULL
BEGIN
    CREATE TABLE dbo.SmsTask (
        Id              INT IDENTITY(1,1)  NOT NULL PRIMARY KEY,
        Name            NVARCHAR(100)      NOT NULL,
        TemplateId      INT                NOT NULL,
        SendTime        DATETIME           NOT NULL,
        HangfireJobId   NVARCHAR(100)      NULL,
        Status          TINYINT            NOT NULL DEFAULT 0,
        TotalCount      INT                NOT NULL DEFAULT 0,
        SuccessCount    INT                NOT NULL DEFAULT 0,
        FailCount       INT                NOT NULL DEFAULT 0,
        CreatedById     UNIQUEIDENTIFIER   NULL,
        CreateTime      DATETIME           NOT NULL DEFAULT GETDATE(),
        UpdateTime      DATETIME           NOT NULL DEFAULT GETDATE()
    );
END
GO

EXEC dbo.usp_AddTableComment N'SmsTask', N'短信发送任务表';
EXEC dbo.usp_AddColumnComment N'SmsTask', N'Id',            N'主键';
EXEC dbo.usp_AddColumnComment N'SmsTask', N'Name',          N'任务名称';
EXEC dbo.usp_AddColumnComment N'SmsTask', N'TemplateId',    N'关联短信模板 Id';
EXEC dbo.usp_AddColumnComment N'SmsTask', N'SendTime',      N'计划发送时间';
EXEC dbo.usp_AddColumnComment N'SmsTask', N'HangfireJobId', N'Hangfire Job ID（用于取消）';
EXEC dbo.usp_AddColumnComment N'SmsTask', N'Status',        N'任务状态：0=待执行 1=执行中 2=已完成 3=已取消 4=失败';
EXEC dbo.usp_AddColumnComment N'SmsTask', N'TotalCount',    N'总收件人数';
EXEC dbo.usp_AddColumnComment N'SmsTask', N'SuccessCount',  N'成功数';
EXEC dbo.usp_AddColumnComment N'SmsTask', N'FailCount',     N'失败数';
EXEC dbo.usp_AddColumnComment N'SmsTask', N'CreatedById',   N'创建人用户 Id（GUID，关联 SysUser.Id，列表关联展示账号与姓名）';
EXEC dbo.usp_AddColumnComment N'SmsTask', N'CreateTime',    N'创建时间';
EXEC dbo.usp_AddColumnComment N'SmsTask', N'UpdateTime',    N'更新时间';
GO

-- 3. 短信收件人表
IF OBJECT_ID(N'dbo.SmsRecipient') IS NULL
BEGIN
    CREATE TABLE dbo.SmsRecipient (
        Id              INT IDENTITY(1,1)  NOT NULL PRIMARY KEY,
        TaskId          INT                NOT NULL,
        Phone           NVARCHAR(20)       NOT NULL,
        Name            NVARCHAR(50)       NOT NULL DEFAULT N'',
        Status          TINYINT            NOT NULL DEFAULT 0,
        ErrorMessage    NVARCHAR(500)      NULL,
        SentTime        DATETIME           NULL
    );
END
GO

EXEC dbo.usp_AddTableComment N'SmsRecipient', N'短信任务收件人表';
EXEC dbo.usp_AddColumnComment N'SmsRecipient', N'Id',          N'主键';
EXEC dbo.usp_AddColumnComment N'SmsRecipient', N'TaskId',      N'关联任务 Id';
EXEC dbo.usp_AddColumnComment N'SmsRecipient', N'Phone',       N'手机号';
EXEC dbo.usp_AddColumnComment N'SmsRecipient', N'Name',        N'姓名（用于模板变量替换）';
EXEC dbo.usp_AddColumnComment N'SmsRecipient', N'Status',      N'发送状态：0=待发送 1=成功 2=失败';
EXEC dbo.usp_AddColumnComment N'SmsRecipient', N'ErrorMessage',N'错误信息';
EXEC dbo.usp_AddColumnComment N'SmsRecipient', N'SentTime',    N'实际发送时间';
GO

-- 4. 短信发送日志表
IF OBJECT_ID(N'dbo.SmsLog') IS NULL
BEGIN
    CREATE TABLE dbo.SmsLog (
        Id              BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        TaskId          INT                NOT NULL,
        RecipientId     INT                NOT NULL,
        Phone           NVARCHAR(20)       NOT NULL,
        Content         NVARCHAR(500)      NOT NULL,
        ProviderMsgId   NVARCHAR(100)      NULL,
        Status          TINYINT            NOT NULL DEFAULT 0,
        ErrorMessage    NVARCHAR(500)      NULL,
        CostMs          INT                NOT NULL DEFAULT 0,
        CreateTime      DATETIME           NOT NULL DEFAULT GETDATE()
    );
    CREATE INDEX IX_SmsLog_CreateTime ON dbo.SmsLog(CreateTime);
    CREATE INDEX IX_SmsLog_Phone ON dbo.SmsLog(Phone);
END
GO

EXEC dbo.usp_AddTableComment N'SmsLog', N'短信发送日志表';
EXEC dbo.usp_AddColumnComment N'SmsLog', N'Id',           N'主键';
EXEC dbo.usp_AddColumnComment N'SmsLog', N'TaskId',       N'关联任务 Id';
EXEC dbo.usp_AddColumnComment N'SmsLog', N'RecipientId',  N'关联收件人 Id';
EXEC dbo.usp_AddColumnComment N'SmsLog', N'Phone',        N'手机号';
EXEC dbo.usp_AddColumnComment N'SmsLog', N'Content',      N'实际发送内容（变量已替换）';
EXEC dbo.usp_AddColumnComment N'SmsLog', N'ProviderMsgId',N'服务商消息 Id（阿里云 RequestId）';
EXEC dbo.usp_AddColumnComment N'SmsLog', N'Status',       N'发送状态：0=失败 1=成功';
EXEC dbo.usp_AddColumnComment N'SmsLog', N'ErrorMessage', N'错误信息';
EXEC dbo.usp_AddColumnComment N'SmsLog', N'CostMs',       N'耗时毫秒';
EXEC dbo.usp_AddColumnComment N'SmsLog', N'CreateTime',   N'创建时间';
GO

-- 5. 短信服务商配置表（列表化，支持多条配置）
IF OBJECT_ID(N'dbo.SmsProviderConfig') IS NULL
BEGIN
    CREATE TABLE dbo.SmsProviderConfig (
        Id                  INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Name                NVARCHAR(100)      NOT NULL DEFAULT N'',
        ProviderType        NVARCHAR(20)       NOT NULL DEFAULT N'aliyun',
        AccessKeyId         NVARCHAR(200)      NOT NULL,
        AccessKeySecret     NVARCHAR(500)      NOT NULL,
        DefaultSignature    NVARCHAR(50)       NOT NULL DEFAULT N'zk',
        TemplateCode        NVARCHAR(100)      NOT NULL DEFAULT N'',
        TemplateId          INT                NULL,
        IsDefault           BIT                NOT NULL DEFAULT 0,
        Enabled             BIT                NOT NULL DEFAULT 1,
        CreateTime          DATETIME           NOT NULL DEFAULT GETDATE(),
        UpdateTime          DATETIME           NOT NULL DEFAULT GETDATE()
    );
END
GO

EXEC dbo.usp_AddTableComment N'SmsProviderConfig', N'短信服务商配置表';
EXEC dbo.usp_AddColumnComment N'SmsProviderConfig', N'Id',               N'主键';
EXEC dbo.usp_AddColumnComment N'SmsProviderConfig', N'Name',             N'配置名称';
EXEC dbo.usp_AddColumnComment N'SmsProviderConfig', N'ProviderType',     N'短信服务商类型：aliyun / ihuyi';
EXEC dbo.usp_AddColumnComment N'SmsProviderConfig', N'AccessKeyId',      N'AccessKey（AES 加密存储）';
EXEC dbo.usp_AddColumnComment N'SmsProviderConfig', N'AccessKeySecret',  N'AccessKeySecret（AES 加密存储）';
EXEC dbo.usp_AddColumnComment N'SmsProviderConfig', N'DefaultSignature', N'默认短信签名';
EXEC dbo.usp_AddColumnComment N'SmsProviderConfig', N'TemplateCode',     N'阿里云模板 Code';
EXEC dbo.usp_AddColumnComment N'SmsProviderConfig', N'TemplateId',        N'关联短信模板 Id（用于测试发送）';
EXEC dbo.usp_AddColumnComment N'SmsProviderConfig', N'IsDefault',        N'是否默认配置';
EXEC dbo.usp_AddColumnComment N'SmsProviderConfig', N'Enabled',          N'是否启用';
EXEC dbo.usp_AddColumnComment N'SmsProviderConfig', N'CreateTime',       N'创建时间';
EXEC dbo.usp_AddColumnComment N'SmsProviderConfig', N'UpdateTime',       N'更新时间';
GO

-- 6. 短信配额表（Daily / Monthly）
IF OBJECT_ID(N'dbo.SmsQuota') IS NULL
BEGIN
    CREATE TABLE dbo.SmsQuota (
        Id          INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        QuotaType   NVARCHAR(20)      NOT NULL,
        MaxCount    INT               NOT NULL DEFAULT 100,
        UpdateTime  DATETIME          NOT NULL DEFAULT GETDATE(),
        CONSTRAINT UQ_SmsQuota_Type UNIQUE(QuotaType)
    );
    -- 默认配额：每日 100 条
    IF NOT EXISTS (SELECT 1 FROM dbo.SmsQuota WHERE QuotaType = 'Daily')
        INSERT INTO dbo.SmsQuota (QuotaType, MaxCount) VALUES ('Daily', 100);
    IF NOT EXISTS (SELECT 1 FROM dbo.SmsQuota WHERE QuotaType = 'Monthly')
        INSERT INTO dbo.SmsQuota (QuotaType, MaxCount) VALUES ('Monthly', 3000);
END
GO

EXEC dbo.usp_AddTableComment N'SmsQuota', N'短信发送配额表';
EXEC dbo.usp_AddColumnComment N'SmsQuota', N'Id',        N'主键';
EXEC dbo.usp_AddColumnComment N'SmsQuota', N'QuotaType', N'配额类型：Daily / Monthly';
EXEC dbo.usp_AddColumnComment N'SmsQuota', N'MaxCount',  N'最大发送条数';
EXEC dbo.usp_AddColumnComment N'SmsQuota', N'UpdateTime',N'更新时间';
GO

-- ========== 邮件通知模块 ==========

-- 1. 邮件 SMTP 配置表（列表化，支持多条配置）
IF OBJECT_ID(N'dbo.EmailConfig') IS NULL
BEGIN
    CREATE TABLE dbo.EmailConfig (
        Id          INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Name        NVARCHAR(100)      NOT NULL DEFAULT N'',
        SmtpServer  NVARCHAR(200)      NOT NULL,
        SmtpPort    INT                NOT NULL DEFAULT 587,
        Account     NVARCHAR(200)      NOT NULL,
        Password    NVARCHAR(500)      NOT NULL,
        FromName    NVARCHAR(100)      NOT NULL DEFAULT N'系统通知',
        EnableSsl   BIT                NOT NULL DEFAULT 1,
        IsDefault   BIT                NOT NULL DEFAULT 0,
        Enabled     BIT                NOT NULL DEFAULT 1,
        CreateTime  DATETIME           NOT NULL DEFAULT GETDATE(),
        UpdateTime  DATETIME           NOT NULL DEFAULT GETDATE()
    );
END
GO

EXEC dbo.usp_AddTableComment N'EmailConfig', N'邮件 SMTP 配置表';
EXEC dbo.usp_AddColumnComment N'EmailConfig', N'Id',         N'主键';
EXEC dbo.usp_AddColumnComment N'EmailConfig', N'Name',       N'配置名称';
EXEC dbo.usp_AddColumnComment N'EmailConfig', N'SmtpServer', N'SMTP 服务器地址';
EXEC dbo.usp_AddColumnComment N'EmailConfig', N'SmtpPort',   N'SMTP 端口';
EXEC dbo.usp_AddColumnComment N'EmailConfig', N'Account',    N'发件人邮箱';
EXEC dbo.usp_AddColumnComment N'EmailConfig', N'Password',   N'授权码（AES 加密存储）';
EXEC dbo.usp_AddColumnComment N'EmailConfig', N'FromName',   N'发件人显示名';
EXEC dbo.usp_AddColumnComment N'EmailConfig', N'EnableSsl',  N'是否启用 SSL';
EXEC dbo.usp_AddColumnComment N'EmailConfig', N'IsDefault',  N'是否默认配置';
EXEC dbo.usp_AddColumnComment N'EmailConfig', N'Enabled',    N'是否启用';
EXEC dbo.usp_AddColumnComment N'EmailConfig', N'CreateTime', N'创建时间';
EXEC dbo.usp_AddColumnComment N'EmailConfig', N'UpdateTime', N'更新时间';
GO

-- 2. 邮件任务表
IF OBJECT_ID(N'dbo.EmailTask') IS NULL
BEGIN
    CREATE TABLE dbo.EmailTask (
        Id              INT IDENTITY(1,1)  NOT NULL PRIMARY KEY,
        Name            NVARCHAR(100)      NOT NULL,
        Subject         NVARCHAR(200)      NOT NULL,
        Content         NVARCHAR(2000)     NOT NULL,
        Recipients      NVARCHAR(1000)     NOT NULL,
        ScheduleType    NVARCHAR(20)       NOT NULL DEFAULT 'once',
        SendTime        DATETIME           NULL,
        CronExpression  NVARCHAR(100)      NULL,
        WeekDays        NVARCHAR(20)       NULL,
        DailyTime       NVARCHAR(10)       NULL,
        HangfireJobId   NVARCHAR(100)      NULL,
        Enabled         BIT                NOT NULL DEFAULT 1,
        Status          TINYINT            NOT NULL DEFAULT 0,
        LastSendTime    DATETIME           NULL,
        CreateTime      DATETIME           NOT NULL DEFAULT GETDATE(),
        UpdateTime      DATETIME           NOT NULL DEFAULT GETDATE(),
        CreatedById     UNIQUEIDENTIFIER   NULL
    );
END
GO

EXEC dbo.usp_AddTableComment N'EmailTask', N'邮件发送任务表';
EXEC dbo.usp_AddColumnComment N'EmailTask', N'Id',            N'主键';
EXEC dbo.usp_AddColumnComment N'EmailTask', N'Name',          N'任务名称';
EXEC dbo.usp_AddColumnComment N'EmailTask', N'Subject',       N'邮件主题';
EXEC dbo.usp_AddColumnComment N'EmailTask', N'Content',       N'邮件内容（支持 {日期} {时间} 变量）';
EXEC dbo.usp_AddColumnComment N'EmailTask', N'Recipients',    N'收件人（多个用分号分隔）';
EXEC dbo.usp_AddColumnComment N'EmailTask', N'ScheduleType',  N'调度类型：once=单次 / daily=每天 / weekly=每周 / cron=Cron 表达式';
EXEC dbo.usp_AddColumnComment N'EmailTask', N'SendTime',      N'单次发送时间';
EXEC dbo.usp_AddColumnComment N'EmailTask', N'CronExpression',N'Cron 表达式（自定义周期）';
EXEC dbo.usp_AddColumnComment N'EmailTask', N'WeekDays',      N'每周几（如 "1,3,5"，0=周日）';
EXEC dbo.usp_AddColumnComment N'EmailTask', N'DailyTime',     N'每天/每周的发送时间（如 "09:00"）';
EXEC dbo.usp_AddColumnComment N'EmailTask', N'HangfireJobId', N'Hangfire Job ID';
EXEC dbo.usp_AddColumnComment N'EmailTask', N'Enabled',       N'是否启用';
EXEC dbo.usp_AddColumnComment N'EmailTask', N'Status',        N'任务状态：0=正常 1=暂停';
EXEC dbo.usp_AddColumnComment N'EmailTask', N'LastSendTime',  N'上次发送时间';
EXEC dbo.usp_AddColumnComment N'EmailTask', N'CreateTime',    N'创建时间';
EXEC dbo.usp_AddColumnComment N'EmailTask', N'UpdateTime',    N'更新时间';
EXEC dbo.usp_AddColumnComment N'EmailTask', N'CreatedById',   N'创建人用户 Id（GUID，关联 SysUser.Id，列表关联展示账号与姓名）';
GO

-- 3. 邮件发送日志表
IF OBJECT_ID(N'dbo.EmailLog') IS NULL
BEGIN
    CREATE TABLE dbo.EmailLog (
        Id              BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        TaskId          INT                NOT NULL,
        TaskName        NVARCHAR(100)      NOT NULL DEFAULT N'',
        Recipients      NVARCHAR(1000)     NOT NULL,
        Subject         NVARCHAR(200)      NOT NULL,
        Content         NVARCHAR(MAX)      NOT NULL,  -- 实际发送内容（HTML 正文可能超 2000 字符，用 MAX）
        Status          TINYINT            NOT NULL DEFAULT 0,
        ErrorMessage    NVARCHAR(500)      NULL,
        CostMs          INT                NOT NULL DEFAULT 0,
        CreateTime      DATETIME           NOT NULL DEFAULT GETDATE(),
        CreatedById     UNIQUEIDENTIFIER   NULL
    );
    CREATE INDEX IX_EmailLog_CreateTime ON dbo.EmailLog(CreateTime);
    CREATE INDEX IX_EmailLog_TaskId ON dbo.EmailLog(TaskId);
END
GO

EXEC dbo.usp_AddTableComment N'EmailLog', N'邮件发送日志表';
EXEC dbo.usp_AddColumnComment N'EmailLog', N'Id',           N'主键';
EXEC dbo.usp_AddColumnComment N'EmailLog', N'TaskId',       N'关联任务 Id';
EXEC dbo.usp_AddColumnComment N'EmailLog', N'TaskName',     N'任务名称';
EXEC dbo.usp_AddColumnComment N'EmailLog', N'Recipients',   N'实际收件人';
EXEC dbo.usp_AddColumnComment N'EmailLog', N'Subject',      N'邮件主题';
EXEC dbo.usp_AddColumnComment N'EmailLog', N'Content',      N'实际发送内容（变量已替换）';
EXEC dbo.usp_AddColumnComment N'EmailLog', N'Status',       N'发送状态：0=失败 1=成功';
EXEC dbo.usp_AddColumnComment N'EmailLog', N'ErrorMessage', N'错误信息';
EXEC dbo.usp_AddColumnComment N'EmailLog', N'CostMs',       N'耗时毫秒';
EXEC dbo.usp_AddColumnComment N'EmailLog', N'CreateTime',   N'创建时间';
EXEC dbo.usp_AddColumnComment N'EmailLog', N'CreatedById',  N'创建人用户 Id（GUID，关联 SysUser.Id；系统自动发送时为 NULL）';
GO

-- 邮件管理菜单（已合并到上方初始菜单块）

-- ========== 系统通知模块（站内通知 + 邮件/短信/群机器人联动推送） ==========

-- 1. 系统通知表
IF OBJECT_ID(N'dbo.SysNotice') IS NULL
BEGIN
    CREATE TABLE dbo.SysNotice (
        Id           INT IDENTITY(1,1)  NOT NULL PRIMARY KEY,
        Title        NVARCHAR(200)      NOT NULL,
        Content      NVARCHAR(MAX)      NOT NULL,
        Level        TINYINT            NOT NULL DEFAULT 1,
        SendEmail    BIT                NOT NULL DEFAULT 0,
        SendSms      BIT                NOT NULL DEFAULT 0,
        SendWebhook  BIT                NOT NULL DEFAULT 0,
        Enabled      BIT                NOT NULL DEFAULT 1,
        ExpireTime   DATETIME2(0)       NULL,
        CreatedById  UNIQUEIDENTIFIER   NULL,
        CreateTime   DATETIME           NOT NULL DEFAULT GETDATE(),
        UpdateTime   DATETIME           NOT NULL DEFAULT GETDATE()
    );
    CREATE INDEX IX_SysNotice_CreateTime ON dbo.SysNotice(CreateTime DESC);
END
GO

EXEC dbo.usp_AddTableComment N'SysNotice', N'系统通知表';
EXEC dbo.usp_AddColumnComment N'SysNotice', N'Id',          N'主键';
EXEC dbo.usp_AddColumnComment N'SysNotice', N'Title',       N'通知标题';
EXEC dbo.usp_AddColumnComment N'SysNotice', N'Content',     N'通知内容';
EXEC dbo.usp_AddColumnComment N'SysNotice', N'Level',       N'通知级别：1=普通 2=重要 3=紧急';
EXEC dbo.usp_AddColumnComment N'SysNotice', N'SendEmail',   N'发布时是否联动邮件推送给已填邮箱的用户';
EXEC dbo.usp_AddColumnComment N'SysNotice', N'SendSms',     N'发布时是否联动短信推送给已填手机号的用户';
EXEC dbo.usp_AddColumnComment N'SysNotice', N'SendWebhook', N'发布时是否联动群机器人广播';
EXEC dbo.usp_AddColumnComment N'SysNotice', N'Enabled',     N'是否启用（停用后用户端不再展示）';
EXEC dbo.usp_AddColumnComment N'SysNotice', N'ExpireTime',  N'有效期截止时间（NULL=永久有效；过期后用户端不再展示）';
EXEC dbo.usp_AddColumnComment N'SysNotice', N'CreatedById', N'发布人用户 Id（GUID，关联 SysUser.Id）';
EXEC dbo.usp_AddColumnComment N'SysNotice', N'CreateTime',  N'发布时间';
EXEC dbo.usp_AddColumnComment N'SysNotice', N'UpdateTime',  N'更新时间';
GO

-- 老库补齐 SysNotice 有效期列（幂等）：新列无已有注释，直接走 usp_AddColumnComment 新增
IF OBJECT_ID(N'dbo.SysNotice') IS NOT NULL AND COL_LENGTH(N'dbo.SysNotice', N'ExpireTime') IS NULL
BEGIN
    ALTER TABLE dbo.SysNotice ADD ExpireTime DATETIME2(0) NULL;
    EXEC dbo.usp_AddColumnComment N'SysNotice', N'ExpireTime', N'有效期截止时间（NULL=永久有效；过期后用户端不再展示）';
END
GO

-- 2. 通知已读记录表（每用户每通知至多一条）
IF OBJECT_ID(N'dbo.SysNoticeRead') IS NULL
BEGIN
    CREATE TABLE dbo.SysNoticeRead (
        Id        BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        NoticeId  INT                  NOT NULL,
        UserId    UNIQUEIDENTIFIER     NOT NULL,
        ReadTime  DATETIME             NOT NULL DEFAULT GETDATE(),
        CONSTRAINT UQ_SysNoticeRead UNIQUE (NoticeId, UserId)
    );
    CREATE INDEX IX_SysNoticeRead_UserId ON dbo.SysNoticeRead(UserId);
END
GO

EXEC dbo.usp_AddTableComment N'SysNoticeRead', N'通知已读记录表';
EXEC dbo.usp_AddColumnComment N'SysNoticeRead', N'Id',       N'主键';
EXEC dbo.usp_AddColumnComment N'SysNoticeRead', N'NoticeId', N'关联通知 Id';
EXEC dbo.usp_AddColumnComment N'SysNoticeRead', N'UserId',   N'已读用户 Id（GUID，关联 SysUser.Id）';
EXEC dbo.usp_AddColumnComment N'SysNoticeRead', N'ReadTime', N'阅读时间';
GO

-- 3. 通知定向发送用户表（某通知无定向记录时默认发送给全部人员）
IF OBJECT_ID(N'dbo.SysNoticeUser') IS NULL
BEGIN
    CREATE TABLE dbo.SysNoticeUser (
        Id        BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        NoticeId  INT                  NOT NULL,
        UserId    UNIQUEIDENTIFIER     NOT NULL,
        CONSTRAINT UQ_SysNoticeUser UNIQUE (NoticeId, UserId)
    );
    CREATE INDEX IX_SysNoticeUser_NoticeId ON dbo.SysNoticeUser(NoticeId);
END
GO

EXEC dbo.usp_AddTableComment N'SysNoticeUser', N'通知定向发送用户表（无记录表示发送给全部人员）';
EXEC dbo.usp_AddColumnComment N'SysNoticeUser', N'Id',       N'主键';
EXEC dbo.usp_AddColumnComment N'SysNoticeUser', N'NoticeId', N'关联通知 Id';
EXEC dbo.usp_AddColumnComment N'SysNoticeUser', N'UserId',   N'定向接收用户 Id（GUID，关联 SysUser.Id）';
GO

-- 4. 通知定向发送角色表（角色内全部用户可见；与用户定向取并集，均无记录时默认全部人员）
IF OBJECT_ID(N'dbo.SysNoticeRole') IS NULL
BEGIN
    CREATE TABLE dbo.SysNoticeRole (
        Id        BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        NoticeId  INT                  NOT NULL,
        RoleId    INT                  NOT NULL,
        CONSTRAINT UQ_SysNoticeRole UNIQUE (NoticeId, RoleId)
    );
    CREATE INDEX IX_SysNoticeRole_NoticeId ON dbo.SysNoticeRole(NoticeId);
END
GO

EXEC dbo.usp_AddTableComment N'SysNoticeRole', N'通知定向发送角色表（无记录表示发送给全部人员）';
EXEC dbo.usp_AddColumnComment N'SysNoticeRole', N'Id',       N'主键';
EXEC dbo.usp_AddColumnComment N'SysNoticeRole', N'NoticeId', N'关联通知 Id';
EXEC dbo.usp_AddColumnComment N'SysNoticeRole', N'RoleId',   N'定向接收角色 Id（关联 SysRole.Id）';
GO

-- ========== 权限与审计模块（审计日志 / 群机器人 / 角色权限） ==========

-- 1. 操作审计日志表（仅记录写操作 POST/PUT/DELETE）
IF OBJECT_ID(N'dbo.SysAuditLog') IS NULL
BEGIN
    CREATE TABLE dbo.SysAuditLog (
        Id           BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        UserId       UNIQUEIDENTIFIER   NULL,
        Account      NVARCHAR(50)       NOT NULL DEFAULT N'',
        Action       NVARCHAR(100)      NOT NULL DEFAULT N'',
        Module       NVARCHAR(50)       NOT NULL DEFAULT N'',
        Path         NVARCHAR(300)      NOT NULL DEFAULT N'',
        Method       NVARCHAR(10)       NOT NULL DEFAULT N'',
        Ip           NVARCHAR(50)       NOT NULL DEFAULT N'',
        ParamSummary NVARCHAR(2000)     NULL,
        Success      BIT                NOT NULL DEFAULT 1,
        StatusCode   INT                NOT NULL DEFAULT 0,
        CostMs       INT                NOT NULL DEFAULT 0,
        CreateTime   DATETIME           NOT NULL DEFAULT GETDATE()
    );
    CREATE INDEX IX_SysAuditLog_CreateTime ON dbo.SysAuditLog(CreateTime);
END
GO

EXEC dbo.usp_AddTableComment N'SysAuditLog', N'操作审计日志表';
EXEC dbo.usp_AddColumnComment N'SysAuditLog', N'Id',          N'主键';
EXEC dbo.usp_AddColumnComment N'SysAuditLog', N'UserId',      N'操作人 SysUser.Id（匿名/未登录为 NULL）';
EXEC dbo.usp_AddColumnComment N'SysAuditLog', N'Account',     N'操作人账号';
EXEC dbo.usp_AddColumnComment N'SysAuditLog', N'Action',      N'动作（控制器/方法）';
EXEC dbo.usp_AddColumnComment N'SysAuditLog', N'Module',      N'所属模块（Area）';
EXEC dbo.usp_AddColumnComment N'SysAuditLog', N'Path',        N'请求路径';
EXEC dbo.usp_AddColumnComment N'SysAuditLog', N'Method',      N'HTTP 方法';
EXEC dbo.usp_AddColumnComment N'SysAuditLog', N'Ip',          N'客户端 IP';
EXEC dbo.usp_AddColumnComment N'SysAuditLog', N'ParamSummary',N'请求体摘要（截断 2000）';
EXEC dbo.usp_AddColumnComment N'SysAuditLog', N'Success',     N'是否成功（状态码 < 400）';
EXEC dbo.usp_AddColumnComment N'SysAuditLog', N'StatusCode',  N'HTTP 状态码';
EXEC dbo.usp_AddColumnComment N'SysAuditLog', N'CostMs',      N'耗时毫秒';
EXEC dbo.usp_AddColumnComment N'SysAuditLog', N'CreateTime',  N'创建时间';
GO

-- 1b. 系统错误日志表（由全局异常过滤器 BizExceptionFilter 写入未处理异常）
IF OBJECT_ID(N'dbo.SysErrorLog') IS NULL
BEGIN
    CREATE TABLE dbo.SysErrorLog (
        Id           BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        UserId       UNIQUEIDENTIFIER   NULL,
        Account      NVARCHAR(50)       NOT NULL DEFAULT N'',
        Path         NVARCHAR(500)      NOT NULL DEFAULT N'',
        Method       NVARCHAR(10)       NOT NULL DEFAULT N'',
        StatusCode   INT                NOT NULL DEFAULT 500,
        ExceptionType NVARCHAR(500)     NOT NULL DEFAULT N'',
        ErrorMessage NVARCHAR(2000)     NOT NULL DEFAULT N'',
        StackTrace   NVARCHAR(MAX)      NULL,
        Ip           NVARCHAR(50)       NOT NULL DEFAULT N'',
        CreateTime   DATETIME           NOT NULL DEFAULT GETDATE()
    );
    CREATE INDEX IX_SysErrorLog_CreateTime ON dbo.SysErrorLog(CreateTime);
END
GO

EXEC dbo.usp_AddTableComment N'SysErrorLog', N'系统错误日志表';
EXEC dbo.usp_AddColumnComment N'SysErrorLog', N'Id',            N'主键';
EXEC dbo.usp_AddColumnComment N'SysErrorLog', N'UserId',        N'操作人用户 Id（GUID，关联 SysUser.Id；未登录为 NULL）';
EXEC dbo.usp_AddColumnComment N'SysErrorLog', N'Account',       N'操作人账号（未登录为空）';
EXEC dbo.usp_AddColumnComment N'SysErrorLog', N'Path',          N'请求路径';
EXEC dbo.usp_AddColumnComment N'SysErrorLog', N'Method',        N'HTTP 方法';
EXEC dbo.usp_AddColumnComment N'SysErrorLog', N'StatusCode',    N'返回给客户端的 HTTP 状态码';
EXEC dbo.usp_AddColumnComment N'SysErrorLog', N'ExceptionType', N'异常类型全名';
EXEC dbo.usp_AddColumnComment N'SysErrorLog', N'ErrorMessage', N'异常消息（截断 2000）';
EXEC dbo.usp_AddColumnComment N'SysErrorLog', N'StackTrace',    N'完整堆栈跟踪';
EXEC dbo.usp_AddColumnComment N'SysErrorLog', N'Ip',            N'客户端 IP';
EXEC dbo.usp_AddColumnComment N'SysErrorLog', N'CreateTime',   N'发生时间';
GO

-- 2. 群机器人 Webhook 配置表（钉钉 / 企业微信 / 飞书）
IF OBJECT_ID(N'dbo.SysWebhookConfig') IS NULL
BEGIN
    CREATE TABLE dbo.SysWebhookConfig (
        Id           INT IDENTITY(1,1)  NOT NULL PRIMARY KEY,
        Name         NVARCHAR(100)      NOT NULL,
        ProviderType NVARCHAR(20)       NOT NULL DEFAULT N'dingtalk',
        WebhookUrl   NVARCHAR(1000)     NOT NULL,
        Secret       NVARCHAR(500)      NULL,
        AppKey       NVARCHAR(100)      NULL,
        AppSecret    NVARCHAR(500)      NULL,
        RecipientIds NVARCHAR(MAX)      NULL,
        EnableGroup  BIT                NOT NULL DEFAULT 1,
        EnablePrivate BIT               NOT NULL DEFAULT 0,
        UseCard      BIT                NOT NULL DEFAULT 0,
        IsDefault    BIT                NOT NULL DEFAULT 0,
        Enabled      BIT                NOT NULL DEFAULT 1,
        CreateTime   DATETIME           NOT NULL DEFAULT GETDATE(),
        UpdateTime   DATETIME           NOT NULL DEFAULT GETDATE()
    );
END
GO

EXEC dbo.usp_AddTableComment N'SysWebhookConfig', N'群机器人 Webhook 配置表';
EXEC dbo.usp_AddColumnComment N'SysWebhookConfig', N'Id',           N'主键';
EXEC dbo.usp_AddColumnComment N'SysWebhookConfig', N'Name',         N'显示名称';
EXEC dbo.usp_AddColumnComment N'SysWebhookConfig', N'ProviderType', N'机器人类型：dingtalk / wecom / feishu';
EXEC dbo.usp_AddColumnComment N'SysWebhookConfig', N'WebhookUrl',   N'机器人 Webhook 地址（群机器人用）';
EXEC dbo.usp_AddColumnComment N'SysWebhookConfig', N'Secret',       N'加签密钥（AES 加密存储，群机器人专用）';
EXEC dbo.usp_AddColumnComment N'SysWebhookConfig', N'AppKey',       N'AppKey（私聊模式专用）';
EXEC dbo.usp_AddColumnComment N'SysWebhookConfig', N'AppSecret',    N'AppSecret（AES 加密存储，私聊模式专用）';
EXEC dbo.usp_AddColumnComment N'SysWebhookConfig', N'RecipientIds', N'接收者 ID JSON 数组（私聊模式专用）';
EXEC dbo.usp_AddColumnComment N'SysWebhookConfig', N'EnableGroup',  N'是否发送群机器人';
EXEC dbo.usp_AddColumnComment N'SysWebhookConfig', N'EnablePrivate',N'是否发送私聊';
EXEC dbo.usp_AddColumnComment N'SysWebhookConfig', N'UseCard',      N'是否使用富文本卡片消息';
EXEC dbo.usp_AddColumnComment N'SysWebhookConfig', N'IsDefault',    N'是否为默认机器人（自动推送只发给默认配置）';
EXEC dbo.usp_AddColumnComment N'SysWebhookConfig', N'Enabled',      N'是否启用';
EXEC dbo.usp_AddColumnComment N'SysWebhookConfig', N'CreateTime',   N'创建时间';
EXEC dbo.usp_AddColumnComment N'SysWebhookConfig', N'UpdateTime',   N'更新时间';
GO

-- 3. 机器人发送日志表（每次 SendOneAsync 发送后记录，群/私聊各记一条）
IF OBJECT_ID(N'dbo.SysWebhookLog') IS NULL
BEGIN
    CREATE TABLE dbo.SysWebhookLog (
        Id           INT IDENTITY(1,1)  NOT NULL PRIMARY KEY,
        ConfigId     INT                NOT NULL,
        ConfigName   NVARCHAR(100)      NOT NULL,
        ProviderType NVARCHAR(20)       NOT NULL,
        Title        NVARCHAR(200)      NOT NULL,
        Content      NVARCHAR(MAX)      NOT NULL,
        Success      BIT                NOT NULL,
        ErrorMessage NVARCHAR(500)      NULL,
        CostMs       INT                NOT NULL DEFAULT 0,
        CreateTime   DATETIME           NOT NULL DEFAULT GETDATE()
    );
    CREATE INDEX IX_SysWebhookLog_CreateTime ON dbo.SysWebhookLog(CreateTime DESC);
END
GO

EXEC dbo.usp_AddTableComment N'SysWebhookLog', N'机器人发送日志表';
EXEC dbo.usp_AddColumnComment N'SysWebhookLog', N'Id',           N'主键';
EXEC dbo.usp_AddColumnComment N'SysWebhookLog', N'ConfigId',     N'关联配置 Id';
EXEC dbo.usp_AddColumnComment N'SysWebhookLog', N'ConfigName',   N'配置名称';
EXEC dbo.usp_AddColumnComment N'SysWebhookLog', N'ProviderType', N'服务商类型：dingtalk / wecom / feishu';
EXEC dbo.usp_AddColumnComment N'SysWebhookLog', N'Title',        N'消息标题';
EXEC dbo.usp_AddColumnComment N'SysWebhookLog', N'Content',      N'消息内容（截断 2000 字符）';
EXEC dbo.usp_AddColumnComment N'SysWebhookLog', N'Success',      N'是否发送成功';
EXEC dbo.usp_AddColumnComment N'SysWebhookLog', N'ErrorMessage',  N'错误信息';
EXEC dbo.usp_AddColumnComment N'SysWebhookLog', N'CostMs',       N'耗时毫秒';
EXEC dbo.usp_AddColumnComment N'SysWebhookLog', N'CreateTime',   N'发送时间';
GO

-- 4. 角色表
IF OBJECT_ID(N'dbo.SysRole') IS NULL
BEGIN
    CREATE TABLE dbo.SysRole (
        Id          INT IDENTITY(1,1)  NOT NULL PRIMARY KEY,
        Name        NVARCHAR(50)       NOT NULL,
        Code        NVARCHAR(50)       NOT NULL,
        Description NVARCHAR(200)      NULL,
        Enabled     BIT                NOT NULL DEFAULT 1,
        IsAdmin     BIT                NOT NULL DEFAULT 0,
        DataScope   INT                NOT NULL DEFAULT 0,
        CreateTime  DATETIME           NOT NULL DEFAULT GETDATE(),
        CONSTRAINT UQ_SysRole_Code UNIQUE (Code)
    );
END
GO

EXEC dbo.usp_AddTableComment N'SysRole', N'系统角色表';
EXEC dbo.usp_AddColumnComment N'SysRole', N'Id',         N'主键';
EXEC dbo.usp_AddColumnComment N'SysRole', N'Name',       N'角色名称';
EXEC dbo.usp_AddColumnComment N'SysRole', N'Code',       N'角色编码（admin 为超级管理员，全通）';
EXEC dbo.usp_AddColumnComment N'SysRole', N'Description',N'角色描述';
EXEC dbo.usp_AddColumnComment N'SysRole', N'Enabled',    N'是否启用';
EXEC dbo.usp_AddColumnComment N'SysRole', N'IsAdmin',    N'是否为管理员，可看所有用户数据';
EXEC dbo.usp_AddColumnComment N'SysRole', N'DataScope',  N'数据范围：0=本人 1=全部';
EXEC dbo.usp_AddColumnComment N'SysRole', N'CreateTime', N'创建时间';
GO

-- 4. 用户-角色关联表
IF OBJECT_ID(N'dbo.SysUserRole') IS NULL
BEGIN
    CREATE TABLE dbo.SysUserRole (
        Id     INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        UserId UNIQUEIDENTIFIER NOT NULL,
        RoleId INT NOT NULL,
        CONSTRAINT UQ_SysUserRole UNIQUE (UserId, RoleId)
    );
END
GO

EXEC dbo.usp_AddTableComment N'SysUserRole', N'用户角色关联表';
EXEC dbo.usp_AddColumnComment N'SysUserRole', N'Id',     N'主键';
EXEC dbo.usp_AddColumnComment N'SysUserRole', N'UserId', N'用户 Id';
EXEC dbo.usp_AddColumnComment N'SysUserRole', N'RoleId', N'角色 Id';
GO

-- 5. 角色-菜单关联表（菜单级权限：角色能看到哪些菜单，同时作为接口鉴权的权限码来源）
IF OBJECT_ID(N'dbo.SysRoleMenu') IS NULL
BEGIN
    CREATE TABLE dbo.SysRoleMenu (
        Id     INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        RoleId INT NOT NULL,
        MenuId INT NOT NULL,
        CONSTRAINT UQ_SysRoleMenu UNIQUE (RoleId, MenuId)
    );
END
GO

EXEC dbo.usp_AddTableComment N'SysRoleMenu', N'角色菜单关联表';
EXEC dbo.usp_AddColumnComment N'SysRoleMenu', N'Id',     N'主键';
EXEC dbo.usp_AddColumnComment N'SysRoleMenu', N'RoleId', N'角色 Id';
EXEC dbo.usp_AddColumnComment N'SysRoleMenu', N'MenuId', N'菜单 Id';
GO

-- 5b. 用户-菜单授权表（角色权限之外的额外授予，加法模型）
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

-- 6. 系统配置表（键值对配置：翻译API密钥、日志保留天数等）
IF OBJECT_ID(N'dbo.SysConfig') IS NULL
BEGIN
    CREATE TABLE dbo.SysConfig (
        Id           INT IDENTITY(1,1)   NOT NULL PRIMARY KEY,
        ConfigKey    NVARCHAR(100)       NOT NULL,
        ConfigValue  NVARCHAR(2000)      NULL,
        Category     NVARCHAR(50)        NOT NULL,
        DisplayName  NVARCHAR(100)       NOT NULL,
        Description  NVARCHAR(500)       NULL,
        InputType    NVARCHAR(20)        NOT NULL DEFAULT N'text',
        TabGroup     NVARCHAR(20)        NOT NULL DEFAULT N'system',
        SortOrder    INT                 NOT NULL DEFAULT 0,
        IsSystem     BIT                 NOT NULL DEFAULT 1,
        CreatedAt    DATETIME2           NOT NULL DEFAULT SYSUTCDATETIME(),
        UpdatedAt    DATETIME2           NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_SysConfig_ConfigKey UNIQUE (ConfigKey)
    );
END
GO

EXEC dbo.usp_AddTableComment N'SysConfig', N'系统配置表（键值对配置管理）';
EXEC dbo.usp_AddColumnComment N'SysConfig', N'ConfigKey',   N'配置键，如 BaiduTranslate.AppId';
EXEC dbo.usp_AddColumnComment N'SysConfig', N'ConfigValue', N'配置值';
EXEC dbo.usp_AddColumnComment N'SysConfig', N'Category',    N'分组：翻译服务/系统安全/日志管理/系统配置';
EXEC dbo.usp_AddColumnComment N'SysConfig', N'DisplayName', N'显示名称';
EXEC dbo.usp_AddColumnComment N'SysConfig', N'Description', N'描述说明';
EXEC dbo.usp_AddColumnComment N'SysConfig', N'InputType',    N'输入类型：text/password/number/switch';
EXEC dbo.usp_AddColumnComment N'SysConfig', N'TabGroup',     N'页签分组：system系统配置/thirdparty第三方配置';
EXEC dbo.usp_AddColumnComment N'SysConfig', N'SortOrder',   N'排序号';
EXEC dbo.usp_AddColumnComment N'SysConfig', N'IsSystem',    N'系统内置（不可删除，仅可改值）';
EXEC dbo.usp_AddColumnComment N'SysConfig', N'CreatedAt',   N'创建时间';
EXEC dbo.usp_AddColumnComment N'SysConfig', N'UpdatedAt',   N'更新时间';
GO

-- SysConfig 幂等补齐：已有表补充 TabGroup 列 + 更新第三方配置分组
IF NOT EXISTS (
    SELECT 1 FROM sys.columns c
    JOIN sys.tables t ON c.object_id = t.object_id
    WHERE t.name = N'SysConfig' AND c.name = N'TabGroup'
)
BEGIN
    ALTER TABLE dbo.SysConfig ADD TabGroup NVARCHAR(20) NOT NULL DEFAULT N'system';
END
GO

UPDATE dbo.SysConfig SET TabGroup = N'thirdparty' WHERE ConfigKey LIKE N'BaiduTranslate%';
GO

-- 系统配置初始数据（幂等，已存在的不覆盖）
IF NOT EXISTS (SELECT 1 FROM dbo.SysConfig)
BEGIN
    INSERT INTO dbo.SysConfig (ConfigKey, ConfigValue, Category, DisplayName, Description, InputType, TabGroup, SortOrder) VALUES
    (N'BaiduTranslate.AppId',   N'zkTranslateKey',                                N'翻译服务', N'百度翻译 AppId',      N'百度翻译API的应用ID',          N'password', N'thirdparty', 1),
    (N'BaiduTranslate.Secret',  N'mhq9_d8tuiathpmqojtmsr4dg',                     N'翻译服务', N'百度翻译密钥',         N'百度翻译API的密钥',            N'password', N'thirdparty', 2),
    (N'Jwt.Key',                 N'ConvenientSystem-Default-Jwt-Key-please-change-in-production', N'系统安全', N'JWT 密钥', N'用于生成登录令牌',             N'password', N'system',     1),
    (N'AppSettings.AuditLogRetentionDays', N'60',                                    N'日志管理', N'审计日志保留天数',    N'超过天数的日志自动清理',      N'number',   N'system',     1),
    (N'AppSettings.ServicePort',          N'51943',                                 N'系统配置', N'服务端口',             N'API服务监听端口（修改后需重启）', N'number',   N'system',     1),
    (N'AppSettings.PublicAppUrl',         N'http://127.0.0.1:51942',               N'系统配置', N'前端站点地址',          N'外部访问的基础地址',           N'text',     N'system',     2);
END

-- 锁屏设置已迁移至用户个人配置（UserConfig 表），从 SysConfig 中删除全局锁屏配置项
DELETE FROM dbo.SysConfig WHERE ConfigKey IN (N'AppSettings.EnableLock', N'AppSettings.LockTimeout');
GO

-- 会话超时时间：用户多久无操作后自动退出登录（分钟），0 表示不自动退出
IF NOT EXISTS (SELECT 1 FROM dbo.SysConfig WHERE ConfigKey = N'Security.SessionTimeoutMinutes')
BEGIN
    INSERT INTO dbo.SysConfig (ConfigKey, ConfigValue, Category, DisplayName, Description, InputType, TabGroup, SortOrder)
    VALUES (N'Security.SessionTimeoutMinutes', N'30', N'系统安全', N'会话超时时间（分钟）', N'用户多久无操作后自动退出登录，0 表示不自动退出', N'number', N'system', 2);
END
GO
GO

-- 7. 外部公开页面表（免登录 public=1 页面配置）
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

EXEC dbo.usp_AddTableComment N'SysPublicPage', N'外部公开页面表（免登录页面配置管理，访问链接带 public=1）';
EXEC dbo.usp_AddColumnComment N'SysPublicPage', N'PageKey',     N'路由路径，如 /lottery-trend';
EXEC dbo.usp_AddColumnComment N'SysPublicPage', N'Title',       N'显示名称';
EXEC dbo.usp_AddColumnComment N'SysPublicPage', N'Component',   N'Vue 组件路径，如 /src/common/views/PublicLotteryTrendView.vue';
EXEC dbo.usp_AddColumnComment N'SysPublicPage', N'Description', N'描述说明';
EXEC dbo.usp_AddColumnComment N'SysPublicPage', N'Enabled',     N'是否启用';
EXEC dbo.usp_AddColumnComment N'SysPublicPage', N'SortOrder',   N'排序号';
EXEC dbo.usp_AddColumnComment N'SysPublicPage', N'CreatedAt',   N'创建时间';
EXEC dbo.usp_AddColumnComment N'SysPublicPage', N'UpdatedAt',   N'更新时间';
GO

-- 外部公开页面初始数据（幂等，已存在的不覆盖）
IF NOT EXISTS (SELECT 1 FROM dbo.SysPublicPage)
BEGIN
    INSERT INTO dbo.SysPublicPage (PageKey, Title, Component, Description, Enabled, SortOrder) VALUES
    (N'/lottery-trend',          N'走势图',     N'/src/common/views/PublicLotteryTrendView.vue',    N'彩票走势图公开访问页',          1, 1),
    (N'/lottery-result-summary',  N'开奖结果汇总', N'/src/common/views/LotteryResultSummaryView.vue', N'开奖结果汇总详情页',          1, 2),
    (N'/lottery-analysis',        N'智能分析',   N'/src/common/views/LotteryAnalysisView.vue',        N'彩票智能分析公开访问页',      1, 3);
END
GO

-- 系统管理菜单（已合并到上方初始菜单块）

-- 以下菜单幂等补齐块已全部合并到上方初始菜单块（Id 1-58），无需单独维护

-- 种子角色：超级管理员（管理员=全部数据范围）
IF NOT EXISTS (SELECT 1 FROM dbo.SysRole WHERE Code = N'admin')
    INSERT INTO dbo.SysRole (Name, Code, Description, IsAdmin, DataScope) VALUES (N'超级管理员', N'admin', N'拥有全部菜单与接口权限', 1, 1);
GO

-- admin 用户关联 admin 角色（幂等）
IF NOT EXISTS (
    SELECT 1 FROM dbo.SysUserRole ur
    JOIN dbo.SysUser u ON u.Id = ur.UserId
    JOIN dbo.SysRole r ON r.Id = ur.RoleId
    WHERE u.Account = N'admin' AND r.Code = N'admin')
    INSERT INTO dbo.SysUserRole (UserId, RoleId)
    SELECT u.Id, r.Id FROM dbo.SysUser u CROSS JOIN dbo.SysRole r
    WHERE u.Account = N'admin' AND r.Code = N'admin';
GO

-- admin 角色关联全部菜单（幂等，仅补齐缺失项；后续新增菜单重复执行本脚本即可补全）
INSERT INTO dbo.SysRoleMenu (RoleId, MenuId)
SELECT r.Id, m.Id
FROM dbo.SysRole r CROSS JOIN dbo.SysMenu m
WHERE r.Code = N'admin'
  AND NOT EXISTS (SELECT 1 FROM dbo.SysRoleMenu rm WHERE rm.RoleId = r.Id AND rm.MenuId = m.Id);
GO

-- 修正分组菜单 Type：Name 和 Page 均为空的顶层/分组节点设为 Type=0（Group）
UPDATE dbo.SysMenu SET Type = 0 WHERE Name IS NULL AND Page IS NULL AND Type = 1;
GO

-- ========== 视图注册表与权限点（替代旧 Type=2 SysMenu 方案） ==========

IF OBJECT_ID(N'dbo.SysView') IS NULL
BEGIN
    CREATE TABLE dbo.SysView (
        Id          INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Name        NVARCHAR(100)     NOT NULL UNIQUE,
        Title       NVARCHAR(100)     NOT NULL,
        Component   NVARCHAR(200)     NULL,
        RoutePath   NVARCHAR(200)     NULL,
        Description NVARCHAR(500)     NULL,
        Enabled     BIT               NOT NULL DEFAULT 1,
        SortOrder   INT               NOT NULL DEFAULT 0
    );
END
GO

IF OBJECT_ID(N'dbo.SysViewPermission') IS NULL
BEGIN
    CREATE TABLE dbo.SysViewPermission (
        Id        INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        ViewId    INT               NOT NULL,
        Name      NVARCHAR(100)     NOT NULL,
        Title     NVARCHAR(100)     NOT NULL,
        SortOrder INT               NOT NULL DEFAULT 0,
        Enabled   BIT               NOT NULL DEFAULT 1,
        CONSTRAINT FK_SysViewPerm_View FOREIGN KEY (ViewId) REFERENCES dbo.SysView(Id)
    );
END
GO

IF OBJECT_ID(N'dbo.SysRoleViewPerm') IS NULL
BEGIN
    CREATE TABLE dbo.SysRoleViewPerm (
        Id         INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        RoleId     INT NOT NULL,
        ViewPermId INT NOT NULL
    );
END
GO

IF OBJECT_ID(N'dbo.SysUserViewPerm') IS NULL
BEGIN
    CREATE TABLE dbo.SysUserViewPerm (
        Id         INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        UserId     UNIQUEIDENTIFIER NOT NULL,
        ViewPermId INT NOT NULL
    );
END
GO

-- 视图种子数据：全部系统页面
IF NOT EXISTS (SELECT 1 FROM dbo.SysView)
BEGIN
    SET IDENTITY_INSERT dbo.SysView ON;
    INSERT INTO dbo.SysView (Id, Name, Title, Component, RoutePath, SortOrder) VALUES
    -- 系统管理类
    (1,  N'user-manage',      N'用户管理',   N'/src/common/views/UserManageView.vue',        N'/user-manage',       1),
    (2,  N'role-manage',      N'角色管理',   N'/src/common/views/RoleManageView.vue',        N'/role-manage',       2),
    (3,  N'menu-manage',      N'菜单管理',   N'/src/common/views/MenuManageView.vue',        N'/menu-manage',       3),
    (4,  N'view-manage',      N'视图管理',   N'/src/common/views/ViewManageView.vue',        N'/view-manage',       4),
    (5,  N'notice',           N'通知管理',   N'/src/common/views/NoticeManageView.vue',      N'/notice',            5),
    (6,  N'sys-config',       N'系统配置',   N'/src/common/views/SysConfigView.vue',         N'/sys-config',        6),
    (7,  N'permission',       N'权限设置',   N'/src/common/views/PermissionView.vue',        N'/permission',        7),
    (8,  N'sys-public-page',  N'外部页面',   N'/src/common/views/SysPublicPageView.vue',      N'/sys-public-page',   8),
    (9,  N'personal-config',  N'个人配置',   N'/src/common/views/PersonalConfigView.vue',    N'/personal-config',   9),
    (10, N'hangfire-jobs',    N'定时任务',   N'/src/common/views/HangfireJobsView.vue',      N'/hangfire-jobs',    10),
    (11, N'error-log',        N'错误日志',   N'/src/common/views/ErrorLogView.vue',         N'/error-log',        11),
    (12, N'log-viewer',       N'实时日志',   N'/src/common/views/LogViewerView.vue',        N'/log-viewer',       12),
    (13, N'audit-log',        N'审计日志',   N'/src/common/views/AuditLogView.vue',         N'/audit-log',        13),
    (14, N'online-users',     N'在线用户',   N'/src/common/views/UserOnlineView.vue',        N'/online-users',     14),
    (15, N'system-dashboard', N'系统大盘',   N'/src/common/views/SystemDashboardView.vue',   N'/system-dashboard', 15),
    -- 开发工具类
    (16, N'code-editor',      N'代码编辑器', N'/src/common/views/CodeEditorView.vue',        N'/code-editor',      16),
    (17, N'dev-tools',        N'开发工具集', N'/src/common/views/DevToolsView.vue',          N'/dev-tools',        17),
    (18, N'sql-query',        N'SQL查询',    N'/src/common/views/SqlQueryView.vue',         N'/sql-query',        18),
    (19, N'code-naming',     N'命名转换',    N'/src/common/views/CodeNamingView.vue',       N'/code-naming',      19),
    -- 彩票类
    (20, N'lottery',          N'大乐透',     N'/src/common/views/LotteryView.vue',          N'/lottery',          20),
    (21, N'lottery-ssq',      N'双色球',     N'/src/common/views/LotteryView.vue',          N'/lottery?type=SSQ', 21),
    (22, N'lottery-pl5',      N'排列五',     N'/src/common/views/LotteryView.vue',          N'/lottery?type=PL5', 22),
    (23, N'lottery-fc3d',     N'福彩3D',     N'/src/common/views/LotteryView.vue',          N'/lottery?type=FC3D',23),
    (24, N'lottery-records',  N'选号记录',   N'/src/common/views/LotteryRecordsView.vue',   N'/lottery-records',  24),
    (25, N'lottery-analysis', N'智能分析',   N'/src/common/views/LotteryAnalysisView.vue',  N'/lottery-analysis', 25),
    -- 短信类
    (26, N'sms-template',     N'短信模板',   N'/src/sms/views/SmsTemplateView.vue',         N'/sms-template',     26),
    (27, N'sms-log',          N'短信日志',   N'/src/sms/views/SmsLogView.vue',              N'/sms-log',          27),
    (28, N'sms-config',       N'短信配置',   N'/src/sms/views/SmsConfigView.vue',           N'/sms-config',       28),
    -- 邮件类
    (29, N'email-config',     N'邮件配置',   N'/src/email/views/EmailConfigView.vue',       N'/email-config',     29),
    (30, N'email-log',        N'邮件日志',   N'/src/email/views/EmailLogView.vue',          N'/email-log',        30),
    -- 通知类
    (31, N'webhook-config',   N'群机器人',   N'/src/notify/views/WebhookConfigView.vue',    N'/webhook-config',   31),
    (32, N'webhook-log',      N'Webhook日志',N'/src/notify/views/WebhookLogView.vue',        N'/webhook-log',      32),
    -- 监控类
    (33, N'web-monitor',      N'网站监控',   N'/src/common/views/WebMonitorView.vue',       N'/web-monitor',      33),
    (34, N'local-monitor',    N'本机监控',   N'/src/common/views/LocalMonitorView.vue',      N'/local-monitor',    34),
    -- 其他
    (35, N'attendance',       N'考勤查询',   N'/src/yunhan/views/AttendanceView.vue',       N'/attendance',       35),
    (36, N'python-knowledge', N'Python知识库',N'/src/common/views/PythonKnowledgeView.vue',  N'/python-knowledge', 36),
    (37, N'hangfire',         N'任务调度',   N'/src/common/views/HangfireView.vue',          N'/hangfire',         37),
    (38, N'web-package',      N'Web版本管理',N'/src/common/views/WebPackageView.vue',        N'/web-package',      38),
    (39, N'desktop-package',  N'桌面安装包', N'/src/common/views/WebPackageView.vue',        N'/web-package',      39),
    (40, N'universal-build',   N'通用构建发布', N'/src/common/views/UniversalBuildView.vue',  N'/universal-build',  40),
    (41, N'build-manager',     N'构建与发布', N'/src/common/views/BuildManagerView.vue',     N'/build-manager',     41);
    SET IDENTITY_INSERT dbo.SysView OFF;
END
GO

-- 视图权限点种子数据
IF NOT EXISTS (SELECT 1 FROM dbo.SysViewPermission)
BEGIN
    SET IDENTITY_INSERT dbo.SysViewPermission ON;
    INSERT INTO dbo.SysViewPermission (Id, ViewId, Name, Title, SortOrder) VALUES
    -- 用户管理
    (1,  1, N'user-manage:add',       N'新增用户',   1),
    (2,  1, N'user-manage:edit',      N'编辑用户',   2),
    (3,  1, N'user-manage:delete',    N'删除用户',   3),
    (4,  1, N'user-manage:reset-pwd', N'重置密码',   4),
    -- 角色管理
    (5,  2, N'role-manage:add',    N'新增角色',   1),
    (6,  2, N'role-manage:edit',   N'编辑角色',   2),
    (7,  2, N'role-manage:delete', N'删除角色',   3),
    -- 菜单管理
    (8,  3, N'menu-manage:save',   N'保存菜单',   1),
    -- 视图管理
    (9,  4, N'view-manage',        N'视图管理操作', 1),
    -- 通知管理
    (10, 5, N'notice:publish',  N'发布通知',   1),
    (11, 5, N'notice:delete',   N'删除通知',   2),
    -- 系统配置
    (12, 6, N'sys-config:save',   N'保存配置', 1),
    (13, 6, N'sys-config:reveal', N'查看明文', 2),
    -- 外部页面
    (14, 8,  N'sys-public-page:create', N'新增', 1),
    (15, 8,  N'sys-public-page:edit',   N'编辑', 2),
    (16, 8,  N'sys-public-page:delete', N'删除', 3),
    -- 个人配置
    (17, 9,  N'personal-config:save', N'保存', 1),
    -- 定时任务
    (18, 10, N'hangfire-jobs:trigger', N'触发任务', 1),
    -- 错误日志
    (19, 11, N'error-log:clear', N'清空日志', 1),
    -- 实时日志
    (20, 12, N'log-viewer:clear', N'清空日志', 1),
    -- SQL查询
    (21, 18, N'sql-query:execute',            N'执行SQL',    1),
    (22, 18, N'sql-query:save-datasource',    N'保存数据源', 2),
    (23, 18, N'sql-query:delete-datasource', N'删除数据源', 3),
    (24, 18, N'sql-query:test-connection',    N'测试连接',   4),
    -- 大乐透
    (25, 20, N'lottery:save-bets',      N'保存选号', 1),
    (26, 20, N'lottery:clear-history',   N'清空记录', 2),
    (27, 20, N'lottery:delete-record',   N'删除记录', 3),
    -- 双色球
    (28, 21, N'lottery-ssq:save-bets',      N'保存选号', 1),
    (29, 21, N'lottery-ssq:clear-history',   N'清空记录', 2),
    (30, 21, N'lottery-ssq:delete-record',   N'删除记录', 3),
    -- 排列五
    (31, 22, N'lottery-pl5:save-bets',      N'保存选号', 1),
    (32, 22, N'lottery-pl5:clear-history',   N'清空记录', 2),
    (33, 22, N'lottery-pl5:delete-record',   N'删除记录', 3),
    -- 福彩3D
    (34, 23, N'lottery-fc3d:save-bets',      N'保存选号', 1),
    (35, 23, N'lottery-fc3d:clear-history',   N'清空记录', 2),
    (36, 23, N'lottery-fc3d:delete-record',   N'删除记录', 3),
    -- 选号记录
    (37, 24, N'lottery-records:verify-issue', N'整期验奖', 1),
    (38, 24, N'lottery-records:verify',        N'单条验奖', 2),
    -- 智能分析
    (39, 25, N'lottery-analysis:run',       N'重新分析', 1),
    (40, 25, N'lottery-analysis:save-bets', N'保存推荐', 2),
    -- 短信模板
    (41, 26, N'sms-template:create', N'新建模板', 1),
    (42, 26, N'sms-template:edit',   N'编辑模板', 2),
    (43, 26, N'sms-template:delete', N'删除模板', 3),
    (44, 26, N'sms-template:toggle', N'启用/禁用', 4),
    -- 短信配置
    (45, 28, N'sms-config:create',    N'新增配置', 1),
    (46, 28, N'sms-config:edit',     N'编辑配置', 2),
    (47, 28, N'sms-config:delete',   N'删除配置', 3),
    (48, 28, N'sms-config:test-send',N'测试发送', 4),
    -- 邮件配置
    (49, 29, N'email-config:create',    N'新增配置', 1),
    (50, 29, N'email-config:edit',     N'编辑配置', 2),
    (51, 29, N'email-config:delete',   N'删除配置', 3),
    (52, 29, N'email-config:test-send',N'测试发送', 4),
    -- 群机器人
    (53, 31, N'webhook-config:create',    N'新增机器人', 1),
    (54, 31, N'webhook-config:edit',     N'编辑机器人', 2),
    (55, 31, N'webhook-config:delete',   N'删除机器人', 3),
    (56, 31, N'webhook-config:test-send', N'测试发送',  4),
    -- 网站监控
    (57, 33, N'web-monitor:create', N'新增监控', 1),
    (58, 33, N'web-monitor:edit',   N'编辑监控', 2),
    (59, 33, N'web-monitor:delete', N'删除监控', 3),
    (60, 33, N'web-monitor:check',  N'立即检测', 4),
    -- 本机监控
    (61, 34, N'local-monitor:clean-disk', N'清理磁盘', 1),
    -- 代码编辑器
    (66, 16, N'code-editor:create',  N'新建文件', 1),
    (67, 16, N'code-editor:save',   N'保存文件', 2),
    (68, 16, N'code-editor:save-as',N'另存为',   3),
    -- Web版本管理
    (69, 38, N'web-package',          N'查看版本列表', 0),
    (70, 38, N'web-package:upload',   N'上传版本包', 1),
    (71, 38, N'web-package:activate', N'激活版本', 2),
    (72, 38, N'web-package:delete',   N'删除版本包', 3),
    -- 桌面安装包（与 Web版本管理 同页面，作为页签展示）
    (73, 39, N'desktop-package',          N'查看桌面安装包', 0),
    (74, 39, N'desktop-package:upload',   N'上传安装包', 1),
    (75, 39, N'desktop-package:activate', N'激活安装包', 2),
    (76, 39, N'desktop-package:delete',   N'删除安装包', 3),
    -- 通用构建发布
    (77, 40, N'universal-build:execute',  N'执行构建', 1),
    -- 构建与发布
    (78, 41, N'build-manager:execute',  N'执行构建', 1),
    (79, 41, N'build-manager:publish', N'发布到服务器', 2);
    SET IDENTITY_INSERT dbo.SysViewPermission OFF;
END
GO

-- admin 角色自动拥有所有视图权限点
INSERT INTO dbo.SysRoleViewPerm (RoleId, ViewPermId)
SELECT r.Id, vp.Id
FROM dbo.SysRole r CROSS JOIN dbo.SysViewPermission vp
WHERE r.Code = N'admin'
  AND NOT EXISTS (SELECT 1 FROM dbo.SysRoleViewPerm rvp WHERE rvp.RoleId = r.Id AND rvp.ViewPermId = vp.Id);
GO

-- 种子角色：普通用户（新注册用户自动赋予此角色）
IF NOT EXISTS (SELECT 1 FROM dbo.SysRole WHERE Code = N'user')
    INSERT INTO dbo.SysRole (Name, Code, Description) VALUES (N'普通用户', N'user', N'普通用户角色，拥有常用功能菜单权限');
GO

-- 普通用户角色关联菜单（排除：菜单管理、开发工具、系统管理、邮件配置、短信系统配置、群机器人配置）
-- 通过菜单 Name/Title 匹配，不依赖固定 Id，避免 Identity 偏移导致权限错位
-- 注：父级分组菜单由应用层 MenuService 自动回溯补齐，此处只需关联叶子菜单
INSERT INTO dbo.SysRoleMenu (RoleId, MenuId)
SELECT r.Id, m.Id
FROM dbo.SysRole r CROSS JOIN dbo.SysMenu m
WHERE r.Code = N'user'
  AND (
      -- 有 Name 的内部页面和链接
      m.Name IN (
          N'attendance', N'deepseek',
          N'sms-template', N'sms-log',
          N'email-log',
          N'lottery', N'lottery-ssq', N'lottery-pl5', N'lottery-fc3d', N'lottery-records', N'lottery-analysis',
          N'web-monitor', N'local-monitor',
          N'webhook-log',
          N'personal-config'
      )
      OR
      -- 无 Name 的外链菜单，按标题匹配
      (m.Name IS NULL AND m.Title IN (
          N'项目管理', N'正式-ERP系统', N'测试-ERP系统',
          N'有道词典', N'云效', N'百度翻译', N'deepseek', N'豆包', N'json格式化'
      ))
  )
  AND NOT EXISTS (SELECT 1 FROM dbo.SysRoleMenu rm WHERE rm.RoleId = r.Id AND rm.MenuId = m.Id);
GO

-- ========== 彩票选号记录表（多彩种共用） ==========
IF OBJECT_ID(N'dbo.LotteryRecord') IS NULL
BEGIN
CREATE TABLE dbo.LotteryRecord (
        Id            INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        UserId        UNIQUEIDENTIFIER  NOT NULL,                -- 所属用户 Id（GUID）
        LotteryType   NVARCHAR(10)      NOT NULL DEFAULT 'DLT',  -- 彩种代码（DLT/SSQ/PL5/FC3D）
        FrontNumbers  NVARCHAR(50)      NOT NULL DEFAULT '',     -- 前区号码（逗号分隔；池选型已排序，位置型按位存储）
        BackNumbers   NVARCHAR(20)      NOT NULL DEFAULT '',     -- 后区号码（逗号分隔，已排序，如 "02,11"）
        IssueNumber   NVARCHAR(20)      NULL,                    -- 所属期号（保存时默认取下一期；历史记录为 NULL）
        DrawDate      DATETIME2(0)      NULL,                    -- 开奖日期（保存时默认取下一期开奖日；历史记录为 NULL）
        CreatedAt     DATETIME2(0)      NOT NULL DEFAULT GETDATE() -- 选号时间
);
-- 索引：按彩种+用户+日期查询
CREATE INDEX IX_LotteryRecord_Type_User_CreatedAt ON dbo.LotteryRecord(LotteryType, UserId, CreatedAt DESC);
END
GO

-- ========== 彩票开奖记录表（走势图数据源，多彩种共用） ==========
IF OBJECT_ID(N'dbo.LotteryDraw') IS NULL
BEGIN
CREATE TABLE dbo.LotteryDraw (
        Id            INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        LotteryType   NVARCHAR(10)      NOT NULL DEFAULT 'DLT',  -- 彩种代码（DLT/SSQ/PL5/FC3D）
        IssueNumber   NVARCHAR(20)      NOT NULL,                -- 期号（如 "2026087"）
        DrawDate      DATETIME2(0)      NOT NULL,                -- 开奖日期
        FrontNumbers  NVARCHAR(50)      NOT NULL DEFAULT '',     -- 前区号码（逗号分隔；池选型已排序，位置型按位存储）
        BackNumbers   NVARCHAR(20)      NOT NULL DEFAULT '',     -- 后区号码（逗号分隔，已排序，如 "02,11"）
        PrizeDetail   NVARCHAR(MAX)     NULL,                    -- 官方中奖明细 JSON（[{"grade":"一等奖","count":2,"money":9662603}]，历史期未采集时为 NULL）
        SalesAmount   DECIMAL(18,2)     NULL,                    -- 当期销量（元）
        PoolBalance   DECIMAL(18,2)     NULL,                    -- 奖池滚存（元；固定奖彩种为空）
        PrizeArea     NVARCHAR(500)     NULL,                    -- 一等奖中奖地区文本（福彩双色球官网通告口径，多省份时较长；无则 NULL）
        NoticeUrl     NVARCHAR(500)     NULL,                    -- 官方开奖通告 PDF 链接（体彩大乐透/排列五；无则 NULL）
        CreatedAt     DATETIME2(0)      NOT NULL DEFAULT GETDATE() -- 创建时间
);
-- 索引：同彩种内期号唯一
CREATE UNIQUE INDEX IX_LotteryDraw_Type_Issue ON dbo.LotteryDraw(LotteryType, IssueNumber);
END
GO

-- 旧库兼容：早期仅按期号唯一，多彩种后不同彩种期号可能相同，确保唯一索引为（彩种+期号）
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_LotteryDraw_IssueNumber' AND object_id = OBJECT_ID(N'dbo.LotteryDraw'))
    DROP INDEX IX_LotteryDraw_IssueNumber ON dbo.LotteryDraw;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_LotteryDraw_Type_Issue' AND object_id = OBJECT_ID(N'dbo.LotteryDraw'))
    CREATE UNIQUE INDEX IX_LotteryDraw_Type_Issue ON dbo.LotteryDraw(LotteryType, IssueNumber);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_LotteryRecord_Type_User_CreatedAt' AND object_id = OBJECT_ID(N'dbo.LotteryRecord'))
    CREATE INDEX IX_LotteryRecord_Type_User_CreatedAt ON dbo.LotteryRecord(LotteryType, UserId, CreatedAt DESC);
GO

-- 老库补齐：选号记录表增加期号/开奖日期字段（幂等；保存选号时默认存入下一期期号与开奖日期）
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.LotteryRecord') AND name = N'IssueNumber')
    ALTER TABLE dbo.LotteryRecord ADD IssueNumber NVARCHAR(20) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.LotteryRecord') AND name = N'DrawDate')
    ALTER TABLE dbo.LotteryRecord ADD DrawDate DATETIME2(0) NULL;
GO

EXEC dbo.usp_AddColumnComment N'LotteryRecord', N'IssueNumber', N'所属期号（保存时默认取下一期；历史记录为 NULL）';
EXEC dbo.usp_AddColumnComment N'LotteryRecord', N'DrawDate', N'开奖日期（保存时默认取下一期开奖日；历史记录为 NULL）';
GO

-- 表与其余列注释（建表时只写了 DDL 行尾注释，补上数据库扩展属性注释）
EXEC dbo.usp_AddTableComment N'LotteryRecord', N'彩票选号记录表（多彩种共用）';
EXEC dbo.usp_AddColumnComment N'LotteryRecord', N'Id', N'主键';
EXEC dbo.usp_AddColumnComment N'LotteryRecord', N'UserId', N'所属用户 Id（GUID，关联 SysUser.Id）';
EXEC dbo.usp_AddColumnComment N'LotteryRecord', N'LotteryType', N'彩种代码（DLT/SSQ/PL5/FC3D）';
EXEC dbo.usp_AddColumnComment N'LotteryRecord', N'FrontNumbers', N'前区号码（逗号分隔；池选型已排序，位置型按位存储）';
EXEC dbo.usp_AddColumnComment N'LotteryRecord', N'BackNumbers', N'后区号码（逗号分隔已排序；位置型彩种为空串）';
EXEC dbo.usp_AddColumnComment N'LotteryRecord', N'CreatedAt', N'选号时间';
GO

-- 老库补齐：开奖记录表增加官方中奖明细/销量/奖池/中奖地区/通告链接字段（幂等）
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.LotteryDraw') AND name = N'PrizeDetail')
    ALTER TABLE dbo.LotteryDraw ADD PrizeDetail NVARCHAR(MAX) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.LotteryDraw') AND name = N'SalesAmount')
    ALTER TABLE dbo.LotteryDraw ADD SalesAmount DECIMAL(18,2) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.LotteryDraw') AND name = N'PoolBalance')
    ALTER TABLE dbo.LotteryDraw ADD PoolBalance DECIMAL(18,2) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.LotteryDraw') AND name = N'PrizeArea')
    ALTER TABLE dbo.LotteryDraw ADD PrizeArea NVARCHAR(500) NULL;
-- 双色球中奖地区文本在多省份时超过 200 字符，早期建的 200 列需扩容
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.LotteryDraw') AND name = N'PrizeArea' AND max_length < 1000)
    ALTER TABLE dbo.LotteryDraw ALTER COLUMN PrizeArea NVARCHAR(500) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.LotteryDraw') AND name = N'NoticeUrl')
    ALTER TABLE dbo.LotteryDraw ADD NoticeUrl NVARCHAR(500) NULL;
GO

EXEC dbo.usp_AddColumnComment N'LotteryDraw', N'PrizeDetail', N'官方中奖明细 JSON（奖级/全国注数/单注奖金，历史期未采集时为 NULL）';
EXEC dbo.usp_AddColumnComment N'LotteryDraw', N'SalesAmount', N'当期销量（元）';
EXEC dbo.usp_AddColumnComment N'LotteryDraw', N'PoolBalance', N'奖池滚存（元；固定奖彩种为空）';
EXEC dbo.usp_AddColumnComment N'LotteryDraw', N'PrizeArea', N'一等奖中奖地区文本（福彩双色球官网通告口径；无则 NULL）';
EXEC dbo.usp_AddColumnComment N'LotteryDraw', N'NoticeUrl', N'官方开奖通告 PDF 链接（体彩大乐透/排列五；无则 NULL）';
GO

-- 表与其余列注释（建表时只写了 DDL 行尾注释，补上数据库扩展属性注释）
EXEC dbo.usp_AddTableComment N'LotteryDraw', N'彩票开奖记录表（走势图与判奖数据源，多彩种共用）';
EXEC dbo.usp_AddColumnComment N'LotteryDraw', N'Id', N'主键';
EXEC dbo.usp_AddColumnComment N'LotteryDraw', N'LotteryType', N'彩种代码（DLT/SSQ/PL5/FC3D）';
EXEC dbo.usp_AddColumnComment N'LotteryDraw', N'IssueNumber', N'期号（同彩种内唯一）';
EXEC dbo.usp_AddColumnComment N'LotteryDraw', N'DrawDate', N'开奖日期';
EXEC dbo.usp_AddColumnComment N'LotteryDraw', N'FrontNumbers', N'前区号码（逗号分隔；池选型已排序，位置型按位存储）';
EXEC dbo.usp_AddColumnComment N'LotteryDraw', N'BackNumbers', N'后区号码（逗号分隔已排序；位置型彩种为空串）';
EXEC dbo.usp_AddColumnComment N'LotteryDraw', N'CreatedAt', N'创建时间';
GO

-- ========== 彩票玩法规则表（每日自官网抓取条文，判奖与奖金匹配的数据源） ==========
-- 一个彩种可有多个版本：只有 Status=1 的那一行参与判奖；官网条文变动时新版先入 Status=2 待审，
-- 经人工确认后才切为生效，避免官网改版或解析歧义直接污染判奖结果
IF OBJECT_ID(N'dbo.LotteryRule') IS NULL
BEGIN
CREATE TABLE dbo.LotteryRule (
        Id            INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        LotteryType   NVARCHAR(10)      NOT NULL DEFAULT 'DLT',  -- 彩种代码（DLT/SSQ/PL5/FC3D）
        Version       INT               NOT NULL DEFAULT 1,      -- 同彩种内递增的规则版本号
        Status        TINYINT           NOT NULL DEFAULT 2,      -- 版本状态：1=生效中 2=待审核 3=已被新版替代 4=已驳回
        SourceUrl     NVARCHAR(500)     NULL,                    -- 条文抓取来源页面地址
        RuleText      NVARCHAR(MAX)     NULL,                    -- 官网玩法规则条文全文（纯文本，供页面展示与版本比对）
        GradeJson     NVARCHAR(MAX)     NULL,                    -- 结构化奖级规则 JSON（奖级名/命中条件/固定奖金，判奖直接读此列）
        GradeCount    INT               NOT NULL DEFAULT 0,      -- 本版本解析出的奖级数（为 0 说明解析失败，不会入库）
        ContentHash   NVARCHAR(64)      NULL,                    -- 条文+奖级 JSON 的 SHA256（比对官网规则是否变动）
        CrawledAt     DATETIME2(0)      NOT NULL DEFAULT GETDATE(), -- 本版本最近一次抓到的时间
        EffectiveAt   DATETIME2(0)      NULL,                    -- 切为生效的时间（未生效过为 NULL）
        ReviewedBy    NVARCHAR(50)      NULL,                    -- 审核人账号（首次自动生效时为系统）
        Remark        NVARCHAR(500)     NULL,                    -- 备注（首次自动生效/驳回原因等）
        CreatedAt     DATETIME2(0)      NOT NULL DEFAULT GETDATE() -- 创建时间
);
-- 索引：同彩种内版本号唯一
CREATE UNIQUE INDEX IX_LotteryRule_Type_Version ON dbo.LotteryRule(LotteryType, Version);
-- 索引：按彩种+状态取生效/待审版本（判奖热路径）
CREATE INDEX IX_LotteryRule_Type_Status ON dbo.LotteryRule(LotteryType, Status);
END
GO

EXEC dbo.usp_AddTableComment N'LotteryRule', N'彩票玩法规则表（每日自官网抓取条文，判奖与奖金匹配的数据源）';
EXEC dbo.usp_AddColumnComment N'LotteryRule', N'Id', N'主键';
EXEC dbo.usp_AddColumnComment N'LotteryRule', N'LotteryType', N'彩种代码（DLT/SSQ/PL5/FC3D）';
EXEC dbo.usp_AddColumnComment N'LotteryRule', N'Version', N'同彩种内递增的规则版本号';
EXEC dbo.usp_AddColumnComment N'LotteryRule', N'Status', N'版本状态：1=生效中 2=待审核 3=已被新版替代 4=已驳回';
EXEC dbo.usp_AddColumnComment N'LotteryRule', N'SourceUrl', N'条文抓取来源页面地址';
EXEC dbo.usp_AddColumnComment N'LotteryRule', N'RuleText', N'官网玩法规则条文全文（纯文本）';
EXEC dbo.usp_AddColumnComment N'LotteryRule', N'GradeJson', N'结构化奖级规则 JSON（奖级名/命中条件/固定奖金）';
EXEC dbo.usp_AddColumnComment N'LotteryRule', N'GradeCount', N'本版本解析出的奖级数';
EXEC dbo.usp_AddColumnComment N'LotteryRule', N'ContentHash', N'条文+奖级 JSON 的 SHA256（比对规则是否变动）';
EXEC dbo.usp_AddColumnComment N'LotteryRule', N'CrawledAt', N'本版本最近一次抓到的时间';
EXEC dbo.usp_AddColumnComment N'LotteryRule', N'EffectiveAt', N'切为生效的时间（未生效过为 NULL）';
EXEC dbo.usp_AddColumnComment N'LotteryRule', N'ReviewedBy', N'审核人账号（首次自动生效时为系统）';
EXEC dbo.usp_AddColumnComment N'LotteryRule', N'Remark', N'备注（首次自动生效/驳回原因等）';
EXEC dbo.usp_AddColumnComment N'LotteryRule', N'CreatedAt', N'创建时间';
GO

-- ========== 网站/API 监控：监控目标表 ==========
IF OBJECT_ID(N'dbo.WebMonitorTarget') IS NULL
BEGIN
CREATE TABLE dbo.WebMonitorTarget (
        Id              INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Name            NVARCHAR(100)     NOT NULL,                      -- 监控目标名称
        Url             NVARCHAR(500)     NOT NULL,                      -- 被监控地址（http/https）
        Method          NVARCHAR(10)      NOT NULL DEFAULT 'GET',        -- 请求方式（GET/POST/HEAD）
        ExpectStatus    INT               NOT NULL DEFAULT 200,          -- 期望 HTTP 状态码
        ExpectKeyword   NVARCHAR(200)     NULL,                          -- 期望关键字（响应体包含才算正常；NULL 不校验）
        TimeoutSeconds  INT               NOT NULL DEFAULT 10,           -- 单次探测超时（秒）
        IntervalMinutes INT               NOT NULL DEFAULT 10,           -- 探测间隔（分钟）
        Enabled         BIT               NOT NULL DEFAULT 1,            -- 是否启用监控
        NotifyEmail     BIT               NOT NULL DEFAULT 1,            -- 状态变化时是否邮件告警
        LastStatus      TINYINT           NULL,                          -- 最近探测结果：NULL=未探测 1=正常 2=异常
        LastLatencyMs   INT               NULL,                          -- 最近探测耗时（毫秒）
        LastErrorMsg    NVARCHAR(500)     NULL,                          -- 最近异常原因
        LastCheckAt     DATETIME2(0)      NULL,                          -- 最近探测时间
        Remark          NVARCHAR(200)     NULL,                          -- 备注
        CreateTime      DATETIME2(0)      NOT NULL DEFAULT GETDATE()     -- 创建时间
);
END
GO

EXEC dbo.usp_AddTableComment N'WebMonitorTarget', N'网站/API 监控目标表';
EXEC dbo.usp_AddColumnComment N'WebMonitorTarget', N'Id', N'主键';
EXEC dbo.usp_AddColumnComment N'WebMonitorTarget', N'Name', N'监控目标名称';
EXEC dbo.usp_AddColumnComment N'WebMonitorTarget', N'Url', N'被监控地址（http/https）';
EXEC dbo.usp_AddColumnComment N'WebMonitorTarget', N'Method', N'请求方式（GET/POST/HEAD）';
EXEC dbo.usp_AddColumnComment N'WebMonitorTarget', N'ExpectStatus', N'期望 HTTP 状态码';
EXEC dbo.usp_AddColumnComment N'WebMonitorTarget', N'ExpectKeyword', N'期望关键字（响应体包含才算正常；NULL 不校验）';
EXEC dbo.usp_AddColumnComment N'WebMonitorTarget', N'TimeoutSeconds', N'单次探测超时（秒）';
EXEC dbo.usp_AddColumnComment N'WebMonitorTarget', N'IntervalMinutes', N'探测间隔（分钟）';
EXEC dbo.usp_AddColumnComment N'WebMonitorTarget', N'Enabled', N'是否启用监控';
EXEC dbo.usp_AddColumnComment N'WebMonitorTarget', N'NotifyEmail', N'状态变化时是否邮件告警';
EXEC dbo.usp_AddColumnComment N'WebMonitorTarget', N'LastStatus', N'最近探测结果：NULL=未探测 1=正常 2=异常';
EXEC dbo.usp_AddColumnComment N'WebMonitorTarget', N'LastLatencyMs', N'最近探测耗时（毫秒）';
EXEC dbo.usp_AddColumnComment N'WebMonitorTarget', N'LastErrorMsg', N'最近异常原因';
EXEC dbo.usp_AddColumnComment N'WebMonitorTarget', N'LastCheckAt', N'最近探测时间';
EXEC dbo.usp_AddColumnComment N'WebMonitorTarget', N'Remark', N'备注';
EXEC dbo.usp_AddColumnComment N'WebMonitorTarget', N'CreateTime', N'创建时间';
GO

-- ========== 网站/API 监控：探测日志表 ==========
IF OBJECT_ID(N'dbo.WebMonitorLog') IS NULL
BEGIN
CREATE TABLE dbo.WebMonitorLog (
        Id           BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        TargetId     INT               NOT NULL,                      -- 关联监控目标 Id
        Status       TINYINT           NOT NULL,                      -- 探测结果：1=正常 2=异常
        HttpStatusCode INT             NULL,                          -- 实际 HTTP 状态码（网络层失败为 NULL）
        LatencyMs    INT               NULL,                          -- 探测耗时（毫秒）
        ErrorMsg     NVARCHAR(500)     NULL,                          -- 异常原因
        CheckAt      DATETIME2(0)      NOT NULL DEFAULT GETDATE()     -- 探测时间
);
-- 索引：按目标查最近探测记录
CREATE INDEX IX_WebMonitorLog_Target_CheckAt ON dbo.WebMonitorLog(TargetId, CheckAt DESC);
END
GO

EXEC dbo.usp_AddTableComment N'WebMonitorLog', N'网站/API 监控探测日志表（保留 30 天）';
EXEC dbo.usp_AddColumnComment N'WebMonitorLog', N'Id', N'主键';
EXEC dbo.usp_AddColumnComment N'WebMonitorLog', N'TargetId', N'关联监控目标 Id';
EXEC dbo.usp_AddColumnComment N'WebMonitorLog', N'Status', N'探测结果：1=正常 2=异常';
EXEC dbo.usp_AddColumnComment N'WebMonitorLog', N'HttpStatusCode', N'实际 HTTP 状态码（网络层失败为 NULL）';
EXEC dbo.usp_AddColumnComment N'WebMonitorLog', N'LatencyMs', N'探测耗时（毫秒）';
EXEC dbo.usp_AddColumnComment N'WebMonitorLog', N'ErrorMsg', N'异常原因';
EXEC dbo.usp_AddColumnComment N'WebMonitorLog', N'CheckAt', N'探测时间';
GO

-- ========== 知识库菜单 ==========
IF NOT EXISTS (SELECT 1 FROM dbo.SysMenu WHERE Title = N'知识库')
BEGIN
    INSERT INTO dbo.SysMenu (Title, Page, IsFloat, Visible, IsExternal, Editable, Name, Component, SortOrder)
    VALUES (N'知识库', NULL, 0, 1, 0, 1, NULL, NULL, 10);

    DECLARE @kbId INT = (SELECT TOP 1 Id FROM dbo.SysMenu WHERE Title = N'知识库');
    IF @kbId IS NOT NULL
        INSERT INTO dbo.SysMenu (ParentId, Title, Page, IsFloat, Visible, IsExternal, Editable, Name, Component, SortOrder)
        VALUES (@kbId, N'Python', N'/python-knowledge', 0, 1, 0, 1, N'python-knowledge', N'/src/common/views/PythonKnowledgeView.vue', 1);
END
GO

-- ========== 用户个人配置表 ==========
IF OBJECT_ID(N'dbo.UserConfig') IS NULL
BEGIN
    CREATE TABLE dbo.UserConfig (
        Id           INT IDENTITY(1,1)    NOT NULL PRIMARY KEY,
        UserId       UNIQUEIDENTIFIER    NOT NULL,                          -- 关联 SysUser.Id
        ConfigKey    NVARCHAR(100)        NOT NULL,                          -- 配置键，如 AppSettings.EnableLock
        ConfigValue  NVARCHAR(2000)       NULL,                              -- 配置值
        CreatedAt    DATETIME2            NOT NULL DEFAULT GETUTCDATE(),      -- 创建时间
        UpdatedAt    DATETIME2            NULL,                               -- 更新时间
        CONSTRAINT UQ_UserConfig_UserId_Key UNIQUE (UserId, ConfigKey)      -- 每用户每键唯一
    );
    CREATE INDEX IX_UserConfig_UserId ON dbo.UserConfig(UserId);             -- 按用户查全部配置
END
GO

EXEC dbo.usp_AddTableComment N'UserConfig', N'用户个人配置表（键值对，用户级个性化配置如锁屏开关/超时）';
EXEC dbo.usp_AddColumnComment N'UserConfig', N'Id', N'主键';
EXEC dbo.usp_AddColumnComment N'UserConfig', N'UserId', N'关联 SysUser.Id';
EXEC dbo.usp_AddColumnComment N'UserConfig', N'ConfigKey', N'配置键，如 AppSettings.EnableLock';
EXEC dbo.usp_AddColumnComment N'UserConfig', N'ConfigValue', N'配置值';
EXEC dbo.usp_AddColumnComment N'UserConfig', N'CreatedAt', N'创建时间';
EXEC dbo.usp_AddColumnComment N'UserConfig', N'UpdatedAt', N'更新时间';
GO

-- ============================================================
-- 定时任务执行日志（由各 Job 基类 ExecuteWithLog 在执行前后自动写入/更新）
-- ============================================================
IF OBJECT_ID('dbo.JobExecutionLog', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.JobExecutionLog (
        Id              BIGINT IDENTITY(1,1) PRIMARY KEY,
        JobName         NVARCHAR(200) NOT NULL,   -- 对应 RecurringJobId（如"网站监控定时巡检"）
        State           NVARCHAR(50)  NOT NULL,   -- Succeeded / Failed
        MethodName      NVARCHAR(200) NULL,       -- 方法名
        Arguments       NVARCHAR(MAX) NULL,       -- 参数 JSON
        StartedAt       DATETIME2     NOT NULL,   -- 开始时间
        FinishedAt      DATETIME2     NULL,       -- 结束时间
        DurationMs      BIGINT        NULL,       -- 耗时毫秒
        Error           NVARCHAR(MAX) NULL,       -- 异常信息
        CreatedAt       DATETIME2     NOT NULL DEFAULT(GETDATE())
    );
    CREATE INDEX IX_JobExecLog_Name ON dbo.JobExecutionLog(JobName, Id DESC);
END
GO

EXEC dbo.usp_AddTableComment N'JobExecutionLog', N'定时任务执行日志（Job 基类自动记录）';
EXEC dbo.usp_AddColumnComment N'JobExecutionLog', N'Id', N'主键';
EXEC dbo.usp_AddColumnComment N'JobExecutionLog', N'JobName', N'对应 RecurringJobId';
EXEC dbo.usp_AddColumnComment N'JobExecutionLog', N'State', N'执行状态：Succeeded/Failed';
EXEC dbo.usp_AddColumnComment N'JobExecutionLog', N'MethodName', N'方法名';
EXEC dbo.usp_AddColumnComment N'JobExecutionLog', N'Arguments', N'参数 JSON';
EXEC dbo.usp_AddColumnComment N'JobExecutionLog', N'StartedAt', N'开始时间';
EXEC dbo.usp_AddColumnComment N'JobExecutionLog', N'FinishedAt', N'结束时间';
EXEC dbo.usp_AddColumnComment N'JobExecutionLog', N'DurationMs', N'耗时毫秒';
EXEC dbo.usp_AddColumnComment N'JobExecutionLog', N'Error', N'异常信息';
EXEC dbo.usp_AddColumnComment N'JobExecutionLog', N'CreatedAt', N'创建时间';
GO

PRINT N'ConvenientSystem 数据库初始化完成';
GO

-- 006 短信/邮件配置列表化 + 机器人菜单重组 + 发送日志
-- 幂等脚本：可重复执行

-- ========== 1. SmsProviderConfig 加列（Name/IsDefault/TemplateId/Enabled/CreateTime） ==========
IF COL_LENGTH('dbo.SmsProviderConfig', 'Name') IS NULL
BEGIN
    ALTER TABLE dbo.SmsProviderConfig ADD Name NVARCHAR(100) NOT NULL DEFAULT N'';
    EXEC dbo.usp_AddColumnComment N'SmsProviderConfig', N'Name', N'配置名称';
END
GO

IF COL_LENGTH('dbo.SmsProviderConfig', 'IsDefault') IS NULL
BEGIN
    ALTER TABLE dbo.SmsProviderConfig ADD IsDefault BIT NOT NULL DEFAULT 0;
    EXEC dbo.usp_AddColumnComment N'SmsProviderConfig', N'IsDefault', N'是否默认配置';
END
GO

IF COL_LENGTH('dbo.SmsProviderConfig', 'TemplateId') IS NULL
BEGIN
    ALTER TABLE dbo.SmsProviderConfig ADD TemplateId INT NULL;
    EXEC dbo.usp_AddColumnComment N'SmsProviderConfig', N'TemplateId', N'关联短信模板 Id（用于测试发送）';
END
GO

IF COL_LENGTH('dbo.SmsProviderConfig', 'Enabled') IS NULL
BEGIN
    ALTER TABLE dbo.SmsProviderConfig ADD Enabled BIT NOT NULL DEFAULT 1;
    EXEC dbo.usp_AddColumnComment N'SmsProviderConfig', N'Enabled', N'是否启用';
END
GO

IF COL_LENGTH('dbo.SmsProviderConfig', 'CreateTime') IS NULL
BEGIN
    ALTER TABLE dbo.SmsProviderConfig ADD CreateTime DATETIME NOT NULL DEFAULT GETDATE();
    EXEC dbo.usp_AddColumnComment N'SmsProviderConfig', N'CreateTime', N'创建时间';
END
GO

-- 给已有配置回填名称
UPDATE dbo.SmsProviderConfig SET Name = ProviderType WHERE Name = N'';
GO

-- 如果没有默认配置，标记最新一条为默认
IF NOT EXISTS (SELECT 1 FROM dbo.SmsProviderConfig WHERE IsDefault = 1)
    UPDATE dbo.SmsProviderConfig SET IsDefault = 1
    WHERE Id = (SELECT TOP 1 Id FROM dbo.SmsProviderConfig ORDER BY Id DESC);
GO

-- ========== 2. EmailConfig 加列（Name/IsDefault/Enabled/CreateTime） ==========
IF COL_LENGTH('dbo.EmailConfig', 'Name') IS NULL
BEGIN
    ALTER TABLE dbo.EmailConfig ADD Name NVARCHAR(100) NOT NULL DEFAULT N'';
    EXEC dbo.usp_AddColumnComment N'EmailConfig', N'Name', N'配置名称';
END
GO

IF COL_LENGTH('dbo.EmailConfig', 'IsDefault') IS NULL
BEGIN
    ALTER TABLE dbo.EmailConfig ADD IsDefault BIT NOT NULL DEFAULT 0;
    EXEC dbo.usp_AddColumnComment N'EmailConfig', N'IsDefault', N'是否默认配置';
END
GO

IF COL_LENGTH('dbo.EmailConfig', 'Enabled') IS NULL
BEGIN
    ALTER TABLE dbo.EmailConfig ADD Enabled BIT NOT NULL DEFAULT 1;
    EXEC dbo.usp_AddColumnComment N'EmailConfig', N'Enabled', N'是否启用';
END
GO

IF COL_LENGTH('dbo.EmailConfig', 'CreateTime') IS NULL
BEGIN
    ALTER TABLE dbo.EmailConfig ADD CreateTime DATETIME NOT NULL DEFAULT GETDATE();
    EXEC dbo.usp_AddColumnComment N'EmailConfig', N'CreateTime', N'创建时间';
END
GO

-- 给已有配置回填名称
UPDATE dbo.EmailConfig SET Name = Account WHERE Name = N'';
GO

-- 如果没有默认配置，标记最新一条为默认
IF NOT EXISTS (SELECT 1 FROM dbo.EmailConfig WHERE IsDefault = 1)
    UPDATE dbo.EmailConfig SET IsDefault = 1
    WHERE Id = (SELECT TOP 1 Id FROM dbo.EmailConfig ORDER BY Id DESC);
GO

-- ========== 3. SysWebhookLog 发送日志表 ==========
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

-- ========== 4. 菜单重组 ==========

-- 4.1 删除短信发送任务菜单(26) 和邮件定时任务菜单(32)
DELETE FROM dbo.SysRoleMenu WHERE MenuId IN (26, 32);
DELETE FROM dbo.SysMenu WHERE Id IN (26, 32);
GO

-- 4.2 新建一级菜单「机器人」
IF NOT EXISTS (SELECT 1 FROM dbo.SysMenu WHERE Id = 44)
BEGIN
    SET IDENTITY_INSERT dbo.SysMenu ON;
    INSERT INTO dbo.SysMenu (Id, ParentId, Title, Page, IsFloat, Visible, IsExternal, Editable, Name, Component, SortOrder)
    VALUES (44, NULL, N'机器人', NULL, 0, 1, 0, 1, NULL, NULL, 9);
    SET IDENTITY_INSERT dbo.SysMenu OFF;
END
GO

-- 4.3 移动群机器人菜单(38) 从系统管理(34) 到机器人(44)
UPDATE dbo.SysMenu SET ParentId = 44, SortOrder = 1 WHERE Id = 38 AND ParentId = 34;
GO

-- 4.4 新增子菜单「发送日志」
IF NOT EXISTS (SELECT 1 FROM dbo.SysMenu WHERE Id = 45)
BEGIN
    SET IDENTITY_INSERT dbo.SysMenu ON;
    INSERT INTO dbo.SysMenu (Id, ParentId, Title, Page, IsFloat, Visible, IsExternal, Editable, Name, Component, SortOrder)
    VALUES (45, 44, N'发送日志', N'/webhook-log', 0, 1, 0, 1, N'webhook-log', N'/src/notify/views/WebhookLogView.vue', 2);
    SET IDENTITY_INSERT dbo.SysMenu OFF;
END
GO

-- ========== 5. 角色-菜单权限 ==========

-- 5.1 admin 角色补齐新菜单（44 机器人、45 发送日志）
INSERT INTO dbo.SysRoleMenu (RoleId, MenuId)
SELECT r.Id, m.Id
FROM dbo.SysRole r CROSS JOIN dbo.SysMenu m
WHERE r.Code = N'admin'
  AND m.Id IN (44, 45)
  AND NOT EXISTS (SELECT 1 FROM dbo.SysRoleMenu rm WHERE rm.RoleId = r.Id AND rm.MenuId = m.Id);
GO

-- 5.2 普通用户角色授予发送日志权限（父级菜单由 MenuService 自动回溯补齐）
INSERT INTO dbo.SysRoleMenu (RoleId, MenuId)
SELECT r.Id, m.Id
FROM dbo.SysRole r CROSS JOIN dbo.SysMenu m
WHERE r.Code = N'user'
  AND m.Id = 45
  AND NOT EXISTS (SELECT 1 FROM dbo.SysRoleMenu rm WHERE rm.RoleId = r.Id AND rm.MenuId = m.Id);
GO

-- 给群机器人配置表补充 IsDefault 列（默认机器人开关）。
-- 自动推送（通知联动、邮件任务推送、任务失败告警）只发给 IsDefault=1 的配置。
IF COL_LENGTH('dbo.SysWebhookConfig', 'IsDefault') IS NULL
BEGIN
    ALTER TABLE dbo.SysWebhookConfig ADD IsDefault BIT NOT NULL DEFAULT 0;
    EXEC dbo.usp_AddColumnComment N'SysWebhookConfig', N'IsDefault', N'是否为默认机器人（自动推送只发给默认配置）';
END
GO

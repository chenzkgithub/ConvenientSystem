-- 给群机器人配置表补充 UseCard 列（富文本卡片开关）。
-- 早期建库的 SysWebhookConfig 可能缺失此列（init.sql 已包含，此处幂等补齐）。
IF COL_LENGTH('dbo.SysWebhookConfig', 'UseCard') IS NULL
BEGIN
    ALTER TABLE dbo.SysWebhookConfig ADD UseCard BIT NOT NULL DEFAULT 0;
    EXEC dbo.usp_AddColumnComment N'SysWebhookConfig', N'UseCard', N'是否使用富文本卡片消息（仅群机器人生效）';
END
GO

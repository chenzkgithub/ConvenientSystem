-- 010_add_session_timeout_config.sql
-- 新增系统配置：用户会话超时时间（分钟），0 表示不自动退出

IF NOT EXISTS (SELECT 1 FROM dbo.SysConfig WHERE ConfigKey = N'Security.SessionTimeoutMinutes')
BEGIN
    INSERT INTO dbo.SysConfig (ConfigKey, ConfigValue, Category, DisplayName, Description, InputType, TabGroup, SortOrder)
    VALUES (N'Security.SessionTimeoutMinutes', N'30', N'系统安全', N'会话超时时间（分钟）', N'用户多久无操作后自动退出登录，0 表示不自动退出', N'number', N'system', 2);
END
GO

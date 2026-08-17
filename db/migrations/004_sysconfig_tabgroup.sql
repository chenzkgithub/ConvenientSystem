/* =============================================================
   004_sysconfig_tabgroup.sql
   ---------------------------------------------------------------
   SysConfig 表新增 TabGroup 列（页签分组：system/thirdparty），
   百度翻译配置归入 thirdparty，其余默认 system。
   幂等可重复执行。
   ============================================================= */

USE [ConvenientSystem];
GO

-- 补充 TabGroup 列
IF NOT EXISTS (
    SELECT 1 FROM sys.columns c
    JOIN sys.tables t ON c.object_id = t.object_id
    WHERE t.name = N'SysConfig' AND c.name = N'TabGroup'
)
BEGIN
    ALTER TABLE dbo.SysConfig ADD TabGroup NVARCHAR(20) NOT NULL DEFAULT N'system';
END
GO

-- 列注释
IF NOT EXISTS (
    SELECT 1 FROM sys.extended_properties ep
    JOIN sys.columns c ON ep.major_id = c.object_id AND ep.minor_id = c.column_id
    WHERE ep.name = N'MS_Description' AND ep.class = 1
      AND OBJECT_NAME(ep.major_id) = N'SysConfig' AND c.name = N'TabGroup'
)
    EXEC sys.sp_addextendedproperty
        @name = N'MS_Description', @value = N'页签分组：system系统配置/thirdparty第三方配置',
        @level0type = N'SCHEMA', @level0name = N'dbo',
        @level1type = N'TABLE',  @level1name = N'SysConfig',
        @level2type = N'COLUMN', @level2name = N'TabGroup';
GO

-- 百度翻译配置归入第三方配置页签
UPDATE dbo.SysConfig SET TabGroup = N'thirdparty' WHERE ConfigKey LIKE N'BaiduTranslate%';
GO

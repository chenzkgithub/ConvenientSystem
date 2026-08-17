-- ============================================================================
-- Drop legacy CreatedBy (account snapshot) columns; creator display now joins
-- SysUser via CreatedById. Idempotent: safe to re-run.
--   1) EmailLog: add CreatedById, backfill by account, drop CreatedBy
--   2) EmailTask / SmsTask / SmsTemplate / SysSqlSnippet / SysDataSource:
--      drop CreatedBy (CreatedById already backfilled by previous migration)
-- Run: sqlcmd -S "(localdb)\MSSQLLocalDB" -d ConvenientSystem -i <this file>
-- ============================================================================
SET XACT_ABORT ON;
GO

-- 1. EmailLog
IF COL_LENGTH(N'dbo.EmailLog', N'CreatedById') IS NULL
    ALTER TABLE dbo.EmailLog ADD CreatedById UNIQUEIDENTIFIER NULL;
GO
UPDATE l SET l.CreatedById = u.Id
FROM dbo.EmailLog l
JOIN dbo.SysUser u ON u.Account = l.CreatedBy
WHERE l.CreatedById IS NULL AND l.CreatedBy IS NOT NULL AND l.CreatedBy <> N'system';
GO
IF COL_LENGTH(N'dbo.EmailLog', N'CreatedBy') IS NOT NULL
    ALTER TABLE dbo.EmailLog DROP COLUMN CreatedBy;
GO

-- 2. business tables (CreatedById already mapped)
IF COL_LENGTH(N'dbo.EmailTask', N'CreatedBy') IS NOT NULL
    ALTER TABLE dbo.EmailTask DROP COLUMN CreatedBy;
GO
IF COL_LENGTH(N'dbo.SmsTask', N'CreatedBy') IS NOT NULL
    ALTER TABLE dbo.SmsTask DROP COLUMN CreatedBy;
GO
IF COL_LENGTH(N'dbo.SmsTemplate', N'CreatedBy') IS NOT NULL
    ALTER TABLE dbo.SmsTemplate DROP COLUMN CreatedBy;
GO
IF COL_LENGTH(N'dbo.SysSqlSnippet', N'CreatedBy') IS NOT NULL
    ALTER TABLE dbo.SysSqlSnippet DROP COLUMN CreatedBy;
GO
IF COL_LENGTH(N'dbo.SysDataSource', N'CreatedBy') IS NOT NULL
    ALTER TABLE dbo.SysDataSource DROP COLUMN CreatedBy;
GO

-- verify
SELECT OBJECT_NAME(object_id) AS tbl, name, TYPE_NAME(system_type_id) AS typ
FROM sys.columns
WHERE OBJECT_NAME(object_id) IN ('EmailTask','SmsTask','SmsTemplate','SysSqlSnippet','SysDataSource','EmailLog')
  AND name IN ('CreatedBy','CreatedById')
ORDER BY tbl, name;
GO
PRINT 'drop-createdby migration done';
GO

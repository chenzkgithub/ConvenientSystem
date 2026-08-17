-- ============================================================================
-- 用户 Id 迁移脚本：SysUser.Id 由 INT IDENTITY 改为 UNIQUEIDENTIFIER（顺序 GUID），
-- 所有数据权限关联由"账号字符串"改为"用户 Id"：
--   1) SysUser.Id        INT -> UNIQUEIDENTIFIER（存量行分配 NEWID，应用层后续生成顺序 GUID）
--   2) SysUserRole.UserId / LotteryRecord.UserId / SysAuditLog.UserId 跟随映射
--   3) SysErrorLog 新增 UserId 并按 Account 映射
--   4) EmailTask / SmsTask / SmsTemplate / SysSqlSnippet / SysDataSource
--      新增 CreatedById 并按 CreatedBy(账号) 映射到 SysUser.Id
-- 脚本幂等：可重复执行；已完成的步骤自动跳过。
-- 执行：sqlcmd -S "(localdb)\MSSQLLocalDB" -d ConvenientSystem -i db\migrate-user-guid.sql
-- ============================================================================
SET XACT_ABORT ON;
GO

-- ----------------------------------------------------------------------------
-- 1. SysUser 增加临时 GUID 列并赋值
-- ----------------------------------------------------------------------------
IF COL_LENGTH(N'dbo.SysUser', N'NewId') IS NULL
    ALTER TABLE dbo.SysUser ADD NewId UNIQUEIDENTIFIER NULL;
GO
UPDATE dbo.SysUser SET NewId = NEWID() WHERE NewId IS NULL;
GO

-- ----------------------------------------------------------------------------
-- 2. SysUserRole.UserId 映射（先映射，再换列）
-- ----------------------------------------------------------------------------
IF COL_LENGTH(N'dbo.SysUserRole', N'NewUserId') IS NULL
    ALTER TABLE dbo.SysUserRole ADD NewUserId UNIQUEIDENTIFIER NULL;
GO
UPDATE ur SET ur.NewUserId = u.NewId
FROM dbo.SysUserRole ur
JOIN dbo.SysUser u ON u.Id = ur.UserId
WHERE ur.NewUserId IS NULL;
GO
IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UQ_SysUserRole')
    ALTER TABLE dbo.SysUserRole DROP CONSTRAINT UQ_SysUserRole;
GO
-- 旧 UserId 仍为 INT 时执行换列
IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID(N'dbo.SysUserRole') AND name = N'UserId' AND system_type_id = 56)
BEGIN
    ALTER TABLE dbo.SysUserRole DROP COLUMN UserId;
    EXEC sp_rename N'dbo.SysUserRole.NewUserId', N'UserId', N'COLUMN';
    ALTER TABLE dbo.SysUserRole ALTER COLUMN UserId UNIQUEIDENTIFIER NOT NULL;
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UQ_SysUserRole')
    ALTER TABLE dbo.SysUserRole ADD CONSTRAINT UQ_SysUserRole UNIQUE (UserId, RoleId);
GO
-- 仅当换列成功（UserId 已是 uniqueidentifier）后才清理临时列，避免映射数据丢失
IF COL_LENGTH(N'dbo.SysUserRole', N'NewUserId') IS NOT NULL
   AND EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID(N'dbo.SysUserRole') AND name = N'UserId' AND system_type_id = 36)
    ALTER TABLE dbo.SysUserRole DROP COLUMN NewUserId;
GO

-- ----------------------------------------------------------------------------
-- 3. LotteryRecord.UserId 映射（先删含 UserId 的索引）
-- ----------------------------------------------------------------------------
IF COL_LENGTH(N'dbo.LotteryRecord', N'NewUserId') IS NULL
    ALTER TABLE dbo.LotteryRecord ADD NewUserId UNIQUEIDENTIFIER NULL;
GO
UPDATE r SET r.NewUserId = u.NewId
FROM dbo.LotteryRecord r
JOIN dbo.SysUser u ON u.Id = r.UserId
WHERE r.NewUserId IS NULL;
GO
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_LotteryRecord_UserId_CreatedAt')
    DROP INDEX IX_LotteryRecord_UserId_CreatedAt ON dbo.LotteryRecord;
GO
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_LotteryRecord_Type_User_CreatedAt')
    DROP INDEX IX_LotteryRecord_Type_User_CreatedAt ON dbo.LotteryRecord;
GO
IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID(N'dbo.LotteryRecord') AND name = N'UserId' AND system_type_id = 56)
BEGIN
    ALTER TABLE dbo.LotteryRecord DROP COLUMN UserId;
    EXEC sp_rename N'dbo.LotteryRecord.NewUserId', N'UserId', N'COLUMN';
    ALTER TABLE dbo.LotteryRecord ALTER COLUMN UserId UNIQUEIDENTIFIER NOT NULL;
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_LotteryRecord_Type_User_CreatedAt')
    CREATE INDEX IX_LotteryRecord_Type_User_CreatedAt
        ON dbo.LotteryRecord(LotteryType, UserId, CreatedAt DESC);
GO
IF COL_LENGTH(N'dbo.LotteryRecord', N'NewUserId') IS NOT NULL
   AND EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID(N'dbo.LotteryRecord') AND name = N'UserId' AND system_type_id = 36)
    ALTER TABLE dbo.LotteryRecord DROP COLUMN NewUserId;
GO

-- ----------------------------------------------------------------------------
-- 4. SysAuditLog.UserId 映射（允许 NULL，历史匿名记录保持 NULL）
-- ----------------------------------------------------------------------------
IF COL_LENGTH(N'dbo.SysAuditLog', N'NewUserId') IS NULL
    ALTER TABLE dbo.SysAuditLog ADD NewUserId UNIQUEIDENTIFIER NULL;
GO
UPDATE l SET l.NewUserId = u.NewId
FROM dbo.SysAuditLog l
JOIN dbo.SysUser u ON u.Id = l.UserId
WHERE l.NewUserId IS NULL AND l.UserId IS NOT NULL;
GO
IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID(N'dbo.SysAuditLog') AND name = N'UserId' AND system_type_id = 56)
BEGIN
    ALTER TABLE dbo.SysAuditLog DROP COLUMN UserId;
    EXEC sp_rename N'dbo.SysAuditLog.NewUserId', N'UserId', N'COLUMN';
END
GO
IF COL_LENGTH(N'dbo.SysAuditLog', N'NewUserId') IS NOT NULL
   AND EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID(N'dbo.SysAuditLog') AND name = N'UserId' AND system_type_id = 36)
    ALTER TABLE dbo.SysAuditLog DROP COLUMN NewUserId;
GO

-- ----------------------------------------------------------------------------
-- 5. SysErrorLog 新增 UserId 并按账号映射（历史数据尽力匹配）
-- ----------------------------------------------------------------------------
IF COL_LENGTH(N'dbo.SysErrorLog', N'UserId') IS NULL
    ALTER TABLE dbo.SysErrorLog ADD UserId UNIQUEIDENTIFIER NULL;
GO
UPDATE l SET l.UserId = u.Id
FROM dbo.SysErrorLog l
JOIN dbo.SysUser u ON u.Account = l.Account
WHERE l.UserId IS NULL AND l.Account <> N'';
GO

-- ----------------------------------------------------------------------------
-- 6. SysUser 换主键：删旧 PK 与旧 INT Id，启用 GUID Id
-- ----------------------------------------------------------------------------
DECLARE @pk NVARCHAR(256);
SELECT @pk = name FROM sys.key_constraints
WHERE parent_object_id = OBJECT_ID(N'dbo.SysUser') AND type = N'PK';
-- 仅当主键仍落在 INT 型 Id 列上时才删旧 PK（重跑时新 PK 已在新 Id 上，跳过）
IF @pk IS NOT NULL AND EXISTS (SELECT 1 FROM sys.columns
                               WHERE object_id = OBJECT_ID(N'dbo.SysUser') AND name = N'Id' AND system_type_id = 56)
    EXEC(N'ALTER TABLE dbo.SysUser DROP CONSTRAINT [' + @pk + N'];');
GO
IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID(N'dbo.SysUser') AND name = N'Id' AND system_type_id = 56)
BEGIN
    ALTER TABLE dbo.SysUser DROP COLUMN Id;
    EXEC sp_rename N'dbo.SysUser.NewId', N'Id', N'COLUMN';
    ALTER TABLE dbo.SysUser ALTER COLUMN Id UNIQUEIDENTIFIER NOT NULL;
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints
               WHERE parent_object_id = OBJECT_ID(N'dbo.SysUser') AND type = N'PK')
    ALTER TABLE dbo.SysUser ADD CONSTRAINT PK_SysUser PRIMARY KEY (Id);
GO
IF COL_LENGTH(N'dbo.SysUser', N'NewId') IS NOT NULL
   AND EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID(N'dbo.SysUser') AND name = N'Id' AND system_type_id = 36)
    ALTER TABLE dbo.SysUser DROP COLUMN NewId;
GO

-- ----------------------------------------------------------------------------
-- 7. 业务表新增 CreatedById 并按 CreatedBy(账号) 映射到 SysUser.Id
-- ----------------------------------------------------------------------------
IF COL_LENGTH(N'dbo.EmailTask', N'CreatedById') IS NULL
    ALTER TABLE dbo.EmailTask ADD CreatedById UNIQUEIDENTIFIER NULL;
GO
UPDATE t SET t.CreatedById = u.Id
FROM dbo.EmailTask t
JOIN dbo.SysUser u ON u.Account = t.CreatedBy
WHERE t.CreatedById IS NULL AND t.CreatedBy IS NOT NULL;
GO

IF COL_LENGTH(N'dbo.SmsTask', N'CreatedById') IS NULL
    ALTER TABLE dbo.SmsTask ADD CreatedById UNIQUEIDENTIFIER NULL;
GO
UPDATE t SET t.CreatedById = u.Id
FROM dbo.SmsTask t
JOIN dbo.SysUser u ON u.Account = t.CreatedBy
WHERE t.CreatedById IS NULL AND t.CreatedBy IS NOT NULL;
GO

IF COL_LENGTH(N'dbo.SmsTemplate', N'CreatedById') IS NULL
    ALTER TABLE dbo.SmsTemplate ADD CreatedById UNIQUEIDENTIFIER NULL;
GO
UPDATE t SET t.CreatedById = u.Id
FROM dbo.SmsTemplate t
JOIN dbo.SysUser u ON u.Account = t.CreatedBy
WHERE t.CreatedById IS NULL AND t.CreatedBy IS NOT NULL;
GO

IF COL_LENGTH(N'dbo.SysSqlSnippet', N'CreatedById') IS NULL
    ALTER TABLE dbo.SysSqlSnippet ADD CreatedById UNIQUEIDENTIFIER NULL;
GO
UPDATE s SET s.CreatedById = u.Id
FROM dbo.SysSqlSnippet s
JOIN dbo.SysUser u ON u.Account = s.CreatedBy
WHERE s.CreatedById IS NULL AND s.CreatedBy IS NOT NULL;
GO

IF COL_LENGTH(N'dbo.SysDataSource', N'CreatedById') IS NULL
    ALTER TABLE dbo.SysDataSource ADD CreatedById UNIQUEIDENTIFIER NULL;
GO
UPDATE d SET d.CreatedById = u.Id
FROM dbo.SysDataSource d
JOIN dbo.SysUser u ON u.Account = d.CreatedBy
WHERE d.CreatedById IS NULL AND d.CreatedBy IS NOT NULL;
GO

PRINT N'user id guid migration done';
GO

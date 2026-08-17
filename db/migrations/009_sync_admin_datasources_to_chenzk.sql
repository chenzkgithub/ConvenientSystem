-- 迁移 009：将管理员创建的数据源同步给 chenzk 账号
-- 1. 把 SysDataSource 的名称唯一约束从全局改为按创建人隔离（Name + CreatedById）
-- 2. 复制 admin 账号创建的数据源到 chenzk 账号

USE ConvenientSystem;
GO

-- 删除旧的全局唯一约束（如果存在）
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UQ_SysDataSource_Name' AND object_id = OBJECT_ID(N'dbo.SysDataSource'))
BEGIN
    ALTER TABLE dbo.SysDataSource DROP CONSTRAINT UQ_SysDataSource_Name;
END
GO

-- 添加按创建人隔离的唯一约束（如果尚未存在）
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UQ_SysDataSource_Name_CreatedById' AND object_id = OBJECT_ID(N'dbo.SysDataSource'))
BEGIN
    ALTER TABLE dbo.SysDataSource ADD CONSTRAINT UQ_SysDataSource_Name_CreatedById UNIQUE (Name, CreatedById);
END
GO

-- 复制 admin 创建的数据源到 chenzk（跳过已存在的同名记录）
DECLARE @AdminId UNIQUEIDENTIFIER = (SELECT TOP 1 Id FROM dbo.SysUser WHERE Account = N'admin');
DECLARE @ChenzkId UNIQUEIDENTIFIER = (SELECT TOP 1 Id FROM dbo.SysUser WHERE Account = N'chenzk');

IF @AdminId IS NOT NULL AND @ChenzkId IS NOT NULL
BEGIN
    INSERT INTO dbo.SysDataSource (Name, ConnectionString, DbType, CreatedById)
    SELECT d.Name, d.ConnectionString, d.DbType, @ChenzkId
    FROM dbo.SysDataSource d
    WHERE d.CreatedById = @AdminId
      AND NOT EXISTS (
          SELECT 1 FROM dbo.SysDataSource d2
          WHERE d2.Name = d.Name AND d2.CreatedById = @ChenzkId
      );
END
GO

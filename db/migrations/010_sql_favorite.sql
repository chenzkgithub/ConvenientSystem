-- SQL 查询收藏表
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[SysSqlFavorite]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[SysSqlFavorite] (
        [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [Name] NVARCHAR(100) NOT NULL,
        [SqlContent] NVARCHAR(MAX) NOT NULL,
        [Remark] NVARCHAR(500) NULL,
        [DataSource] NVARCHAR(100) NULL,
        [SortOrder] INT NOT NULL DEFAULT 0,
        [CreateTime] DATETIME NOT NULL DEFAULT GETDATE(),
        [CreatedById] UNIQUEIDENTIFIER NULL
    );
END
GO

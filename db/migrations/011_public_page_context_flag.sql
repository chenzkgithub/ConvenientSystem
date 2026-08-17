/* =============================================================
   011_public_page_context_flag.sql
   ---------------------------------------------------------------
   外部公开页面改用 public=1 显式标记免登录上下文：
   - standalone=1 退回纯粹的"纯净窗口呈现"语义（内部独立窗口仍需登录）
   - public=1 标记免登录公开上下文（前端不发送 JWT）
   两者正交，公开访问链接只需 public=1，形如 /#/lottery-trend?public=1。

   本脚本仅刷新 SysPublicPage 表注释以对齐新口径，
   不改动 PageKey 等业务数据（新方案与路由路径无关）。
   幂等可重复执行。
   ============================================================= */

USE [ConvenientSystem];
GO

DECLARE @NewDesc NVARCHAR(500) = N'外部公开页面表（免登录页面配置管理，访问链接带 public=1）';

IF EXISTS (
    SELECT 1 FROM sys.extended_properties ep
    JOIN sys.objects o ON ep.major_id = o.object_id
    WHERE ep.name = N'MS_Description' AND ep.class = 1 AND ep.minor_id = 0
      AND o.name = N'SysPublicPage' AND SCHEMA_NAME(o.schema_id) = N'dbo'
)
    EXEC sys.sp_updateextendedproperty
        @name = N'MS_Description', @value = @NewDesc,
        @level0type = N'SCHEMA', @level0name = N'dbo',
        @level1type = N'TABLE',  @level1name = N'SysPublicPage';
ELSE
    EXEC sys.sp_addextendedproperty
        @name = N'MS_Description', @value = @NewDesc,
        @level0type = N'SCHEMA', @level0name = N'dbo',
        @level1type = N'TABLE',  @level1name = N'SysPublicPage';
GO

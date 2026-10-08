/* =============================================================
   ConvenientSystem 手机端数据库初始化脚本（手机端专属对象唯一维护入口）
   ---------------------------------------------------------------
   - 本文件维护手机端专属的数据库对象与初始数据，幂等可重复执行；
     当前范围：SysUserAppPerm 手机端权限白名单表、ChatFriendship/ChatFriendRequest
     好友体系两表、存量单聊自动互加；
   - PC 端/两端共用对象（含 Chat 会话/成员/消息/屏蔽/转发表及其老库补列）维护在 db/init.sql；
   - 执行顺序：必须在 db/init.sql 之后执行（本文件依赖其中的库、ChatConversation 表
     与 usp_AddTableComment/usp_AddColumnComment 存储过程）；
   - 以后每次手机端需求涉及到的表结构变更、初始数据、存量数据迁移都追加/修改在本文件，
     严禁按需求另建 migrate 迁移脚本；
   - 部署链路：deploy.yml 打包时把 init.sql 与本文件顺序拼接为部署包内的 db/init.sql
     （服务器 compose 仍只挂载单文件，docker/init-db.sh 逻辑不变），
     老库随部署自动补齐；本地手动执行方式（Windows 认证，接在 init.sql 之后）：
       sqlcmd -S (localdb)\MSSQLLocalDB -E -i db\init-app.sql -f 65001 -I
   ============================================================= */

USE [ConvenientSystem];
GO

-- 过滤唯一索引要求会话 QUOTED_IDENTIFIER ON（sqlcmd 默认 OFF 会报 Msg 1934）；
-- init.sql 已开，本文件被拼接进部署链路时同会话自然继承，独立执行时在此再开一次
SET QUOTED_IDENTIFIER ON;
GO

-- ========== 1. 手机端权限（SysUserAppPerm 白名单：一行=一个用户的一个权限点，无行=无权限；与 PC 端角色/菜单权限完全独立） ==========
IF OBJECT_ID(N'dbo.SysUserAppPerm') IS NULL
BEGIN
    CREATE TABLE dbo.SysUserAppPerm (
        UserId        UNIQUEIDENTIFIER NOT NULL,                    -- 用户 Id（GUID，关联 SysUser.Id）
        PermCode      NVARCHAR(50)     NOT NULL,                    -- 权限点码：app-login/app-chat/app-friends/app-notice/app-dashboard/app-lottery/app-moments/app-favorites/app-permission-manage/app-user-manage/app-role-manage（字典见后端 AppPerm.Catalog，DB 不存字典）
        CreateTime    DATETIME2        NOT NULL DEFAULT GETDATE(),  -- 开通时间
        CreatedById   UNIQUEIDENTIFIER NULL,                       -- 开通人 Id（审计：谁开的；自助注册时为本人）
        CONSTRAINT PK_SysUserAppPerm PRIMARY KEY (UserId, PermCode)
    );
END
GO

EXEC dbo.usp_AddTableComment N'SysUserAppPerm', N'手机端权限授权（白名单行式存储：无行=无权限；与 PC 端角色/菜单权限完全独立）';
EXEC dbo.usp_AddColumnComment N'SysUserAppPerm', N'UserId',      N'用户 Id（GUID，关联 SysUser.Id）';
EXEC dbo.usp_AddColumnComment N'SysUserAppPerm', N'PermCode',    N'权限点码（app-login=登录手机端/app-chat=消息/app-friends=好友/app-notice=通知/app-dashboard=仪表盘/app-lottery=彩票/app-moments=朋友圈/app-favorites=收藏/app-permission-manage=权限管理/app-user-manage=用户管理/app-role-manage=角色管理；管理类默认仅 admin 恒有，其他账号需在权限管理页开通）';
EXEC dbo.usp_AddColumnComment N'SysUserAppPerm', N'CreateTime',  N'开通时间';
EXEC dbo.usp_AddColumnComment N'SysUserAppPerm', N'CreatedById', N'开通人 Id（审计：谁开的；自助注册时为本人）';
GO

-- 存量用户预置手机端基础四项（登录/消息/好友/通知）：仅对“从未配置过手机端权限”的用户执行，幂等且不影响后续手动调整
-- 内置 admin 不依赖本表（恒全权限，见后端 AppPermService）
INSERT INTO dbo.SysUserAppPerm (UserId, PermCode, CreatedById)
SELECT u.Id, p.PermCode, u.Id
FROM dbo.SysUser u
CROSS JOIN (VALUES (N'app-login'), (N'app-chat'), (N'app-friends'), (N'app-notice')) AS p(PermCode)
WHERE u.IsDeleted = 0
  AND NOT EXISTS (SELECT 1 FROM dbo.SysUserAppPerm x WHERE x.UserId = u.Id);
GO

-- ========== 2. 好友关系表（双行模式：A/B 成为好友即成对插 A→B 与 B→A 两行，事务保证成对） ==========
IF OBJECT_ID(N'dbo.ChatFriendship') IS NULL
BEGIN
    CREATE TABLE dbo.ChatFriendship (
        Id         BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        UserId     UNIQUEIDENTIFIER    NOT NULL,                    -- 关系持有方（GUID，关联 SysUser.Id）
        FriendId   UNIQUEIDENTIFIER    NOT NULL,                    -- 好友用户 Id（GUID，关联 SysUser.Id）
        Remark     NVARCHAR(100)       NULL,                        -- 我给对方的备注名（双行模式各存各的，空=未设置）
        CreateTime DATETIME2           NOT NULL DEFAULT GETDATE(),
        CONSTRAINT UQ_ChatFriendship UNIQUE (UserId, FriendId)
    );
END
GO

-- 2.0.1 好友备注列（老库幂等补列；新库建表段已含）
IF COL_LENGTH(N'dbo.ChatFriendship', N'Remark') IS NULL
    ALTER TABLE dbo.ChatFriendship ADD Remark NVARCHAR(100) NULL;
GO

-- 2.1 好友关系查询索引（独立成段幂等补建：按好友 Id 反查持有方）
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ChatFriendship') AND name = N'IX_ChatFriendship_FriendId')
    CREATE INDEX IX_ChatFriendship_FriendId ON dbo.ChatFriendship(FriendId);
GO

EXEC dbo.usp_AddTableComment N'ChatFriendship', N'好友关系表（双行模式：A/B 成为好友即成对插 A→B 与 B→A 两行）';
EXEC dbo.usp_AddColumnComment N'ChatFriendship', N'Id',         N'主键';
EXEC dbo.usp_AddColumnComment N'ChatFriendship', N'UserId',     N'关系持有方（GUID，关联 SysUser.Id）';
EXEC dbo.usp_AddColumnComment N'ChatFriendship', N'FriendId',   N'好友用户 Id（GUID，关联 SysUser.Id）';
EXEC dbo.usp_AddColumnComment N'ChatFriendship', N'Remark',     N'我给对方的备注名（双行模式各存各的，空=未设置）';
EXEC dbo.usp_AddColumnComment N'ChatFriendship', N'CreateTime', N'成为好友时间';
GO

-- ========== 3. 好友申请表（From→To 方向明确；过滤唯一索引防同一对待处理申请重复提交） ==========
IF OBJECT_ID(N'dbo.ChatFriendRequest') IS NULL
BEGIN
    CREATE TABLE dbo.ChatFriendRequest (
        Id         BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        FromUserId UNIQUEIDENTIFIER    NOT NULL,                    -- 申请人（GUID，关联 SysUser.Id）
        ToUserId   UNIQUEIDENTIFIER    NOT NULL,                    -- 接收人（GUID，关联 SysUser.Id）
        [Message]  NVARCHAR(100)       NULL,                        -- 验证留言（可选，最长 100 字）
        Status     INT                 NOT NULL DEFAULT 0,          -- 0=待处理 1=已同意 2=已拒绝
        HandleTime DATETIME2           NULL,                        -- 处理时间（待处理为空）
        CreateTime DATETIME2           NOT NULL DEFAULT GETDATE()
    );
END
GO

-- 3.0 好友申请查询索引（独立成段幂等补建：接收人列表与防重校验按 To/From+Status 查询）
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ChatFriendRequest') AND name = N'IX_ChatFriendRequest_To')
    CREATE INDEX IX_ChatFriendRequest_To ON dbo.ChatFriendRequest(ToUserId, Status);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ChatFriendRequest') AND name = N'IX_ChatFriendRequest_From')
    CREATE INDEX IX_ChatFriendRequest_From ON dbo.ChatFriendRequest(FromUserId, Status);
GO

-- 3.1 待处理申请过滤唯一索引（独立成段幂等补建；同一对用户只允许一条待处理申请，
--     已同意/已拒绝的历史不限制，可再次申请）
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ChatFriendRequest') AND name = N'UQ_ChatFriendRequest_Pending')
    CREATE UNIQUE INDEX UQ_ChatFriendRequest_Pending ON dbo.ChatFriendRequest(FromUserId, ToUserId) WHERE Status = 0;
GO

EXEC dbo.usp_AddTableComment N'ChatFriendRequest', N'好友申请表（From→To 方向明确；0=待处理 1=已同意 2=已拒绝）';
EXEC dbo.usp_AddColumnComment N'ChatFriendRequest', N'Id',         N'主键';
EXEC dbo.usp_AddColumnComment N'ChatFriendRequest', N'FromUserId', N'申请人（GUID，关联 SysUser.Id）';
EXEC dbo.usp_AddColumnComment N'ChatFriendRequest', N'ToUserId',   N'接收人（GUID，关联 SysUser.Id）';
EXEC dbo.usp_AddColumnComment N'ChatFriendRequest', N'Message',    N'验证留言（可选，最长 100 字）';
EXEC dbo.usp_AddColumnComment N'ChatFriendRequest', N'Status',     N'状态：0=待处理 1=已同意 2=已拒绝';
EXEC dbo.usp_AddColumnComment N'ChatFriendRequest', N'HandleTime', N'处理时间（待处理为空）';
EXEC dbo.usp_AddColumnComment N'ChatFriendRequest', N'CreateTime', N'申请时间';
GO

-- ========== 4. 存量单聊双方自动互加为好友（幂等，随部署链路自动执行） ==========
--    好友制上线后 ChatService 要求单聊双方须为好友才能发消息/打开会话，
--    本段把存量单聊双方自动互加，保证升级后老会话可继续聊天；
--    UserKey 为两个 Guid 的 N 格式（32 位十六进制无连字符）排序后拼接（64 字符），
--    SQL Server 不能直接 CAST 32 位无连字符串，需手工补 8-4-4-4-12 连字符；
--    已有屏蔽关系的会话不互加（屏蔽优先于好友，解除屏蔽后由下次部署自动补加）。
--    依赖：ChatConversation.ConversationType 列（db/init.sql 建表段/老库补列段提供）。
IF COL_LENGTH(N'dbo.ChatConversation', N'ConversationType') IS NOT NULL
BEGIN
    DECLARE @pairs TABLE (U1 uniqueidentifier, U2 uniqueidentifier);

    INSERT INTO @pairs (U1, U2)
    SELECT
        CAST(SUBSTRING(c.UserKey,  1, 8) + '-' + SUBSTRING(c.UserKey,  9, 4) + '-' +
             SUBSTRING(c.UserKey, 13, 4) + '-' + SUBSTRING(c.UserKey, 17, 4) + '-' +
             SUBSTRING(c.UserKey, 21, 12) AS uniqueidentifier),
        CAST(SUBSTRING(c.UserKey, 33, 8) + '-' + SUBSTRING(c.UserKey, 41, 4) + '-' +
             SUBSTRING(c.UserKey, 45, 4) + '-' + SUBSTRING(c.UserKey, 49, 4) + '-' +
             SUBSTRING(c.UserKey, 53, 12) AS uniqueidentifier)
    FROM dbo.ChatConversation c
    WHERE c.ConversationType = 0
      AND LEN(c.UserKey) = 64
      AND NOT EXISTS (
          SELECT 1 FROM dbo.ChatBlockList b
          WHERE (b.UserId = CAST(SUBSTRING(c.UserKey,  1, 8) + '-' + SUBSTRING(c.UserKey,  9, 4) + '-' +
                                 SUBSTRING(c.UserKey, 13, 4) + '-' + SUBSTRING(c.UserKey, 17, 4) + '-' +
                                 SUBSTRING(c.UserKey, 21, 12) AS uniqueidentifier)
                 AND b.BlockedUserId = CAST(SUBSTRING(c.UserKey, 33, 8) + '-' + SUBSTRING(c.UserKey, 41, 4) + '-' +
                                            SUBSTRING(c.UserKey, 45, 4) + '-' + SUBSTRING(c.UserKey, 49, 4) + '-' +
                                            SUBSTRING(c.UserKey, 53, 12) AS uniqueidentifier))
              OR (b.UserId = CAST(SUBSTRING(c.UserKey, 33, 8) + '-' + SUBSTRING(c.UserKey, 41, 4) + '-' +
                                  SUBSTRING(c.UserKey, 45, 4) + '-' + SUBSTRING(c.UserKey, 49, 4) + '-' +
                                  SUBSTRING(c.UserKey, 53, 12) AS uniqueidentifier)
                  AND b.BlockedUserId = CAST(SUBSTRING(c.UserKey,  1, 8) + '-' + SUBSTRING(c.UserKey,  9, 4) + '-' +
                                             SUBSTRING(c.UserKey, 13, 4) + '-' + SUBSTRING(c.UserKey, 17, 4) + '-' +
                                             SUBSTRING(c.UserKey, 21, 12) AS uniqueidentifier))
      );

    -- 成对插入：U1→U2 与 U2→U1（各自 NOT EXISTS 幂等）
    INSERT INTO dbo.ChatFriendship (UserId, FriendId, CreateTime)
    SELECT p.U1, p.U2, GETDATE()
    FROM @pairs p
    WHERE NOT EXISTS (SELECT 1 FROM dbo.ChatFriendship f WHERE f.UserId = p.U1 AND f.FriendId = p.U2);

    INSERT INTO dbo.ChatFriendship (UserId, FriendId, CreateTime)
    SELECT p.U2, p.U1, GETDATE()
    FROM @pairs p
    WHERE NOT EXISTS (SELECT 1 FROM dbo.ChatFriendship f WHERE f.UserId = p.U2 AND f.FriendId = p.U1);
END
GO

-- ========== 5. 手机端用户设置表（账号级 KV：一行=一个用户的一个设置项；换设备登录后拉回实现设置跨设备同步） ==========
--    当前键：chatbg_default / chatbg_{conversationId}=聊天背景图（值为图片相对路径 yyyyMM/xxx.ext，上传通道 Common/Chat/UploadImage）、
--    theme_mode=主题（system/light/dark）、lottery_active_type=彩票页默认彩种；新增设置直接存新键，表结构不变
IF OBJECT_ID(N'dbo.AppUserSetting') IS NULL
BEGIN
    CREATE TABLE dbo.AppUserSetting (
        UserId       UNIQUEIDENTIFIER NOT NULL,                    -- 用户 Id（GUID，关联 SysUser.Id）
        SettingKey   NVARCHAR(50)     NOT NULL,                    -- 设置键（前端自治命名，限字母数字下划线中划线）
        SettingValue NVARCHAR(1000)   NOT NULL DEFAULT '',         -- 设置值（空串行由保存逻辑删除，不在表中留存）
        UpdateTime   DATETIME2        NOT NULL DEFAULT GETDATE(),  -- 最近保存时间
        CONSTRAINT PK_AppUserSetting PRIMARY KEY (UserId, SettingKey)
    );
END
GO

EXEC dbo.usp_AddTableComment N'AppUserSetting', N'手机端用户设置（账号级 KV：换设备登录后拉回实现设置跨设备同步；键由前端自治命名）';
EXEC dbo.usp_AddColumnComment N'AppUserSetting', N'UserId',       N'用户 Id（GUID，关联 SysUser.Id）';
EXEC dbo.usp_AddColumnComment N'AppUserSetting', N'SettingKey',   N'设置键（chatbg_default/chatbg_{会话Id}=聊天背景图路径、theme_mode=主题、lottery_active_type=默认彩种；限字母数字下划线中划线，≤50 字符）';
EXEC dbo.usp_AddColumnComment N'AppUserSetting', N'SettingValue', N'设置值（≤1000 字符；保存空串=删除该行即恢复默认）';
EXEC dbo.usp_AddColumnComment N'AppUserSetting', N'UpdateTime',   N'最近保存时间';
GO

PRINT N'ConvenientSystem 手机端数据库结构同步完成（SysUserAppPerm / 好友体系 / 存量互加 / 用户设置 KV）';
GO

-- ============================================================
-- 增量迁移：即时聊天模块（企业通讯录模式单聊）
-- 适用：已初始化的 ConvenientSystem 库（全新库直接跑 init.sql 即可，本脚本幂等可重复执行）
-- 新增 4 张表：ChatConversation / ChatConversationMember / ChatMessage / ChatBlockList
-- 依赖：dbo.usp_AddTableComment / dbo.usp_AddColumnComment（init.sql 已内置创建，幂等）
-- ============================================================
USE ConvenientSystem;
GO

-- 1. 会话表（单聊会话；双向归一 UserKey 唯一：两个用户 Guid 排序后拼接，A→B 与 B→A 同一会话）
IF OBJECT_ID(N'dbo.ChatConversation') IS NULL
BEGIN
    CREATE TABLE dbo.ChatConversation (
        Id              BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        UserKey         NVARCHAR(100)       NOT NULL,                    -- 双向归一键（排序后两个 Guid 拼接）
        LastMessageId   BIGINT              NOT NULL DEFAULT 0,          -- 最后一条消息 Id（0=尚无消息）
        LastSenderId    UNIQUEIDENTIFIER    NULL,                        -- 最后一条消息发送者（判未读：非我发送且 Id>我的水位）
        LastMessageTime DATETIME2           NULL,                        -- 最后一条消息时间
        LastMessageText NVARCHAR(200)       NULL,                        -- 最后一条消息预览（超长截断）
        CreateTime      DATETIME2           NOT NULL DEFAULT GETDATE(),
        CONSTRAINT UQ_ChatConversation_UserKey UNIQUE (UserKey)
    );
END
GO

EXEC dbo.usp_AddTableComment N'ChatConversation', N'聊天会话表（单聊，双向归一）';
EXEC dbo.usp_AddColumnComment N'ChatConversation', N'Id',              N'主键';
EXEC dbo.usp_AddColumnComment N'ChatConversation', N'UserKey',         N'双向归一键：两个用户 Guid 排序后拼接，A→B 与 B→A 同一会话';
EXEC dbo.usp_AddColumnComment N'ChatConversation', N'LastMessageId',   N'最后一条消息 Id（0=尚无消息）';
EXEC dbo.usp_AddColumnComment N'ChatConversation', N'LastSenderId',    N'最后一条消息发送者 Id（未读判断：非我发送且 Id > 我的已读水位）';
EXEC dbo.usp_AddColumnComment N'ChatConversation', N'LastMessageTime', N'最后一条消息时间';
EXEC dbo.usp_AddColumnComment N'ChatConversation', N'LastMessageText', N'最后一条消息预览（会话列表展示，超长截断）';
EXEC dbo.usp_AddColumnComment N'ChatConversation', N'CreateTime',      N'会话创建时间';
GO

-- 2. 会话成员表（每个会话两条成员记录：已读水位 / 免打扰 / 隐藏）
IF OBJECT_ID(N'dbo.ChatConversationMember') IS NULL
BEGIN
    CREATE TABLE dbo.ChatConversationMember (
        Id             BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        ConversationId BIGINT              NOT NULL,                    -- 关联 ChatConversation.Id
        UserId         UNIQUEIDENTIFIER    NOT NULL,                    -- 关联 SysUser.Id
        ReadMessageId  BIGINT              NOT NULL DEFAULT 0,          -- 已读水位：已读到的最后消息 Id
        Muted          BIT                 NOT NULL DEFAULT 0,          -- 免打扰（仍计未读，不提醒）
        Hidden         BIT                 NOT NULL DEFAULT 0,          -- 会话隐藏：删会话=隐藏，来新消息自动恢复
        CreateTime     DATETIME2           NOT NULL DEFAULT GETDATE(),
        CONSTRAINT UQ_ChatMember UNIQUE (ConversationId, UserId)
    );
    CREATE INDEX IX_ChatMember_UserId ON dbo.ChatConversationMember(UserId);
END
GO

EXEC dbo.usp_AddTableComment N'ChatConversationMember', N'聊天会话成员表（每会话两条，含已读水位/免打扰/隐藏）';
EXEC dbo.usp_AddColumnComment N'ChatConversationMember', N'Id',             N'主键';
EXEC dbo.usp_AddColumnComment N'ChatConversationMember', N'ConversationId', N'关联 ChatConversation.Id';
EXEC dbo.usp_AddColumnComment N'ChatConversationMember', N'UserId',         N'成员用户 Id（GUID，关联 SysUser.Id）';
EXEC dbo.usp_AddColumnComment N'ChatConversationMember', N'ReadMessageId',  N'已读水位：已读到的最后消息 Id（未读数=水位之后的消息数）';
EXEC dbo.usp_AddColumnComment N'ChatConversationMember', N'Muted',          N'免打扰（仍计未读，前端不提醒）';
EXEC dbo.usp_AddColumnComment N'ChatConversationMember', N'Hidden',         N'会话隐藏（删除会话即隐藏，收到新消息自动恢复显示）';
EXEC dbo.usp_AddColumnComment N'ChatConversationMember', N'CreateTime',     N'成员创建时间';
GO

-- 3. 消息表（按会话+Id 索引，倒序分页拉取历史）
IF OBJECT_ID(N'dbo.ChatMessage') IS NULL
BEGIN
    CREATE TABLE dbo.ChatMessage (
        Id             BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        ConversationId BIGINT              NOT NULL,                    -- 关联 ChatConversation.Id
        SenderId       UNIQUEIDENTIFIER    NOT NULL,                    -- 发送者（GUID，关联 SysUser.Id）
        Content        NVARCHAR(4000)      NOT NULL,                    -- 消息正文（纯文本）
        CreateTime     DATETIME2           NOT NULL DEFAULT GETDATE()
    );
    CREATE INDEX IX_ChatMessage_Conv ON dbo.ChatMessage(ConversationId, Id DESC);
END
GO

EXEC dbo.usp_AddTableComment N'ChatMessage', N'聊天消息表';
EXEC dbo.usp_AddColumnComment N'ChatMessage', N'Id',             N'主键（递增，兼作排序与已读水位）';
EXEC dbo.usp_AddColumnComment N'ChatMessage', N'ConversationId', N'关联 ChatConversation.Id';
EXEC dbo.usp_AddColumnComment N'ChatMessage', N'SenderId',       N'发送者用户 Id（GUID，关联 SysUser.Id）';
EXEC dbo.usp_AddColumnComment N'ChatMessage', N'Content',        N'消息正文（纯文本，超长截断）';
EXEC dbo.usp_AddColumnComment N'ChatMessage', N'CreateTime',     N'发送时间';
GO

-- 4. 屏蔽名单表（通讯录模式下以屏蔽代替好友关系控制；任一方屏蔽则双向拒收）
IF OBJECT_ID(N'dbo.ChatBlockList') IS NULL
BEGIN
    CREATE TABLE dbo.ChatBlockList (
        Id             BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        UserId         UNIQUEIDENTIFIER    NOT NULL,                    -- 屏蔽发起人（GUID，关联 SysUser.Id）
        BlockedUserId  UNIQUEIDENTIFIER    NOT NULL,                    -- 被屏蔽人（GUID，关联 SysUser.Id）
        CreateTime     DATETIME2           NOT NULL DEFAULT GETDATE(),
        CONSTRAINT UQ_ChatBlock UNIQUE (UserId, BlockedUserId)
    );
    CREATE INDEX IX_ChatBlock_Blocked ON dbo.ChatBlockList(BlockedUserId);
END
GO

EXEC dbo.usp_AddTableComment N'ChatBlockList', N'聊天屏蔽名单（任一方屏蔽则双向拒收消息）';
EXEC dbo.usp_AddColumnComment N'ChatBlockList', N'Id',            N'主键';
EXEC dbo.usp_AddColumnComment N'ChatBlockList', N'UserId',        N'屏蔽发起人 Id（GUID，关联 SysUser.Id）';
EXEC dbo.usp_AddColumnComment N'ChatBlockList', N'BlockedUserId', N'被屏蔽人 Id（GUID，关联 SysUser.Id）';
EXEC dbo.usp_AddColumnComment N'ChatBlockList', N'CreateTime',    N'屏蔽时间';
GO

PRINT N'聊天模块表结构迁移完成';
GO

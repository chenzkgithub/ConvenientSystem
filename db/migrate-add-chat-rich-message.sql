-- ============================================================
-- 增量迁移：聊天富消息（图片 / 引用 / 合并转发 + 单方面删除水位）
-- 适用：已执行过 migrate-add-chat.sql 的 ConvenientSystem 库（幂等可重复执行）
-- ChatMessage 加 5 列；ChatConversationMember 加 1 列；新增 ChatForwardRecord 表
-- 依赖：dbo.usp_AddTableComment / dbo.usp_AddColumnComment（init.sql 已内置创建，幂等）
-- ============================================================
USE ConvenientSystem;
GO

-- 1. 消息表扩展列（MsgType 类型 / 引用快照 / 合并转发卡片指向）
IF COL_LENGTH(N'dbo.ChatMessage', N'MsgType') IS NULL
    ALTER TABLE dbo.ChatMessage ADD MsgType INT NOT NULL DEFAULT 0;
GO
IF COL_LENGTH(N'dbo.ChatMessage', N'QuoteId') IS NULL
    ALTER TABLE dbo.ChatMessage ADD QuoteId BIGINT NOT NULL DEFAULT 0;
GO
IF COL_LENGTH(N'dbo.ChatMessage', N'QuoteText') IS NULL
    ALTER TABLE dbo.ChatMessage ADD QuoteText NVARCHAR(500) NULL;
GO
IF COL_LENGTH(N'dbo.ChatMessage', N'QuoteSenderId') IS NULL
    ALTER TABLE dbo.ChatMessage ADD QuoteSenderId UNIQUEIDENTIFIER NULL;
GO
IF COL_LENGTH(N'dbo.ChatMessage', N'RefRecordId') IS NULL
    ALTER TABLE dbo.ChatMessage ADD RefRecordId BIGINT NULL;
GO

EXEC dbo.usp_AddColumnComment N'ChatMessage', N'MsgType',       N'消息类型：0=文本 1=图片 2=合并转发记录卡片';
EXEC dbo.usp_AddColumnComment N'ChatMessage', N'QuoteId',       N'引用的原消息 Id（0=无引用）';
EXEC dbo.usp_AddColumnComment N'ChatMessage', N'QuoteText',     N'引用内容快照（发送时截取固化，原消息被单方面删除后引用块仍可显示）';
EXEC dbo.usp_AddColumnComment N'ChatMessage', N'QuoteSenderId', N'被引用消息的发送者 Id（引用块显示“xxx：”用）';
EXEC dbo.usp_AddColumnComment N'ChatMessage', N'RefRecordId',   N'合并转发卡片指向的 ChatForwardRecord.Id（MsgType=2 时有效）';
GO

-- 2. 会话成员表扩展列（单方面删除水位：仅我方视图过滤，对方不受影响）
IF COL_LENGTH(N'dbo.ChatConversationMember', N'ClearBeforeMessageId') IS NULL
    ALTER TABLE dbo.ChatConversationMember ADD ClearBeforeMessageId BIGINT NOT NULL DEFAULT 0;
GO

EXEC dbo.usp_AddColumnComment N'ChatConversationMember', N'ClearBeforeMessageId', N'单方面删除水位：仅我方视图不显示 Id <= 该值的消息（消息本体保留，对方不受影响）';
GO

-- 3. 合并转发记录表（消息快照固化为 JSON，原消息删除不影响已转发记录查看）
IF OBJECT_ID(N'dbo.ChatForwardRecord') IS NULL
BEGIN
    CREATE TABLE dbo.ChatForwardRecord (
        Id          BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        CreatorId   UNIQUEIDENTIFIER    NOT NULL,                    -- 创建者（转发发起人）
        Title       NVARCHAR(200)       NOT NULL,                    -- 卡片标题（如“张三和李四的聊天记录”）
        ContentJson NVARCHAR(MAX)       NOT NULL,                    -- 消息快照 JSON：[{senderName,msgType,content,time}]
        CreateTime  DATETIME2           NOT NULL DEFAULT GETDATE()
    );
    CREATE INDEX IX_ChatForwardRecord_Creator ON dbo.ChatForwardRecord(CreatorId);
END
GO

EXEC dbo.usp_AddTableComment N'ChatForwardRecord', N'合并转发记录表（快照固化，删除原消息不影响已转发记录）';
EXEC dbo.usp_AddColumnComment N'ChatForwardRecord', N'Id',          N'主键';
EXEC dbo.usp_AddColumnComment N'ChatForwardRecord', N'CreatorId',   N'创建者 Id（GUID，关联 SysUser.Id）';
EXEC dbo.usp_AddColumnComment N'ChatForwardRecord', N'Title',       N'卡片标题（如“张三和李四的聊天记录”）';
EXEC dbo.usp_AddColumnComment N'ChatForwardRecord', N'ContentJson', N'消息快照 JSON 数组（只读展示用，保留原发送者名）';
EXEC dbo.usp_AddColumnComment N'ChatForwardRecord', N'CreateTime',  N'转发时间';
GO

PRINT N'聊天富消息迁移完成（MsgType/引用/合并转发/单方面删除水位）';
GO

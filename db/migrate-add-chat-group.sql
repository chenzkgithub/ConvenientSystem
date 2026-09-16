-- ============================================================
-- 增量迁移：聊天群聊最小版本
-- 适用：已执行过 migrate-add-chat.sql / migrate-add-chat-rich-message.sql 的 ConvenientSystem 库（幂等可重复执行）
-- ChatConversation 加群聊字段；ChatConversationMember 加角色；ChatMessage 加 Mentions
-- 依赖：dbo.usp_AddTableComment / dbo.usp_AddColumnComment（init.sql 已内置创建，幂等）
-- ============================================================
USE ConvenientSystem;
GO

-- 1. 会话表扩展列（群聊类型 / 群名 / 群主 / 群头像）
IF COL_LENGTH(N'dbo.ChatConversation', N'ConversationType') IS NULL
    ALTER TABLE dbo.ChatConversation ADD ConversationType INT NOT NULL DEFAULT 0; -- 0=单聊 1=群聊
GO
IF COL_LENGTH(N'dbo.ChatConversation', N'GroupName') IS NULL
    ALTER TABLE dbo.ChatConversation ADD GroupName NVARCHAR(100) NULL;
GO
IF COL_LENGTH(N'dbo.ChatConversation', N'CreatorId') IS NULL
    ALTER TABLE dbo.ChatConversation ADD CreatorId UNIQUEIDENTIFIER NULL; -- 群聊必填
GO
IF COL_LENGTH(N'dbo.ChatConversation', N'Avatar') IS NULL
    ALTER TABLE dbo.ChatConversation ADD Avatar NVARCHAR(500) NULL;
GO

EXEC dbo.usp_AddColumnComment N'ChatConversation', N'ConversationType', N'会话类型：0=单聊 1=群聊';
EXEC dbo.usp_AddColumnComment N'ChatConversation', N'GroupName',        N'群聊名称（单聊为空）';
EXEC dbo.usp_AddColumnComment N'ChatConversation', N'CreatorId',        N'群聊创建者 Id（单聊为空）';
EXEC dbo.usp_AddColumnComment N'ChatConversation', N'Avatar',           N'群聊头像或单聊对方头像（data URL）';
GO

-- 2. 会话成员表扩展列（角色：0=成员 1=群主）
IF COL_LENGTH(N'dbo.ChatConversationMember', N'Role') IS NULL
    ALTER TABLE dbo.ChatConversationMember ADD Role INT NOT NULL DEFAULT 0;
GO

EXEC dbo.usp_AddColumnComment N'ChatConversationMember', N'Role', N'成员角色：0=成员 1=群主';
GO

-- 3. 消息表扩展列（@提及用户 Id 列表，JSON 数组）
IF COL_LENGTH(N'dbo.ChatMessage', N'Mentions') IS NULL
    ALTER TABLE dbo.ChatMessage ADD Mentions NVARCHAR(500) NULL;
GO

EXEC dbo.usp_AddColumnComment N'ChatMessage', N'Mentions', N'@提及用户 Id 列表（JSON 数组字符串，空表示无人被@）';
GO

PRINT N'聊天群聊迁移完成（ConversationType/GroupName/CreatorId/Avatar/Role/Mentions）';
GO

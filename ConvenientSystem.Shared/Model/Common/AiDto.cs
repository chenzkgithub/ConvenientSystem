namespace ConvenientSystem.Shared.Model.Common
{
    // ============================================================
    // AI 能力 P0：模型管理（系统配置页 AI 页签）+ 对话助手（全局抽屉）
    // 模型 CRUD 复用 sys-config:save 权限；对话能力经 ai-chat 视图权限点控制
    // ============================================================

    /// <summary>模型管理列表项（不回传明文 Key，仅 HasKey）</summary>
    public class AiModelListItemDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string BaseUrl { get; set; } = string.Empty;
        public string ModelId { get; set; } = string.Empty;
        public string Scenes { get; set; } = "chat";
        public bool IsDefault { get; set; }
        public bool Enabled { get; set; }
        public bool HasKey { get; set; }
        public int MaxContextChars { get; set; }
        public int SortOrder { get; set; }
    }

    /// <summary>新增/编辑模型（Id=0 新增；ApiKey 留空 = 编辑时保持原 Key 不变）</summary>
    public class AiModelSaveRequest
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string BaseUrl { get; set; } = string.Empty;
        public string? ApiKey { get; set; }
        public string ModelId { get; set; } = string.Empty;
        public int MaxContextChars { get; set; } = 60000;
        public bool Enabled { get; set; } = true;
    }

    /// <summary>模型连通性测试（保存草稿即可测：未保存的新增表单直接带值测试）</summary>
    public class AiModelTestRequest
    {
        /// <summary>已保存模型 Id（>0 且 ApiKey 为空时用库中已存 Key）</summary>
        public int Id { get; set; }
        public string BaseUrl { get; set; } = string.Empty;
        public string? ApiKey { get; set; }
        public string ModelId { get; set; } = string.Empty;
    }

    /// <summary>测试结果</summary>
    public class AiModelTestResult
    {
        public bool Ok { get; set; }
        public string Message { get; set; } = string.Empty;
        public int ElapsedMs { get; set; }
    }

    /// <summary>会话列表项</summary>
    public class AiConversationDto
    {
        public long Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public DateTime UpdateTime { get; set; }
    }

    /// <summary>消息（Status：0 生成中 / 1 完成 / 2 失败 / 3 已停止）</summary>
    public class AiMessageDto
    {
        public long Id { get; set; }
        public long ConversationId { get; set; }
        public string Role { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public int Status { get; set; }
        public int TokensIn { get; set; }
        public int TokensOut { get; set; }
        public string? Error { get; set; }
        public DateTime CreateTime { get; set; }
    }

    /// <summary>发送消息（ConversationId=0 新建会话）</summary>
    public class AiSendRequest
    {
        public long ConversationId { get; set; }
        public string Content { get; set; } = string.Empty;
    }

    /// <summary>发送结果：前端拿到 assistantMessageId 后监听流式 chunk / 轮询兜底</summary>
    public class AiSendResult
    {
        public long ConversationId { get; set; }
        public long UserMessageId { get; set; }
        public long AssistantMessageId { get; set; }
    }

    /// <summary>助手抽屉头部一次拉齐的状态（登录即可，无敏感数据）</summary>
    public class AiMyStatusDto
    {
        public bool Enabled { get; set; }

        /// <summary>今日已用请求次数</summary>
        public int RequestsUsed { get; set; }

        /// <summary>每日请求配额（0 = 不限制）</summary>
        public int RequestsQuota { get; set; }

        /// <summary>是否已有可用模型（管理员已配置并启用；false 时发送入口禁用）</summary>
        public bool ModelReady { get; set; }
    }

    /// <summary>
    /// 流式推送 chunk（SignalR AiResponseChunk 事件，也作为轮询兜底以外的唯一实时通道）：
    /// Done=false 时 Delta 为增量；Done=true 时 Content 为完整最终内容（前端直接覆盖 buffer）。
    /// </summary>
    public class AiChunkDto
    {
        public long ConversationId { get; set; }
        public long MessageId { get; set; }
        public string Delta { get; set; } = string.Empty;
        public bool Done { get; set; }
        public string? Error { get; set; }
        public string? Content { get; set; }

        /// <summary>工具调用中间态事件（生成过程逐个推送，前端按 CallId 更新工具横条；null = 普通文本增量/终态）</summary>
        public AiToolEventDto? ToolEvent { get; set; }
    }

    /// <summary>工具调用中间态事件（executing → done/error；result 摘要后端截 200 字符防大 payload）</summary>
    public class AiToolEventDto
    {
        public string CallId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        /// <summary>executing / done / error</summary>
        public string Status { get; set; } = "executing";
        public string? Result { get; set; }
        public string? Error { get; set; }
    }

    // ============================================================
    // Function Calling：工具调用相关类型
    // ============================================================

    /// <summary>工具定义（OpenAI tools 参数格式）</summary>
    public class AiToolDefinition
    {
        public string Type { get; set; } = "function";
        public AiToolFunction Function { get; set; } = new();
    }

    public class AiToolFunction
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public AiToolParameters Parameters { get; set; } = new();
    }

    public class AiToolParameters
    {
        public string Type { get; set; } = "object";
        public Dictionary<string, AiToolProperty> Properties { get; set; } = new();
        public List<string> Required { get; set; } = new();
    }

    public class AiToolProperty
    {
        public string Type { get; set; } = "string";
        public string? Description { get; set; }
    }

    /// <summary>AI 返回的工具调用请求</summary>
    public class AiToolCall
    {
        public string Id { get; set; } = string.Empty;
        public string Type { get; set; } = "function";
        public AiToolCallFunction Function { get; set; } = new();
    }

    public class AiToolCallFunction
    {
        public string Name { get; set; } = string.Empty;
        /// <summary>JSON 字符串参数</summary>
        public string Arguments { get; set; } = string.Empty;
    }

    /// <summary>工具执行结果（回传给 AI 继续生成）</summary>
    public class AiToolResult
    {
        public string ToolCallId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
    }

    /// <summary>任务/待办 DTO</summary>
    public class AiTaskDto
    {
        public long Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public DateTime? DueDate { get; set; }
        public int Status { get; set; }
        public DateTime CreateTime { get; set; }
    }

    // ============================================================
    // AI 辅助 SQL（NL2SQL）：SQL 查询工具的「AI 生成」能力
    // 准入复用 sql-query:execute 权限；生成结果只进编辑器不自动执行；
    // 配额复用 ai.quota.*（生成 1 次 = 1 次请求 + 实际 token，写 AiUsageDaily）
    // ============================================================

    /// <summary>AI 生成 SQL 请求（数据源/目标库用于在服务端组装表结构上下文）</summary>
    public class AiSqlGenerateRequest
    {
        /// <summary>数据源名称（SysDataSource.Name）</summary>
        public string DataSource { get; set; } = string.Empty;

        /// <summary>目标数据库（空 = 连接串默认库）</summary>
        public string Database { get; set; } = string.Empty;

        /// <summary>自然语言需求描述</summary>
        public string Prompt { get; set; } = string.Empty;
    }

    /// <summary>AI 生成 SQL 结果（纯 SQL，不含 markdown 代码块标记）</summary>
    public class AiSqlGenerateResult
    {
        public string Sql { get; set; } = string.Empty;
    }
}

using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Ai
{
    /// <summary>Provider 层的对话消息（system/user/assistant/tool；tool 系 Function Calling 工具结果）</summary>
    public class AiPromptMessage
    {
        public string Role { get; set; } = "user";
        public string Content { get; set; } = string.Empty;

        /// <summary>tool 角色消息：对应的工具调用 Id（协议要求与上轮 assistant.tool_calls[].id 配对，缺失直接 400）</summary>
        public string? ToolCallId { get; set; }

        /// <summary>tool 角色消息：工具名（部分兼容端要求携带）</summary>
        public string? Name { get; set; }

        /// <summary>assistant 角色消息：模型发起的工具调用请求（下一轮请求须原样回传，协议配对校验依赖）</summary>
        public List<AiToolCall>? ToolCalls { get; set; }
    }

    /// <summary>解密后的模型连接参数（由 AiModelService 从实体解密组装，含 Key 明文，仅内存传递）</summary>
    public class AiModelConfig
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string BaseUrl { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public string ModelId { get; set; } = string.Empty;
        public int MaxContextChars { get; set; } = 60000;
    }

    /// <summary>非流式补全结果（连通性测试用）</summary>
    public class AiCompletionResult
    {
        public string Content { get; set; } = string.Empty;
        public int TokensIn { get; set; }
        public int TokensOut { get; set; }
        /// <summary>工具调用请求（Function Calling）</summary>
        public List<AiToolCall>? ToolCalls { get; set; }
    }

    /// <summary>流式增量（最后一个 chunk 可能带 usage，上游不给则为 0，由调用方估算兜底）</summary>
    public class AiStreamChunk
    {
        public string Delta { get; set; } = string.Empty;
        public int TokensIn { get; set; }
        public int TokensOut { get; set; }
    }

    /// <summary>
    /// AI 补全 Provider：OpenAI 兼容协议（/chat/completions）通吃 DeepSeek/通义/Kimi/GLM/Ollama/vLLM。
    /// 实现方持有 IHttpClientFactory（named client "Ai"，长超时防长生成被掐）。
    /// 支持 Function Calling（tools 参数 + tool_calls 响应解析）。
    /// </summary>
    public interface IAiCompletionProvider
    {
        /// <summary>非流式补全（模型连通性测试用）</summary>
        Task<AiCompletionResult> CompleteAsync(AiModelConfig model, List<AiPromptMessage> messages, CancellationToken ct);

        /// <summary>带工具定义的补全（Function Calling）</summary>
        Task<AiCompletionResult> CompleteWithToolsAsync(AiModelConfig model, List<AiPromptMessage> messages, List<AiToolDefinition>? tools, CancellationToken ct);

        /// <summary>流式补全：逐 chunk 产出增量文本</summary>
        IAsyncEnumerable<AiStreamChunk> StreamAsync(AiModelConfig model, List<AiPromptMessage> messages, CancellationToken ct);
    }

    /// <summary>
    /// AI 流式推送通知器：Service 层只依赖本接口，云端实现经 SignalR AiResponseChunk 事件推给用户。
    /// 与 IAsyncTaskNotifier 同模式（fire-and-forget，失败不影响生成主流程）。
    /// </summary>
    public interface IAiChunkNotifier
    {
        /// <summary>推送一个生成 chunk（Delta 增量或 Done 终态）</summary>
        Task NotifyUserAsync(Guid userId, AiChunkDto chunk);
    }
}

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ConvenientSystem.Shared.Common.Exceptions;
using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Ai
{
    /// <summary>
    /// OpenAI 兼容协议实现：POST {BaseUrl}/chat/completions，流式 SSE（data: {...} / [DONE]）。
    /// 支持 Function Calling（tools 参数 + tool_calls 响应解析）。
    /// 不带 stream_options（部分兼容端不识别），token 数优先取上游 usage、缺失时按字符数估算。
    /// </summary>
    public class OpenAiCompatibleProvider : IAiCompletionProvider
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public OpenAiCompatibleProvider(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        public async Task<AiCompletionResult> CompleteAsync(AiModelConfig model, List<AiPromptMessage> messages, CancellationToken ct)
        {
            return await CompleteWithToolsAsync(model, messages, tools: null, ct);
        }

        /// <summary>带工具定义的完成请求（支持 Function Calling）</summary>
        public async Task<AiCompletionResult> CompleteWithToolsAsync(AiModelConfig model, List<AiPromptMessage> messages, List<AiToolDefinition>? tools, CancellationToken ct)
        {
            var url = BuildUrl(model);
            using var client = _httpClientFactory.CreateClient("Ai");
            using var request = BuildRequest(model, url, messages, stream: false, tools);
            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                throw new BizException(await DescribeErrorAsync(response, ct));

            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var root = doc.RootElement;

            // 解析 content
            var content = string.Empty;
            if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0
                && choices[0].ValueKind == JsonValueKind.Object
                && choices[0].TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.Object)
            {
                if (message.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String)
                    content = c.GetString() ?? string.Empty;
            }

            // 解析 tool_calls
            List<AiToolCall>? toolCalls = null;
            if (root.TryGetProperty("choices", out var choices2) && choices2.ValueKind == JsonValueKind.Array && choices2.GetArrayLength() > 0
                && choices2[0].ValueKind == JsonValueKind.Object
                && choices2[0].TryGetProperty("message", out var message2) && message2.ValueKind == JsonValueKind.Object
                && message2.TryGetProperty("tool_calls", out var toolCallsElem) && toolCallsElem.ValueKind == JsonValueKind.Array)
            {
                toolCalls = new List<AiToolCall>();
                foreach (var tc in toolCallsElem.EnumerateArray())
                {
                    if (tc.ValueKind != JsonValueKind.Object) continue;
                    var call = new AiToolCall();
                    if (tc.TryGetProperty("id", out var id)) call.Id = id.GetString() ?? "";
                    if (tc.TryGetProperty("type", out var type)) call.Type = type.GetString() ?? "function";
                    if (tc.TryGetProperty("function", out var func) && func.ValueKind == JsonValueKind.Object)
                    {
                        if (func.TryGetProperty("name", out var name)) call.Function.Name = name.GetString() ?? "";
                        if (func.TryGetProperty("arguments", out var args)) call.Function.Arguments = args.GetString() ?? "";
                    }
                    toolCalls.Add(call);
                }
            }

            var (tin, tout) = ReadUsage(root);
            return new AiCompletionResult
            {
                Content = content,
                TokensIn = tin,
                TokensOut = tout,
                ToolCalls = toolCalls
            };
        }

        public async IAsyncEnumerable<AiStreamChunk> StreamAsync(AiModelConfig model, List<AiPromptMessage> messages,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            var url = BuildUrl(model);
            using var client = _httpClientFactory.CreateClient("Ai");
            using var request = BuildRequest(model, url, messages, stream: true);
            // 流式必须 ResponseHeadersRead，否则 HttpClient 会先缓冲整个响应（长生成看似卡死）
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
                throw new BizException(await DescribeErrorAsync(response, ct));

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            while (!reader.EndOfStream)
            {
                ct.ThrowIfCancellationRequested();
                var line = await reader.ReadLineAsync(ct);
                if (string.IsNullOrEmpty(line)) continue;
                if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
                var payload = line["data:".Length..].Trim();
                if (payload == "[DONE]") yield break;

                using var doc = JsonDocument.Parse(payload);
                var root = doc.RootElement;
                var delta = string.Empty;
                if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0
                    && choices[0].ValueKind == JsonValueKind.Object
                    && choices[0].TryGetProperty("delta", out var d) && d.ValueKind == JsonValueKind.Object
                    && d.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String)
                    delta = c.GetString() ?? string.Empty;
                var (tin, tout) = ReadUsage(root);
                yield return new AiStreamChunk { Delta = delta, TokensIn = tin, TokensOut = tout };
            }
        }

        private static string BuildUrl(AiModelConfig model)
        {
            var baseUrl = (model.BaseUrl ?? string.Empty).Trim().TrimEnd('/');
            if (baseUrl.Length == 0) throw new BizException("接口地址不能为空");
            return baseUrl + "/chat/completions";
        }

        private static HttpRequestMessage BuildRequest(AiModelConfig model, string url, List<AiPromptMessage> messages, bool stream, List<AiToolDefinition>? tools = null)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            if (!string.IsNullOrEmpty(model.ApiKey))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", model.ApiKey);

            var body = new Dictionary<string, object>
            {
                ["model"] = model.ModelId,
                ["messages"] = messages.Select(SerializeMessage).ToList(),
                ["stream"] = stream
            };

            // 添加工具定义
            if (tools != null && tools.Count > 0)
            {
                body["tools"] = tools.Select(t => new
                {
                    type = t.Type,
                    function = new
                    {
                        name = t.Function.Name,
                        description = t.Function.Description,
                        parameters = new
                        {
                            type = t.Function.Parameters.Type,
                            properties = t.Function.Parameters.Properties.ToDictionary(
                                kv => kv.Key,
                                kv => new { type = kv.Value.Type, description = kv.Value.Description }
                            ),
                            required = t.Function.Parameters.Required
                        }
                    }
                }).ToList();
                body["tool_choice"] = "auto";
            }

            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            return request;
        }

        /// <summary>
        /// 按协议序列化单条消息：tool 结果消息必须带 tool_call_id（与上轮 assistant.tool_calls 配对，
        /// OpenAI 兼容端强校验）；assistant 携带工具调用时须原样回传 tool_calls，否则第二轮请求必被拒。
        /// </summary>
        private static object SerializeMessage(AiPromptMessage m)
        {
            if (m.Role == "tool")
                return new { role = m.Role, content = m.Content, tool_call_id = m.ToolCallId ?? string.Empty, name = m.Name ?? string.Empty };
            if (m.Role == "assistant" && m.ToolCalls is { Count: > 0 })
                return new
                {
                    role = m.Role,
                    content = m.Content,
                    tool_calls = m.ToolCalls.Select(c => new
                    {
                        id = c.Id,
                        type = c.Type,
                        function = new { name = c.Function.Name, arguments = c.Function.Arguments }
                    }).ToList()
                };
            return new { role = m.Role, content = m.Content };
        }

        private static (int tokensIn, int tokensOut) ReadUsage(JsonElement root)
        {
            if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
            {
                var tin = usage.TryGetProperty("prompt_tokens", out var pt) ? pt.GetInt32() : 0;
                var tout = usage.TryGetProperty("completion_tokens", out var ctp) ? ctp.GetInt32() : 0;
                return (tin, tout);
            }
            return (0, 0);
        }

        /// <summary>非 2xx 时读响应体前 200 字符作为友好错误（上游通常给 {error:{message}}）</summary>
        private static async Task<string> DescribeErrorAsync(HttpResponseMessage response, CancellationToken ct)
        {
            var status = (int)response.StatusCode;
            string detail;
            try
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("error", out var err) && err.TryGetProperty("message", out var msg))
                    detail = msg.GetString() ?? body;
                else
                    detail = body;
            }
            catch
            {
                detail = response.ReasonPhrase ?? string.Empty;
            }
            detail = detail.Trim();
            if (detail.Length > 200) detail = detail[..200] + "…";
            return $"上游返回 {status}：{detail}";
        }
    }
}

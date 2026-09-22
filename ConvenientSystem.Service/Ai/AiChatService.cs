using ConvenientSystem.Shared.Common;
using System.Diagnostics;
using ConvenientSystem.Service.Common;
using ConvenientSystem.Shared.Common.Exceptions;
using ConvenientSystem.Shared.Entity.Common;
using ConvenientSystem.Shared.Model.Common;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ConvenientSystem.Service.Ai
{
    /// <summary>
    /// AI 对话实现。Send 的同步段（校验/落库）在请求线程执行；流式生成在 Task.Run 后台执行，
    /// 后台段只使用单例依赖（IFreeSql/Provider/Notifier/Registry）与已捕获的值参数，
    /// 不触碰任何 scoped 服务（ISysConfigService 等），避免请求结束后访问已失效上下文。
    /// </summary>
    public class AiChatService : IAiChatService
    {
        private const string SystemPrompt = @"你是 ConvenientSystem 内置的 AI 助手，用简洁、专业的中文回答问题；代码与技术问题给出可直接落地的答案。

你可以调用以下工具帮助用户：
- send_message：发送站内消息（消息立即送达对方）
- query_attendance：查询考勤记录
- query_sql：查询系统数据（只读，仅限考勤相关表：BuAttendance、DingtalkUser、DeptView）
- create_task：创建任务/待办（创建后用户可在 AI 助手「任务」面板查看与勾选完成）

当用户请求涉及这些功能时，使用对应工具完成后再回复。调用 send_message 前，若收件人或消息内容不够明确，先向用户确认清楚再执行。";

        private static readonly string[] EnabledValues = { "true", "1" };

        /// <summary>chunk 节流推送：距上次推送超过该毫秒数才 flush（避免逐字推送打爆 SignalR）</summary>
        private const int PushIntervalMs = 300;

        private readonly IFreeSql _db;
        private readonly IAiModelService _modelService;
        private readonly IAiCompletionProvider _provider;
        private readonly IAiChunkNotifier _notifier;
        private readonly AiGenerationRegistry _registry;
        private readonly ISysConfigService _sysConfigService;
        private readonly AiToolService _toolService;
        private readonly ILogger<AiChatService> _logger;

        public AiChatService(
            [FromKeyedServices("ConvenientSystemDb")] IFreeSql db,
            IAiModelService modelService,
            IAiCompletionProvider provider,
            IAiChunkNotifier notifier,
            AiGenerationRegistry registry,
            ISysConfigService sysConfigService,
            AiToolService toolService,
            ILogger<AiChatService> logger)
        {
            _db = db;
            _modelService = modelService;
            _provider = provider;
            _notifier = notifier;
            _registry = registry;
            _sysConfigService = sysConfigService;
            _toolService = toolService;
            _logger = logger;
        }

        public AiMyStatusDto GetMyStatus(Guid userId)
        {
            var today = TimeHelper.Now.Date;
            var usage = _db.Select<AiUsageDailyEntity>()
                .Where(u => u.UserId == userId.ToString() && u.UtcDate == today)
                .First();

            return new AiMyStatusDto
            {
                Enabled = IsEnabled(),
                RequestsUsed = usage?.RequestCount ?? 0,
                RequestsQuota = ParseInt(_sysConfigService.GetValue("ai.quota.dailyRequests"), 50),
                // 与 ResolveUsableModel() 同口径：只要有启用模型即可发送（Scenes 首期固定 chat，不参与门槛判定）
                ModelReady = _db.Select<AiModelEntity>().Any(m => m.Enabled),
            };
        }

        public List<AiConversationDto> GetConversations(Guid userId)
        {
            return _db.Select<AiConversationEntity>()
                .Where(c => c.UserId == userId.ToString())
                .OrderByDescending(c => c.UpdateTime)
                .ToList(c => new AiConversationDto { Id = c.Id, Title = c.Title, UpdateTime = c.UpdateTime });
        }

        public List<AiMessageDto> GetMessages(Guid userId, long conversationId)
        {
            if (!_db.Select<AiConversationEntity>().Any(c => c.Id == conversationId && c.UserId == userId.ToString()))
                throw new BizException("会话不存在");
            return ToDto(_db.Select<AiMessageEntity>()
                .Where(m => m.ConversationId == conversationId)
                .OrderBy(m => m.Id)
                .ToList());
        }

        public void DeleteConversation(Guid userId, long conversationId)
        {
            var owned = _db.Select<AiConversationEntity>().Where(c => c.Id == conversationId && c.UserId == userId.ToString()).First();
            if (owned == null) throw new BizException("会话不存在");
            _db.Delete<AiMessageEntity>().Where(m => m.ConversationId == conversationId).ExecuteAffrows();
            _db.Delete<AiConversationEntity>().Where(c => c.Id == conversationId).ExecuteAffrows();
        }

        public AiSendResult Send(Guid userId, AiSendRequest request)
        {
            if (request == null) throw new BizException("请求不能为空");
            var content = request.Content?.Trim() ?? string.Empty;
            if (content.Length == 0) throw new BizException("消息内容不能为空");
            if (content.Length > 100_000) throw new BizException("消息内容过长");

            if (!IsEnabled()) throw new BizException("AI 功能未启用，请联系管理员在系统配置中开启");

            // 配额检查：请求次数 + token 双配额（均为 0 = 不限制）
            var userIdStr = userId.ToString();
            var today = TimeHelper.Now.Date;
            var requestsQuota = ParseInt(_sysConfigService.GetValue("ai.quota.dailyRequests"), 50);
            var tokensQuota = ParseInt(_sysConfigService.GetValue("ai.quota.dailyTokensK"), 200) * 1000;
            var usage = _db.Select<AiUsageDailyEntity>()
                .Where(u => u.UserId == userIdStr && u.UtcDate == today).First();
            if (requestsQuota > 0 && (usage?.RequestCount ?? 0) >= requestsQuota)
                throw new BizException("今日 AI 请求配额已用完，请明天再试或联系管理员调整配额");
            if (tokensQuota > 0 && ((usage?.TokensIn ?? 0) + (usage?.TokensOut ?? 0)) >= tokensQuota)
                throw new BizException("今日 AI Token 配额已用完，请明天再试或联系管理员调整配额");

            // 模型解析（全员统一全局默认）与配额落库在请求线程完成，之后一切值传给后台任务
            var chosen = _modelService.ResolveUsableModel();

            // 会话：ConversationId=0 新建（标题取首条消息前 20 字），否则校验归属
            AiConversationEntity conversation;
            if (request.ConversationId == 0)
            {
                conversation = new AiConversationEntity
                {
                    UserId = userIdStr,
                    Title = content.Length > 20 ? content[..20] + "…" : content,
                    };
                conversation.Id = _db.Insert(conversation).ExecuteIdentity();
            }
            else
            {
                conversation = _db.Select<AiConversationEntity>()
                    .Where(c => c.Id == request.ConversationId && c.UserId == userIdStr).First()
                    ?? throw new BizException("会话不存在");
                _db.Update<AiConversationEntity>().Set(c => c.UpdateTime, TimeHelper.Now)
                    .Set(c => c.ModelId, chosen.Id).Where(c => c.Id == conversation.Id).ExecuteAffrows();
            }

            var userMessage = new AiMessageEntity
            {
                ConversationId = conversation.Id,
                UserId = userIdStr,
                Role = "user",
                Content = content,
                Status = 1,
            };
            userMessage.Id = _db.Insert(userMessage).ExecuteIdentity();

            var assistantMessage = new AiMessageEntity
            {
                ConversationId = conversation.Id,
                UserId = userIdStr,
                Role = "assistant",
                Content = string.Empty,
                Status = 0, // 生成中（轮询兜底以此判断）
            };
            assistantMessage.Id = _db.Insert(assistantMessage).ExecuteIdentity();

            // 请求计数 +1（失败/停止不退还，防刷）
            if (usage == null)
            {
                _db.Insert(new AiUsageDailyEntity
                {
                    UtcDate = today, UserId = userIdStr, RequestCount = 1, UpdateTime = TimeHelper.Now,
                }).ExecuteAffrows();
            }
            else
            {
                _db.Update<AiUsageDailyEntity>()
                    .SetRaw("RequestCount = RequestCount + 1")
                    .Set(u => u.UpdateTime, TimeHelper.Now)
                    .Where(u => u.Id == usage.Id).ExecuteAffrows();
            }

            // 后台生成：只传单例依赖与值参数（脱开 this，请求结束后照常工作）
            _ = Task.Run(() => DoGenerateAsync(_db, _provider, _notifier, _registry, _toolService, _logger, userId, conversation.Id,
                assistantMessage.Id, chosen));

            return new AiSendResult
            {
                ConversationId = conversation.Id,
                UserMessageId = userMessage.Id,
                AssistantMessageId = assistantMessage.Id,
            };
        }

        /// <summary>重新生成：重置该 AI 回复（Status=0）并以之前的会话上下文重新生成；配额与 Send 同口径计数</summary>
        public AiSendResult Regenerate(Guid userId, long messageId)
        {
            var message = _db.Select<AiMessageEntity>().Where(m => m.Id == messageId).First();
            if (message == null || message.UserId != userId.ToString()) throw new BizException("消息不存在");
            if (message.Role != "assistant") throw new BizException("仅支持重新生成 AI 回复");
            if (message.Status == 0) throw new BizException("该回复正在生成中");

            if (!IsEnabled()) throw new BizException("AI 功能未启用，请联系管理员在系统配置中开启");

            var userIdStr = userId.ToString();
            var today = TimeHelper.Now.Date;
            var requestsQuota = ParseInt(_sysConfigService.GetValue("ai.quota.dailyRequests"), 50);
            var tokensQuota = ParseInt(_sysConfigService.GetValue("ai.quota.dailyTokensK"), 200) * 1000;
            var usage = _db.Select<AiUsageDailyEntity>()
                .Where(u => u.UserId == userIdStr && u.UtcDate == today).First();
            if (requestsQuota > 0 && (usage?.RequestCount ?? 0) >= requestsQuota)
                throw new BizException("今日 AI 请求配额已用完，请明天再试或联系管理员调整配额");
            if (tokensQuota > 0 && ((usage?.TokensIn ?? 0) + (usage?.TokensOut ?? 0)) >= tokensQuota)
                throw new BizException("今日 AI Token 配额已用完，请明天再试或联系管理员调整配额");

            var chosen = _modelService.ResolveUsableModel();

            // 重置该回复为生成中（BuildPrompt 按 Status 过滤，即以之前的用户消息与历史重新生成）
            _db.Update<AiMessageEntity>()
                .Set(m => m.Content, string.Empty)
                .Set(m => m.Status, 0)
                .Set(m => m.Error, (string?)null)
                .Set(m => m.TokensIn, 0)
                .Set(m => m.TokensOut, 0)
                .Where(m => m.Id == messageId).ExecuteAffrows();
            _db.Update<AiConversationEntity>().Set(c => c.UpdateTime, TimeHelper.Now)
                .Set(c => c.ModelId, chosen.Id).Where(c => c.Id == message.ConversationId).ExecuteAffrows();

            // 请求计数 +1（重新生成同样计入配额，失败/停止不退还）
            if (usage == null)
            {
                _db.Insert(new AiUsageDailyEntity
                {
                    UtcDate = today, UserId = userIdStr, RequestCount = 1, UpdateTime = TimeHelper.Now,
                }).ExecuteAffrows();
            }
            else
            {
                _db.Update<AiUsageDailyEntity>()
                    .SetRaw("RequestCount = RequestCount + 1")
                    .Set(u => u.UpdateTime, TimeHelper.Now)
                    .Where(u => u.Id == usage.Id).ExecuteAffrows();
            }

            _ = Task.Run(() => DoGenerateAsync(_db, _provider, _notifier, _registry, _toolService, _logger, userId, message.ConversationId,
                messageId, chosen));

            return new AiSendResult
            {
                ConversationId = message.ConversationId,
                UserMessageId = 0,
                AssistantMessageId = messageId,
            };
        }

        public void StopGeneration(Guid userId, long messageId)
        {
            var message = _db.Select<AiMessageEntity>().Where(m => m.Id == messageId).First();
            if (message == null || message.UserId != userId.ToString())
                throw new BizException("消息不存在");
            if (message.Status != 0) return; // 已结束，静默成功
            if (!_registry.TryCancel(messageId))
                throw new BizException("该生成已结束或服务已重启，无法停止");
        }

        public int DeleteExpired()
        {
            var days = ParseInt(_sysConfigService.GetValue("ai.conversation.retentionDays"), 30);
            if (days <= 0) return 0; // 0 = 永久保留
            var deadline = TimeHelper.Now.AddDays(-days);
            var expired = _db.Select<AiConversationEntity>().Where(c => c.UpdateTime < deadline).ToList(c => c.Id);
            if (expired.Count == 0) return 0;
            _db.Delete<AiMessageEntity>().Where(m => expired.Contains(m.ConversationId)).ExecuteAffrows();
            return _db.Delete<AiConversationEntity>().Where(c => expired.Contains(c.Id)).ExecuteAffrows();
        }

        /// <summary>我的 AI 任务列表（create_task 工具创建的待办；待办在前，创建时间倒序，上限 100 条）</summary>
        public List<AiTaskDto> GetTasks(Guid userId)
        {
            return _db.Select<AiTaskEntity>()
                .Where(t => t.UserId == userId.ToString())
                .OrderBy(t => t.Status)
                .OrderByDescending(t => t.CreateTime)
                .Limit(100)
                .ToList(t => new AiTaskDto
                {
                    Id = t.Id,
                    Title = t.Title,
                    Description = t.Description,
                    DueDate = t.DueDate,
                    Status = t.Status,
                    CreateTime = t.CreateTime,
                });
        }

        /// <summary>更新任务状态（0 待办 / 1 完成，校验归属）</summary>
        public void UpdateTaskStatus(Guid userId, long taskId, int status)
        {
            if (status is not (0 or 1)) throw new BizException("无效的任务状态");
            var affrows = _db.Update<AiTaskEntity>()
                .Set(t => t.Status, status)
                .Where(t => t.Id == taskId && t.UserId == userId.ToString())
                .ExecuteAffrows();
            if (affrows == 0) throw new BizException("任务不存在");
        }

        // ------------------------------------------------------------------ 后台生成

        /// <summary>后台流式生成（静态方法：仅依赖单例与值参数）</summary>
        private static async Task DoGenerateAsync(
            IFreeSql db, IAiCompletionProvider provider, IAiChunkNotifier notifier,
            AiGenerationRegistry registry, AiToolService toolService, ILogger logger,
            Guid userId, long conversationId, long messageId, AiModelConfig model)
        {
            var cts = registry.Begin(messageId);
            var sw = Stopwatch.StartNew();
            var buffer = string.Empty;
            try
            {
                var messages = BuildPrompt(db, conversationId, model);
                var tools = toolService.GetToolDefinitions();

                // Function Calling 循环（最多 3 轮）
                const int maxToolRounds = 3;
                for (int round = 0; round < maxToolRounds; round++)
                {
                    var result = await provider.CompleteWithToolsAsync(model, messages, tools, cts.Token);

                    // 如果有工具调用，执行并继续
                    if (result.ToolCalls != null && result.ToolCalls.Count > 0)
                    {
                        // 添加 assistant 消息（含 tool_calls，下一轮原样回传——协议配对强校验）
                        messages.Add(new AiPromptMessage
                        {
                            Role = "assistant",
                            Content = result.Content,
                            ToolCalls = result.ToolCalls
                        });

                        // 执行每个工具调用（逐个推送中间态，前端工具横条实时更新）
                        foreach (var call in result.ToolCalls)
                        {
                            await SafeNotifyAsync(notifier, userId, new AiChunkDto
                            {
                                ConversationId = conversationId, MessageId = messageId,
                                ToolEvent = new AiToolEventDto { CallId = call.Id, Name = call.Function.Name, Status = "executing" },
                            });
                            try
                            {
                                var toolResult = await toolService.ExecuteAsync(call.Function.Name, call.Function.Arguments, userId, cts.Token);
                                messages.Add(new AiPromptMessage
                                {
                                    Role = "tool",
                                    Content = toolResult.Content,
                                    ToolCallId = call.Id,
                                    Name = call.Function.Name,
                                });
                                await SafeNotifyAsync(notifier, userId, new AiChunkDto
                                {
                                    ConversationId = conversationId, MessageId = messageId,
                                    ToolEvent = new AiToolEventDto
                                    {
                                        CallId = call.Id, Name = call.Function.Name, Status = "done",
                                        Result = toolResult.Content.Length > 200 ? toolResult.Content[..200] + "…" : toolResult.Content,
                                    },
                                });
                            }
                            catch (Exception ex)
                            {
                                logger.LogWarning(ex, "工具调用失败：{Tool}", call.Function.Name);
                                messages.Add(new AiPromptMessage
                                {
                                    Role = "tool",
                                    Content = $"工具调用失败：{ex.Message}",
                                    ToolCallId = call.Id,
                                    Name = call.Function.Name,
                                });
                                await SafeNotifyAsync(notifier, userId, new AiChunkDto
                                {
                                    ConversationId = conversationId, MessageId = messageId,
                                    ToolEvent = new AiToolEventDto { CallId = call.Id, Name = call.Function.Name, Status = "error", Error = ex.Message },
                                });
                            }
                        }

                        // 继续下一轮
                        continue;
                    }

                    // 没有工具调用，最终回复
                    buffer = result.Content;
                    break;
                }

                // 流式推送最终回复（如果 buffer 已有内容，直接推送）
                if (!string.IsNullOrEmpty(buffer))
                {
                    await SafeNotifyAsync(notifier, userId, new AiChunkDto
                    {
                        ConversationId = conversationId, MessageId = messageId, Delta = buffer,
                    });
                }
                else
                {
                    // 如果 buffer 为空，用流式生成
                    var lastPush = sw.Elapsed;
                    var pushBuffer = string.Empty;
                    await foreach (var chunk in provider.StreamAsync(model, messages, cts.Token))
                    {
                        buffer += chunk.Delta;
                        pushBuffer += chunk.Delta;
                        if ((sw.Elapsed - lastPush).TotalMilliseconds >= PushIntervalMs)
                        {
                            await SafeNotifyAsync(notifier, userId, new AiChunkDto
                            {
                                ConversationId = conversationId, MessageId = messageId, Delta = pushBuffer,
                            });
                            pushBuffer = string.Empty;
                            lastPush = sw.Elapsed;
                        }
                    }
                }

                sw.Stop();
                var tokensIn = EstimateTokens(string.Concat(messages.Select(m => m.Content)));
                var tokensOut = EstimateTokens(buffer);
                db.Update<AiMessageEntity>()
                    .Set(m => m.Content, buffer)
                    .Set(m => m.Status, 1)
                    .Set(m => m.TokensIn, tokensIn)
                    .Set(m => m.TokensOut, tokensOut)
                    .Set(m => m.ElapsedMs, (int)sw.ElapsedMilliseconds)
                    .Where(m => m.Id == messageId).ExecuteAffrows();
                AddTokenUsage(db, userId, tokensIn, tokensOut);
                await SafeNotifyAsync(notifier, userId, new AiChunkDto
                {
                    ConversationId = conversationId, MessageId = messageId, Done = true, Content = buffer,
                });
            }
            catch (OperationCanceledException)
            {
                sw.Stop();
                // 用户手动停止：保留已生成部分，标记 Status=3
                db.Update<AiMessageEntity>()
                    .Set(m => m.Content, buffer)
                    .Set(m => m.Status, 3)
                    .Set(m => m.Error, "已手动停止")
                    .Set(m => m.ElapsedMs, (int)sw.ElapsedMilliseconds)
                    .Where(m => m.Id == messageId).ExecuteAffrows();
                await SafeNotifyAsync(notifier, userId, new AiChunkDto
                {
                    ConversationId = conversationId, MessageId = messageId, Done = true, Content = buffer,
                    Error = "已手动停止",
                });
            }
            catch (Exception ex)
            {
                sw.Stop();
                logger.LogError(ex, "AI 生成失败（Conversation={ConversationId}, Message={MessageId}）", conversationId, messageId);
                var error = ex is BizException biz ? biz.Message : $"生成失败：{ex.Message}";
                db.Update<AiMessageEntity>()
                    .Set(m => m.Content, buffer)
                    .Set(m => m.Status, 2)
                    .Set(m => m.Error, error)
                    .Set(m => m.ElapsedMs, (int)sw.ElapsedMilliseconds)
                    .Where(m => m.Id == messageId).ExecuteAffrows();
                await SafeNotifyAsync(notifier, userId, new AiChunkDto
                {
                    ConversationId = conversationId, MessageId = messageId, Done = true, Content = buffer,
                    Error = error,
                });
            }
            finally
            {
                registry.End(messageId);
            }
        }

        /// <summary>组装上下文：system 提示词 + 会话历史（含刚落库的 user 消息）按 MaxContextChars 从最新往回截断</summary>
        private static List<AiPromptMessage> BuildPrompt(IFreeSql db, long conversationId, AiModelConfig model)
        {
            var history = db.Select<AiMessageEntity>()
                .Where(m => m.ConversationId == conversationId && m.Status != 0 && m.Status != 2)
                .OrderByDescending(m => m.Id)
                .ToList();
            var prompts = new List<AiPromptMessage> { new() { Role = "system", Content = SystemPrompt } };
            var budget = model.MaxContextChars - SystemPrompt.Length;
            foreach (var m in history) // 倒序累积，超出预算停止（即保留最近的消息）
            {
                if (budget - m.Content.Length < 0) break;
                prompts.Add(new AiPromptMessage { Role = m.Role, Content = m.Content });
                budget -= m.Content.Length;
            }
            prompts.Reverse(); // API 要求时间正序：system 在前
            return prompts;
        }

        /// <summary>token 粗估（约 4 字符 = 1 token，仅配额粗控用；上游 usage 在流式里不可靠故统一估算）</summary>
        private static int EstimateTokens(string text) => Math.Max(0, text.Length / 4);

        private static void AddTokenUsage(IFreeSql db, Guid userId, int tokensIn, int tokensOut)
        {
            var userIdStr = userId.ToString();
            var today = TimeHelper.Now.Date;
            var usage = db.Select<AiUsageDailyEntity>().Where(u => u.UserId == userIdStr && u.UtcDate == today).First();
            if (usage == null)
            {
                db.Insert(new AiUsageDailyEntity
                {
                    UtcDate = today, UserId = userIdStr,
                    TokensIn = tokensIn, TokensOut = tokensOut, UpdateTime = TimeHelper.Now,
                }).ExecuteAffrows();
            }
            else
            {
                db.Update<AiUsageDailyEntity>()
                    .SetRaw($"TokensIn = TokensIn + {tokensIn}")
                    .SetRaw($"TokensOut = TokensOut + {tokensOut}")
                    .Set(u => u.UpdateTime, TimeHelper.Now)
                    .Where(u => u.Id == usage.Id).ExecuteAffrows();
            }
        }

        /// <summary>推送失败不影响生成主流程（fire-and-forget 同 AsyncTaskNotifier 语义）</summary>
        private static async Task SafeNotifyAsync(IAiChunkNotifier notifier, Guid userId, AiChunkDto chunk)
        {
            try { await notifier.NotifyUserAsync(userId, chunk); }
            catch { /* 推送通道异常时前端轮询兜底 */ }
        }

        // ------------------------------------------------------------------ 通用

        private bool IsEnabled() => EnabledValues.Contains(
            (_sysConfigService.GetValue("ai.enabled") ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase);

        private static int ParseInt(string? value, int fallback = 0)
            => int.TryParse(value?.Trim(), out var n) && n >= 0 ? n : fallback;

        private static List<AiMessageDto> ToDto(List<AiMessageEntity> entities) => entities.Select(m => new AiMessageDto
        {
            Id = m.Id,
            ConversationId = m.ConversationId,
            Role = m.Role,
            Content = m.Content,
            Status = m.Status,
            TokensIn = m.TokensIn,
            TokensOut = m.TokensOut,
            Error = m.Error,
            CreateTime = m.CreateTime,
        }).ToList();
    }
}

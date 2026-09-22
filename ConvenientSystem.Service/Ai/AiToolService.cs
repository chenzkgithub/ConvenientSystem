using ConvenientSystem.Shared.Common;
using System.Text.Json;
using ConvenientSystem.Service.Common;
using ConvenientSystem.Shared.Common.Exceptions;
using ConvenientSystem.Shared.Entity.Common;
using ConvenientSystem.Shared.Entity.YunHan;
using ConvenientSystem.Shared.Model.Common;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ConvenientSystem.Service.Ai
{
    /// <summary>
    /// AI 工具函数服务：定义并执行 Function Calling 工具。
    /// 工具列表：send_message / query_attendance / query_sql / create_task。
    /// 双库分工：考勤查询与只读 SQL 走业务库（云汉，与 AttendanceService 同连接），
    /// 查收件用户（SysUser）与任务落库（AiTask）走主库（系统表）。
    /// </summary>
    public class AiToolService
    {
        /// <summary>主库（系统表：SysUser 查收件人、AiTask 落库）</summary>
        private readonly IFreeSql _db;
        /// <summary>业务库（云汉考勤数据：BuAttendance/DingtalkUser/DeptView）</summary>
        private readonly IFreeSql _bizDb;
        private readonly IChatService _chatService;
        private readonly ILogger<AiToolService> _logger;

        /// <summary>SQL 查询允许的表名（白名单）</summary>
        private static readonly HashSet<string> AllowedTables = new(StringComparer.OrdinalIgnoreCase)
        {
            "BuAttendance", "DingtalkUser", "DeptView"
        };

        public AiToolService(IFreeSql bizDb, [FromKeyedServices("ConvenientSystemDb")] IFreeSql db,
            IChatService chatService, ILogger<AiToolService> logger)
        {
            _bizDb = bizDb;
            _db = db;
            _chatService = chatService;
            _logger = logger;
        }

        /// <summary>获取所有工具定义（OpenAI tools 参数格式）</summary>
        public List<AiToolDefinition> GetToolDefinitions()
        {
            return new List<AiToolDefinition>
            {
                new()
                {
                    Function = new AiToolFunction
                    {
                        Name = "send_message",
                        Description = "发送站内消息给指定用户（消息立即送达对方）。",
                        Parameters = new AiToolParameters
                        {
                            Properties = new Dictionary<string, AiToolProperty>
                            {
                                ["userId"] = new() { Type = "string", Description = "接收消息的用户账号（登录名）" },
                                ["content"] = new() { Type = "string", Description = "消息内容" }
                            },
                            Required = new List<string> { "userId", "content" }
                        }
                    }
                },
                new()
                {
                    Function = new AiToolFunction
                    {
                        Name = "query_attendance",
                        Description = "查询考勤记录。可指定用户和时间范围，不指定用户时查当前用户。",
                        Parameters = new AiToolParameters
                        {
                            Properties = new Dictionary<string, AiToolProperty>
                            {
                                ["userId"] = new() { Type = "string", Description = "用户账号（可选，不填查当前用户）" },
                                ["startDate"] = new() { Type = "string", Description = "开始日期（YYYY-MM-DD）" },
                                ["endDate"] = new() { Type = "string", Description = "结束日期（YYYY-MM-DD）" }
                            },
                            Required = new List<string> { "startDate", "endDate" }
                        }
                    }
                },
                new()
                {
                    Function = new AiToolFunction
                    {
                        Name = "query_sql",
                        Description = "执行 SQL 查询（只读）。只能查询考勤相关表：BuAttendance、DingtalkUser、DeptView。",
                        Parameters = new AiToolParameters
                        {
                            Properties = new Dictionary<string, AiToolProperty>
                            {
                                ["sql"] = new() { Type = "string", Description = "SELECT 语句（只能查 BuAttendance/DingtalkUser/DeptView）" }
                            },
                            Required = new List<string> { "sql" }
                        }
                    }
                },
                new()
                {
                    Function = new AiToolFunction
                    {
                        Name = "create_task",
                        Description = "创建任务/待办事项。",
                        Parameters = new AiToolParameters
                        {
                            Properties = new Dictionary<string, AiToolProperty>
                            {
                                ["title"] = new() { Type = "string", Description = "任务标题" },
                                ["description"] = new() { Type = "string", Description = "任务描述（可选）" },
                                ["dueDate"] = new() { Type = "string", Description = "截止时间（YYYY-MM-DD，可选）" }
                            },
                            Required = new List<string> { "title" }
                        }
                    }
                }
            };
        }

        /// <summary>执行工具调用，返回结果</summary>
        public async Task<AiToolResult> ExecuteAsync(string toolName, string argumentsJson, Guid userId, CancellationToken ct)
        {
            _logger.LogInformation("AI 工具调用：{Tool} by {User}", toolName, userId);

            return toolName switch
            {
                "send_message" => await SendMessageAsync(argumentsJson, userId, ct),
                "query_attendance" => await QueryAttendanceAsync(argumentsJson, userId, ct),
                "query_sql" => await QuerySqlAsync(argumentsJson, userId, ct),
                "create_task" => await CreateTaskAsync(argumentsJson, userId, ct),
                _ => throw new BizException($"未知工具：{toolName}")
            };
        }

        private async Task<AiToolResult> SendMessageAsync(string argumentsJson, Guid userId, CancellationToken ct)
        {
            var args = JsonSerializer.Deserialize<SendMessageArgs>(argumentsJson);
            if (args == null || string.IsNullOrWhiteSpace(args.UserId) || string.IsNullOrWhiteSpace(args.Content))
                throw new BizException("参数不完整：需要 userId 和 content");

            // 查找目标用户
            var targetUser = await _db.Select<SysUserEntity>()
                .Where(u => u.Account == args.UserId)
                .FirstAsync(ct);

            if (targetUser == null)
                throw new BizException($"用户 {args.UserId} 不存在");

            // 真实发送：单聊路径自动建/复用会话；停用/删除/双向屏蔽等校验与用户手动发送完全一致（不满足时抛错回传给模型）
            _chatService.SendMessage(userId, targetUser.Id, 0, args.Content);
            _logger.LogInformation("AI 发送消息：from {From} to {To}", userId, args.UserId);

            return new AiToolResult
            {
                Name = "send_message",
                Content = $"已发送站内消息给 {args.UserId}：{args.Content}"
            };
        }

        private async Task<AiToolResult> QueryAttendanceAsync(string argumentsJson, Guid userId, CancellationToken ct)
        {
            var args = JsonSerializer.Deserialize<QueryAttendanceArgs>(argumentsJson);
            if (args == null || string.IsNullOrWhiteSpace(args.StartDate) || string.IsNullOrWhiteSpace(args.EndDate))
                throw new BizException("参数不完整：需要 startDate 和 endDate");

            if (!DateTime.TryParse(args.StartDate, out var start) || !DateTime.TryParse(args.EndDate, out var end))
                throw new BizException("日期格式错误，请使用 YYYY-MM-DD 格式");

            // 查询考勤记录（业务库云汉，非主库）
            var query = _bizDb.Select<BuAttendanceEntity, DingtalkUserEntity, DeptView>()
                .InnerJoin((a, b, c) => a.UserId == b.DDUserId)
                .InnerJoin((a, b, c) => b.DefaultDeptId == c.dept_id)
                .Where((a, b, c) => b.IsDelete == false)
                .Where((a, b, c) => a.WorkDate >= start && a.WorkDate <= end.AddDays(1));

            // 如果指定了用户，过滤
            if (!string.IsNullOrWhiteSpace(args.UserId))
            {
                query = query.Where((a, b, c) => b.DingCode == args.UserId || b.UserName == args.UserId);
            }

            var results = await query
                .Limit(100)
                .ToListAsync((a, b, c) => new
                {
                    WorkDate = a.WorkDate,
                    WorkDuration = a.WorkDuration,
                    OvertimeDuration = a.OvertimeDuration,
                    ActualDuration = a.ActualDuration,
                    UserName = b.UserName,
                    DeptName = c.dept_name
                }, ct);

            return new AiToolResult
            {
                Name = "query_attendance",
                Content = JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true })
            };
        }

        private async Task<AiToolResult> QuerySqlAsync(string argumentsJson, Guid userId, CancellationToken ct)
        {
            var args = JsonSerializer.Deserialize<QuerySqlArgs>(argumentsJson);
            if (args == null || string.IsNullOrWhiteSpace(args.Sql))
                throw new BizException("参数不完整：需要 sql");

            var sql = args.Sql.Trim();

            // 安全检查：只允许 SELECT
            if (!sql.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
                throw new BizException("只允许 SELECT 查询");

            // 安全检查：只能查指定的表
            var containsAllowedTable = false;
            foreach (var table in AllowedTables)
            {
                if (sql.Contains(table, StringComparison.OrdinalIgnoreCase))
                {
                    containsAllowedTable = true;
                    break;
                }
            }

            if (!containsAllowedTable)
                throw new BizException($"只能查询以下表：{string.Join("、", AllowedTables)}");

            // 禁止危险操作
            var dangerousKeywords = new[] { "INSERT", "UPDATE", "DELETE", "DROP", "ALTER", "CREATE", "EXEC", "EXECUTE" };
            foreach (var keyword in dangerousKeywords)
            {
                if (sql.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    throw new BizException($"SQL 包含禁止的操作：{keyword}");
            }

            // 执行查询（业务库云汉，限制 100 行）
            var results = await _bizDb.Ado.QueryAsync<Dictionary<string, object>>(sql, ct);
            if (results.Count > 100)
                results = results.Take(100).ToList();

            return new AiToolResult
            {
                Name = "query_sql",
                Content = JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true })
            };
        }

        private async Task<AiToolResult> CreateTaskAsync(string argumentsJson, Guid userId, CancellationToken ct)
        {
            var args = JsonSerializer.Deserialize<CreateTaskArgs>(argumentsJson);
            if (args == null || string.IsNullOrWhiteSpace(args.Title))
                throw new BizException("参数不完整：需要 title");

            DateTime? dueDate = null;
            if (!string.IsNullOrWhiteSpace(args.DueDate) && DateTime.TryParse(args.DueDate, out var parsed))
                dueDate = parsed;

            var task = new AiTaskEntity
            {
                UserId = userId.ToString(),
                Title = args.Title,
                Description = args.Description,
                DueDate = dueDate,
                Status = 0,
                CreateTime = TimeHelper.Now
            };

            var id = await _db.Insert(task).ExecuteIdentityAsync(ct);
            task.Id = id;

            _logger.LogInformation("AI 创建任务：{TaskId} by {User}: {Title}", id, userId, args.Title);

            return new AiToolResult
            {
                Name = "create_task",
                Content = $"任务已创建：{args.Title}" + (dueDate.HasValue ? $"（截止 {dueDate:yyyy-MM-dd}）" : "") + "，可在 AI 助手「任务」面板查看"
            };
        }

        // 工具参数类型
        private class SendMessageArgs
        {
            public string UserId { get; set; } = string.Empty;
            public string Content { get; set; } = string.Empty;
        }

        private class QueryAttendanceArgs
        {
            public string? UserId { get; set; }
            public string StartDate { get; set; } = string.Empty;
            public string EndDate { get; set; } = string.Empty;
        }

        private class QuerySqlArgs
        {
            public string Sql { get; set; } = string.Empty;
        }

        private class CreateTaskArgs
        {
            public string Title { get; set; } = string.Empty;
            public string? Description { get; set; }
            public string? DueDate { get; set; }
        }
    }
}

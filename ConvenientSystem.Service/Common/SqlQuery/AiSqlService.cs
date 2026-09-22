using System.Text;
using System.Text.RegularExpressions;
using ConvenientSystem.Service.Ai;
using ConvenientSystem.Shared.Common;
using ConvenientSystem.Shared.Common.Exceptions;
using ConvenientSystem.Shared.Entity.Common;
using ConvenientSystem.Shared.Model.Common;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ConvenientSystem.Service.Common.SqlQuery
{
    /// <summary>
    /// AI 辅助 SQL 实现（NL2SQL）。
    /// 表结构上下文构建策略：先拉表/视图清单，按 prompt 关键词打分筛选最相关的 MaxTables 张，
    /// 并行拉取列信息后拼成紧凑文本（上限 MaxSchemaChars，超出截断），连接串等敏感信息绝不进入 prompt。
    /// </summary>
    public class AiSqlService : IAiSqlService
    {
        /// <summary>参与上下文构建的最大表数量（表很多的库只带最相关的一部分，控制 token 与构建耗时）</summary>
        private const int MaxTables = 20;

        /// <summary>schema 文本字符上限（超出截断，给需求描述与回复留上下文空间）</summary>
        private const int MaxSchemaChars = 15000;

        private static readonly string[] EnabledValues = { "true", "1" };

        private static readonly string SystemPromptTemplate = @"你是 SQL 生成专家。根据下方表结构，把用户的自然语言需求转成 {DBTYPE} 方言的 SQL。

硬性要求：
1. 只输出一个 ```sql 代码块，代码块之外不要输出任何解释文字
2. 只生成 SELECT 只读查询，禁止 INSERT/UPDATE/DELETE/DROP/ALTER/TRUNCATE/EXEC
3. 只能使用下方列出的表和列，禁止臆造不存在的对象
4. 表注释与列注释是字段含义的权威说明，选列时优先参考
5. 需要关联时根据主键/外键语义合理 JOIN
{DATABASENAME}
表结构：
{SCHEMA}";

        private readonly IFreeSql _db;
        private readonly IAiModelService _modelService;
        private readonly IAiCompletionProvider _provider;
        private readonly ISchemaService _schemaService;
        private readonly IDataSourceService _dataSourceService;
        private readonly ISysConfigService _sysConfigService;
        private readonly ILogger<AiSqlService> _logger;

        public AiSqlService(
            [FromKeyedServices("ConvenientSystemDb")] IFreeSql db,
            IAiModelService modelService,
            IAiCompletionProvider provider,
            ISchemaService schemaService,
            IDataSourceService dataSourceService,
            ISysConfigService sysConfigService,
            ILogger<AiSqlService> logger)
        {
            _db = db;
            _modelService = modelService;
            _provider = provider;
            _schemaService = schemaService;
            _dataSourceService = dataSourceService;
            _sysConfigService = sysConfigService;
            _logger = logger;
        }

        public async Task<AiSqlGenerateResult> GenerateAsync(Guid userId, AiSqlGenerateRequest request, CancellationToken ct)
        {
            if (request == null) throw new BizException("请求不能为空");
            var prompt = request.Prompt?.Trim() ?? string.Empty;
            if (prompt.Length == 0) throw new BizException("请描述你的查询需求");
            if (prompt.Length > 2000) throw new BizException("需求描述过长（最多 2000 字）");
            if (string.IsNullOrWhiteSpace(request.DataSource)) throw new BizException("请先选择数据源");

            if (!IsEnabled()) throw new BizException("AI 功能未启用，请联系管理员在系统配置中开启");

            // 配额检查：请求次数 + token 双配额（均为 0 = 不限制），与全局助手同一套体系
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

            // 解析模型（全员统一全局默认）
            var model = _modelService.ResolveUsableModel();

            // 目标库为空时用连接串默认库
            var database = request.Database?.Trim() ?? string.Empty;
            if (database.Length == 0)
            {
                var dbList = await _schemaService.GetDatabasesAsync(request.DataSource);
                database = dbList.DefaultDatabase ?? string.Empty;
            }

            // 数据源类型决定 SQL 方言（sqlserver/mysql/postgresql/oracle/sqlite/clickhouse）
            _dataSourceService.Resolve(request.DataSource, out var dbType);

            // 组装表结构上下文
            var schemaText = await BuildSchemaContextAsync(request.DataSource, database, prompt, ct);

            var systemPrompt = SystemPromptTemplate
                .Replace("{DBTYPE}", dbType, StringComparison.Ordinal)
                .Replace("{SCHEMA}", schemaText, StringComparison.Ordinal)
                .Replace("{DATABASENAME}", database.Length > 0 ? $"目标数据库名：{database}" : string.Empty, StringComparison.Ordinal);

            // 生成（NL2SQL 输出短，非流式一次返回即可；loading 期间客户端等待）
            var result = await _provider.CompleteAsync(model, new List<AiPromptMessage>
            {
                new() { Role = "system", Content = systemPrompt },
                new() { Role = "user", Content = prompt },
            }, ct);

            var sql = ExtractSql(result.Content);
            if (sql.Length == 0) throw new BizException("AI 未返回有效 SQL，请换种描述再试");

            // 配额落库：请求 +1、token 累加（失败/异常不退还，防刷；并发安全用 SetRaw 原子自增）
            RecordUsage(usage, userIdStr, today, result.TokensIn, result.TokensOut);

            return new AiSqlGenerateResult { Sql = sql };
        }

        /// <summary>
        /// 组装表结构上下文：表/视图清单 → 按 prompt 关键词打分取最相关的 MaxTables 张 →
        /// 并行拉列信息 → 拼紧凑文本（上限 MaxSchemaChars）。
        /// </summary>
        private async Task<string> BuildSchemaContextAsync(string dataSource, string database, string prompt, CancellationToken ct)
        {
            var objects = await _schemaService.GetObjectsAsync(dataSource, database);

            // 表 + 视图一起参与候选（视图也是可查询对象）
            var candidates = objects.Tables.Concat(objects.Views)
                .Select(t => new { Table = t, Score = MatchScore(t, prompt) })
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Table.Name, StringComparer.OrdinalIgnoreCase)
                .Take(MaxTables)
                .Select(x => x.Table)
                .ToList();

            // 并行拉取列信息（每张表一次元数据查询）
            var columnLists = await Task.WhenAll(candidates.Select(async t =>
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var cols = await _schemaService.GetColumnsAsync(dataSource, database, t.Schema ?? string.Empty, t.Name);
                    return (Table: t, Cols: cols.Columns);
                }
                catch (Exception ex)
                {
                    // 单表列信息读取失败不阻断整体（权限/系统表等场景），该表不带列
                    _logger.LogWarning(ex, "NL2SQL 读取列信息失败：{Schema}.{Name}", t.Schema, t.Name);
                    return (Table: t, Cols: new List<SchemaColumnDto>());
                }
            }));

            var sb = new StringBuilder();
            foreach (var (table, cols) in columnLists)
            {
                var tableComment = string.IsNullOrWhiteSpace(table.Description) ? string.Empty : $" -- {table.Description}";
                sb.Append($"表 {table.Name}{tableComment}\n");
                foreach (var c in cols)
                {
                    var flags = c.IsPk ? " PK" : string.Empty;
                    var colComment = string.IsNullOrWhiteSpace(c.Description) ? string.Empty : $" -- {c.Description}";
                    sb.Append($"  {c.Name} {c.Type}{flags}{colComment}\n");
                }
            }

            var text = sb.ToString();
            if (text.Length > MaxSchemaChars) text = text[..MaxSchemaChars] + "\n（表结构过长已截断）";
            return text;
        }

        /// <summary>表与需求的相关度打分：表名分词命中 +2/个；注释完整包含 +3；注释双字组命中 +1/个</summary>
        private static int MatchScore(SchemaObjectItemDto table, string prompt)
        {
            var score = 0;
            foreach (var token in SplitNameTokens(table.Name))
            {
                if (prompt.Contains(token, StringComparison.OrdinalIgnoreCase)) score += 2;
            }

            var desc = table.Description?.Trim() ?? string.Empty;
            if (desc.Length > 0)
            {
                if (prompt.Contains(desc, StringComparison.OrdinalIgnoreCase)) score += 3;
                else
                {
                    for (var i = 0; i + 2 <= desc.Length; i += 2)
                    {
                        if (prompt.Contains(desc.Substring(i, 2), StringComparison.Ordinal)) score += 1;
                    }
                }
            }
            return score;
        }

        /// <summary>表名展开为匹配词：下划线/驼峰/数字边界切分（User_Log → user,log；userLog → user,log）</summary>
        private static IEnumerable<string> SplitNameTokens(string name)
        {
            var normalized = Regex.Replace(name, @"([a-z0-9])([A-Z])", "$1 $2");
            return normalized.Split(new[] { '_', ' ', '-', '.' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .Where(t => t.Length >= 2);
        }

        /// <summary>从模型回复中提取纯 SQL：优先取 ```sql 代码块，否则用全文</summary>
        private static string ExtractSql(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return string.Empty;

            var match = Regex.Match(content, "```\\s*sql\\s*\\r?\\n(?<body>[\\s\\S]*?)```", RegexOptions.IgnoreCase);
            if (!match.Success)
                match = Regex.Match(content, "```\\s*\\r?\\n(?<body>[\\s\\S]*?)```", RegexOptions.IgnoreCase);

            var sql = (match.Success ? match.Groups["body"].Value : content).Trim();
            // 去掉 AI 常见的开头空行与结尾解释性残留（非 SQL 行以 -- 开头的保留，便于阅读）
            return sql.Trim();
        }

        /// <summary>配额落库：请求 +1、token 原子累加（并发安全，与 AiChatService 同模式）</summary>
        private void RecordUsage(AiUsageDailyEntity? usage, string userIdStr, DateTime today, int tokensIn, int tokensOut)
        {
            if (usage == null)
            {
                _db.Insert(new AiUsageDailyEntity
                {
                    UtcDate = today, UserId = userIdStr, RequestCount = 1,
                    TokensIn = tokensIn, TokensOut = tokensOut, UpdateTime = TimeHelper.Now,
                }).ExecuteAffrows();
            }
            else
            {
                var sqlType = _db.Ado.DataType == DataType.SqlServer ? "ISNULL" : "IFNULL";
                _db.Update<AiUsageDailyEntity>()
                    .SetRaw($"RequestCount = {sqlType}(RequestCount, 0) + 1")
                    .SetRaw($"TokensIn = {sqlType}(TokensIn, 0) + {tokensIn}")
                    .SetRaw($"TokensOut = {sqlType}(TokensOut, 0) + {tokensOut}")
                    .Set(u => u.UpdateTime, TimeHelper.Now)
                    .Where(u => u.Id == usage.Id).ExecuteAffrows();
            }
        }

        private bool IsEnabled()
        {
            var v = _sysConfigService.GetValue("ai.enabled") ?? string.Empty;
            return EnabledValues.Contains(v.Trim(), StringComparer.OrdinalIgnoreCase);
        }

        private static int ParseInt(string? value, int fallback)
            => int.TryParse(value, out var n) ? n : fallback;
    }
}

using ConvenientSystem.Shared.Common.Exceptions;
using ConvenientSystem.Shared.Common.Sql;
using ConvenientSystem.Shared.Model.Common;
using System.Data.Common;
using System.Globalization;
using System.Xml.Linq;

namespace ConvenientSystem.Service.Common.SqlQuery
{
    /// <summary>
    /// SQL 语句执行服务实现：统一走 FreeSql 池化连接 + 会话级 READ UNCOMMITTED，
    /// 执行前切库、结束后还原，避免污染连接池中的连接。
    /// </summary>
    public class SqlExecuteService : ISqlExecuteService
    {
        /// <summary>客户端主动中断请求时的状态码（沿用重构前的约定）</summary>
        private const int ClientClosedRequest = 499;

        /// <summary>导出上限行数</summary>
        private const int MaxExportRows = 100000;

        private readonly IDataSourceService _dataSourceService;
        private readonly ILogger<SqlExecuteService> _logger;

        public SqlExecuteService(IDataSourceService dataSourceService, ILogger<SqlExecuteService> logger)
        {
            _dataSourceService = dataSourceService;
            _logger = logger;
        }

        public async Task<SqlExecuteResultDto> ExecuteAsync(SqlQueryRequest request, CancellationToken ct)
        {
            var sql = PrepareSql(request);
            var fsql = _dataSourceService.Resolve(request.DataSource, out var dbType);

            try
            {
                // FreeSql 池化连接（取到即已打开，using 归还连接池）
                using var pooled = await fsql.Ado.MasterPool.GetAsync();
                var conn = pooled.Value;

                // 全局 NOLOCK：会话级 READ UNCOMMITTED 等价于所有表加 WITH (NOLOCK)
                await DbConnectionHelper.ApplyNoLockAsync(conn, dbType);

                // 切换到前端选中的数据库上下文（执行完还原，避免污染连接池中的连接）
                var originalDb = DbConnectionHelper.SwitchDatabase(conn, dbType, request.Database);
                try
                {
                    await using var cmd = conn.CreateCommand();
                    cmd.CommandText = sql;
                    cmd.CommandTimeout = 0; // 不设置超时，等待数据库返回结果（0 = 无限制）

                    // 注册取消令牌：前端 abort 请求时取消数据库正在执行的命令
                    await using var ctr = ct.Register(() =>
                    {
                        try { cmd.Cancel(); }
                        catch { /* 忽略取消异常 */ }
                    });

                    await using var reader = await cmd.ExecuteReaderAsync(ct);

                    // 服务端分页：流式跳过前面页、只物化当前页，首次执行时继续空读统计实际总行数
                    var page = Math.Max(1, request.Page);
                    var pageSize = Math.Clamp(request.PageSize, 1, 1000);
                    var offset = (long)(page - 1) * pageSize;

                    // 多结果集：每条 SELECT 返回独立结果，各自取同一页窗口
                    var resultSets = new List<SqlResultSetDto>();

                    do
                    {
                        ct.ThrowIfCancellationRequested();

                        var curColumns = new List<string>();
                        for (int i = 0; i < reader.FieldCount; i++)
                            curColumns.Add(reader.GetName(i));

                        var curRows = new List<Dictionary<string, object?>>();
                        long scanned = 0; // 实际扫描到的总行数
                        while (await reader.ReadAsync(ct))
                        {
                            scanned++;
                            if (scanned <= offset) continue; // 跳过前面页（不物化，仅计数）
                            if (curRows.Count < pageSize)
                            {
                                var row = new Dictionary<string, object?>();
                                for (int i = 0; i < reader.FieldCount; i++)
                                {
                                    row[curColumns[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                                }
                                curRows.Add(row);
                            }
                            else if (!request.NeedTotal)
                            {
                                break; // 翻页请求：本页读满即止，总数沿用前端已知值
                            }
                            // NeedTotal：本页读满后继续空读，只为统计真实总行数
                        }

                        if (curColumns.Count > 0)
                        {
                            resultSets.Add(new SqlResultSetDto
                            {
                                Columns = curColumns,
                                Rows = curRows,
                                TotalRows = request.NeedTotal ? scanned : -1
                            });
                        }
                    } while (await reader.NextResultAsync(ct));

                    _logger.LogInformation("SQL查询工具执行成功，返回{Count}个结果集", resultSets.Count);

                    return new SqlExecuteResultDto
                    {
                        ResultSets = resultSets,
                        // 无结果集（INSERT/UPDATE/DELETE/DDL 等）：返回受影响行数（DDL 为 -1）
                        AffectedRows = resultSets.Count == 0 ? reader.RecordsAffected : -1
                    };
                }
                finally
                {
                    DbConnectionHelper.RestoreDatabase(conn, originalDb);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("SQL查询工具执行已取消");
                throw new BizException("查询已取消", ClientClosedRequest);
            }
            catch (DbException) when (ct.IsCancellationRequested)
            {
                _logger.LogInformation("SQL查询工具执行已取消（DbException）");
                throw new BizException("查询已取消", ClientClosedRequest);
            }
            catch (DbException ex)
            {
                _logger.LogWarning(ex, "SQL查询工具执行失败");
                throw new BadRequestException($"SQL 执行错误：{ex.Message}");
            }
        }

        public async Task<SqlExportResultDto> ExportAsync(SqlQueryRequest request, CancellationToken ct)
        {
            var sql = PrepareSql(request);
            var fsql = _dataSourceService.Resolve(request.DataSource, out var dbType);

            try
            {
                using var pooled = await fsql.Ado.MasterPool.GetAsync();
                var conn = pooled.Value;
                await DbConnectionHelper.ApplyNoLockAsync(conn, dbType);
                var originalDb = DbConnectionHelper.SwitchDatabase(conn, dbType, request.Database);
                try
                {
                    await using var cmd = conn.CreateCommand();
                    cmd.CommandText = sql;
                    cmd.CommandTimeout = 0;

                    await using var ctr = ct.Register(() =>
                    {
                        try { cmd.Cancel(); }
                        catch { }
                    });

                    await using var reader = await cmd.ExecuteReaderAsync(ct);

                    var columns = new List<string>();
                    for (int i = 0; i < reader.FieldCount; i++)
                        columns.Add(reader.GetName(i));

                    var rows = new List<Dictionary<string, object?>>();
                    while (await reader.ReadAsync(ct))
                    {
                        if (rows.Count >= MaxExportRows) break;
                        var row = new Dictionary<string, object?>();
                        for (int i = 0; i < reader.FieldCount; i++)
                            row[columns[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                        rows.Add(row);
                    }

                    return new SqlExportResultDto { Columns = columns, Rows = rows, TotalRows = rows.Count };
                }
                finally
                {
                    DbConnectionHelper.RestoreDatabase(conn, originalDb);
                }
            }
            catch (OperationCanceledException)
            {
                throw new BizException("导出已取消", ClientClosedRequest);
            }
            catch (DbException) when (ct.IsCancellationRequested)
            {
                throw new BizException("导出已取消", ClientClosedRequest);
            }
            catch (DbException ex)
            {
                throw new BadRequestException($"SQL 执行错误：{ex.Message}");
            }
            catch (Exception ex)
            {
                throw new BizException($"导出异常：{ex.Message}", StatusCodes.Status500InternalServerError);
            }
        }

        public async Task<ExplainPlanDto> ExplainPlanAsync(SqlQueryRequest request)
        {
            var sql = PrepareSql(request);
            var fsql = _dataSourceService.Resolve(request.DataSource, out var dbType);

            try
            {
                // FreeSql 池化连接（取到即已打开，using 归还连接池）
                using var pooled = await fsql.Ado.MasterPool.GetAsync();
                var conn = pooled.Value;
                await DbConnectionHelper.ApplyNoLockAsync(conn, dbType);

                // 与 Execute 一致：切换到前端选中的数据库上下文（结束后还原）
                var originalDb = DbConnectionHelper.SwitchDatabase(conn, dbType, request.Database);
                try
                {
                    if (dbType != "sqlserver")
                    {
                        if (dbType == "oracle")
                            throw new BadRequestException("Oracle 暂不支持查看执行计划");
                        // MySQL/PostgreSQL/ClickHouse 用 EXPLAIN，SQLite 用 EXPLAIN QUERY PLAN
                        await using var exCmd = conn.CreateCommand();
                        exCmd.CommandText = (dbType == "sqlite" ? "EXPLAIN QUERY PLAN " : "EXPLAIN ") + sql;
                        exCmd.CommandTimeout = 30;
                        var exLines = new List<string>();
                        await using var exReader = await exCmd.ExecuteReaderAsync();
                        while (await exReader.ReadAsync())
                        {
                            var parts = new List<string>();
                            for (int i = 0; i < exReader.FieldCount; i++)
                                parts.Add(exReader.IsDBNull(i) ? "" : exReader.GetValue(i)?.ToString() ?? "");
                            exLines.Add(string.Join("  ", parts));
                        }
                        return new ExplainPlanDto { Plan = string.Join("\n", exLines) };
                    }

                    await using (var onCmd = conn.CreateCommand())
                    {
                        onCmd.CommandText = "SET SHOWPLAN_XML ON";
                        await onCmd.ExecuteNonQueryAsync();
                    }

                    await using var cmd = conn.CreateCommand();
                    cmd.CommandText = sql;
                    cmd.CommandTimeout = 30;
                    await using var reader = await cmd.ExecuteReaderAsync();

                    var xmlPlans = new List<string>();
                    while (await reader.ReadAsync())
                    {
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            if (!reader.IsDBNull(i))
                                xmlPlans.Add(reader.GetString(i));
                        }
                    }

                    // 尝试解析 XML 执行计划为结构化数据（解析失败时退化为纯文本显示）
                    return new ExplainPlanDto
                    {
                        Plan = string.Join("\n", xmlPlans),
                        Statements = ParseShowplanXml(xmlPlans)
                    };
                }
                finally
                {
                    DbConnectionHelper.RestoreDatabase(conn, originalDb);
                }
            }
            catch (DbException ex)
            {
                throw new BadRequestException($"执行计划获取失败：{ex.Message}");
            }
        }

        /// <summary>
        /// 取出并校验待执行的 SQL：内置本地数据源（名称 + 连接串双重校验）允许任意语句，
        /// 其余数据源仅允许 SELECT。
        /// </summary>
        private string PrepareSql(SqlQueryRequest request)
        {
            var sql = request.Sql?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(sql))
                throw new BadRequestException("SQL 不能为空");

            if (!_dataSourceService.IsFullAccessSource(request.DataSource))
            {
                var validationError = SqlSecurityValidator.Validate(sql);
                if (validationError != null)
                    throw new BadRequestException(validationError);
            }
            return sql;
        }

        /// <summary>解析 SQL Server SHOWPLAN_XML 输出为结构化执行计划（各语句开销、操作符详情）</summary>
        private static List<PlanStatementDto> ParseShowplanXml(List<string> xmlPlans)
        {
            var result = new List<PlanStatementDto>();
            try
            {
                foreach (var xmlStr in xmlPlans)
                {
                    var doc = XDocument.Parse(xmlStr);
                    var ns = doc.Root?.Name.Namespace ?? XNamespace.None;
                    var stmts = doc.Descendants(ns + "StmtSimple").ToList();
                    if (stmts.Count == 0) continue;

                    // 计算总开销用于各语句占比
                    var totalCost = stmts.Sum(s => ParseDouble(s.Attribute("StatementSubTreeCost")?.Value));

                    for (int i = 0; i < stmts.Count; i++)
                    {
                        var stmt = stmts[i];
                        var stmtCost = ParseDouble(stmt.Attribute("StatementSubTreeCost")?.Value);

                        // 提取操作符树
                        var operators = new List<PlanOperatorDto>();
                        foreach (var op in stmt.Descendants(ns + "RelOp"))
                        {
                            var opSubtreeCost = op.Attribute("EstimatedTotalSubtreeCost")?.Value ?? "";
                            var opCost = ParseDouble(opSubtreeCost);

                            // 尝试获取对象名（表/索引）
                            var objectNode = op.Descendants(ns + "Object").FirstOrDefault();
                            var objectName = "";
                            var objectFullName = "";
                            if (objectNode != null)
                            {
                                var db = objectNode.Attribute("Database")?.Value ?? "";
                                var schema = objectNode.Attribute("Schema")?.Value ?? "";
                                var tbl = objectNode.Attribute("Table")?.Value ?? "";
                                var idx = objectNode.Attribute("Index")?.Value ?? "";
                                var alias = objectNode.Attribute("Alias")?.Value ?? "";
                                objectName = !string.IsNullOrEmpty(idx) ? $"{tbl}.{idx}" : tbl;
                                // 完整对象名：db.schema.table.index alias
                                var parts = new List<string>();
                                if (!string.IsNullOrEmpty(db)) parts.Add(db);
                                if (!string.IsNullOrEmpty(schema)) parts.Add(schema);
                                if (!string.IsNullOrEmpty(tbl)) parts.Add(tbl);
                                if (!string.IsNullOrEmpty(idx)) parts.Add(idx);
                                objectFullName = string.Join(".", parts);
                                if (!string.IsNullOrEmpty(alias)) objectFullName += " " + alias;
                            }

                            // 输出列表
                            var outputCols = new List<string>();
                            var outputList = op.Element(ns + "OutputList");
                            if (outputList != null)
                            {
                                foreach (var colRef in outputList.Elements(ns + "ColumnReference"))
                                {
                                    var colParts = new List<string>();
                                    foreach (var attr in new[] { "Database", "Schema", "Table" })
                                    {
                                        var v = colRef.Attribute(attr)?.Value ?? "";
                                        if (!string.IsNullOrEmpty(v)) colParts.Add(v);
                                    }
                                    colParts.Add(colRef.Attribute("Column")?.Value ?? "");
                                    outputCols.Add(string.Join(".", colParts));
                                }
                            }

                            operators.Add(new PlanOperatorDto
                            {
                                PhysicalOp = op.Attribute("PhysicalOp")?.Value ?? "",
                                LogicalOp = op.Attribute("LogicalOp")?.Value ?? "",
                                CostPercent = stmtCost > 0 ? Math.Round(opCost / stmtCost * 100, 0) : 0,
                                EstimatedRows = op.Attribute("EstimateRows")?.Value ?? "",
                                EstimatedRowsRead = op.Attribute("EstimatedRowsRead")?.Value ?? "",
                                Executions = op.Attribute("EstimateExecutions")?.Value ?? "1",
                                ObjectName = objectName,
                                // 详细信息（用于悬浮提示）
                                NodeId = op.Attribute("NodeId")?.Value ?? "",
                                EstIoCost = op.Attribute("EstimateIO")?.Value ?? "",
                                EstCpuCost = op.Attribute("EstimateCPU")?.Value ?? "",
                                EstSubtreeCost = opSubtreeCost,
                                AvgRowSize = op.Attribute("AvgRowSize")?.Value ?? "",
                                ObjectFullName = objectFullName,
                                OutputColumns = outputCols,
                            });
                        }

                        result.Add(new PlanStatementDto
                        {
                            Index = i + 1,
                            Sql = stmt.Attribute("StatementText")?.Value?.Trim() ?? "",
                            CostPercent = totalCost > 0 ? Math.Round(stmtCost / totalCost * 100, 0) : 0,
                            SubtreeCost = Math.Round(stmtCost, 6),
                            EstimatedRows = stmt.Attribute("StatementEstRows")?.Value ?? "",
                            Operators = operators,
                        });
                    }
                }
            }
            catch { /* XML 解析失败时退化为纯文本显示 */ }
            return result;
        }

        /// <summary>解析执行计划中的开销数值（固定用不变文化，避免小数点风格差异）</summary>
        private static double ParseDouble(string? value) =>
            double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0;
    }
}

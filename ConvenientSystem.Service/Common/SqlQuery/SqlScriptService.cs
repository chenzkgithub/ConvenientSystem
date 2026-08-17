using ConvenientSystem.Shared.Common.Exceptions;
using ConvenientSystem.Shared.Common.Sql;
using ConvenientSystem.Shared.Model.Common;
using System.Data.Common;
using System.Text;

namespace ConvenientSystem.Service.Common.SqlQuery
{
    /// <summary>
    /// 数据库对象脚本生成服务实现：能取原生 DDL 的数据库（MySQL/Oracle/SQLite/ClickHouse）直接取，
    /// SQL Server 与 PostgreSQL 由系统视图元数据拼接。
    /// </summary>
    public class SqlScriptService : ISqlScriptService
    {
        private readonly IDataSourceService _dataSourceService;

        public SqlScriptService(IDataSourceService dataSourceService)
        {
            _dataSourceService = dataSourceService;
        }

        public async Task<SqlScriptDto> GetCreateScriptAsync(string dataSource, string database, string schema, string name, string type)
        {
            var fsql = _dataSourceService.Resolve(dataSource, out var dbType);
            if (string.IsNullOrWhiteSpace(database) || string.IsNullOrWhiteSpace(name))
                throw new BadRequestException("参数不完整");

            try
            {
                // FreeSql 池化连接（取到即已打开，using 归还连接池）
                using var pooled = await fsql.Ado.MasterPool.GetAsync();
                var conn = pooled.Value;
                await DbConnectionHelper.ApplyNoLockAsync(conn, dbType);

                if (dbType == "clickhouse")
                {
                    // ClickHouse：SHOW CREATE TABLE 对表/视图均有效
                    await using var chCmd = conn.CreateCommand();
                    chCmd.CommandText = $"SHOW CREATE TABLE {SqlEscape.ChIdent(database)}.{SqlEscape.ChIdent(name)}";
                    var chScript = await chCmd.ExecuteScalarAsync() as string;
                    if (string.IsNullOrEmpty(chScript))
                        throw new BadRequestException("未找到对象定义");
                    return new SqlScriptDto { Script = chScript };
                }

                if (dbType == "mysql")
                {
                    // MySQL：SHOW CREATE TABLE 对表/视图均有效；存储过程/函数用专用语句
                    await using var myCmd = conn.CreateCommand();
                    myCmd.CommandText = type?.ToLowerInvariant() switch
                    {
                        "proc" => $"SHOW CREATE PROCEDURE {SqlEscape.ChIdent(database)}.{SqlEscape.ChIdent(name)}",
                        "func" => $"SHOW CREATE FUNCTION {SqlEscape.ChIdent(database)}.{SqlEscape.ChIdent(name)}",
                        _ => $"SHOW CREATE TABLE {SqlEscape.ChIdent(database)}.{SqlEscape.ChIdent(name)}",
                    };
                    await using var myReader = await myCmd.ExecuteReaderAsync();
                    if (await myReader.ReadAsync())
                    {
                        // 结果列名形如 Create Table / Create View / Create Procedure，按列名定位
                        for (var i = 0; i < myReader.FieldCount; i++)
                        {
                            if (myReader.GetName(i).StartsWith("Create", StringComparison.OrdinalIgnoreCase) && !myReader.IsDBNull(i))
                                return new SqlScriptDto { Script = myReader.GetString(i) };
                        }
                    }
                    throw new BadRequestException("未找到对象定义");
                }

                if (dbType == "postgresql")
                {
                    var pgSchema = string.IsNullOrWhiteSpace(schema) ? "public" : schema;
                    await using var pgCmd = conn.CreateCommand();
                    var t = type?.ToLowerInvariant();
                    if (t == "view")
                    {
                        pgCmd.CommandText = $"SELECT 'CREATE OR REPLACE VIEW ' || '{SqlEscape.Literal(pgSchema)}.{SqlEscape.Literal(name)}' || ' AS\n' || pg_get_viewdef('{SqlEscape.QuoteIdent(pgSchema)}.{SqlEscape.QuoteIdent(name)}'::regclass, true)";
                    }
                    else if (t == "proc" || t == "func")
                    {
                        pgCmd.CommandText = $"SELECT pg_get_functiondef(p.oid) FROM pg_proc p JOIN pg_namespace n ON p.pronamespace = n.oid WHERE n.nspname = '{SqlEscape.Literal(pgSchema)}' AND p.proname = '{SqlEscape.Literal(name)}' LIMIT 1";
                    }
                    else
                    {
                        var pgScript = await BuildPgCreateTableScript(conn, pgSchema, name);
                        if (pgScript == null)
                            throw new BadRequestException("表不存在或无列信息");
                        return new SqlScriptDto { Script = pgScript };
                    }
                    var pgDef = await pgCmd.ExecuteScalarAsync() as string;
                    if (string.IsNullOrEmpty(pgDef))
                        throw new BadRequestException("未找到对象定义");
                    return new SqlScriptDto { Script = pgDef };
                }

                if (dbType == "oracle")
                {
                    // Oracle：DBMS_METADATA.GET_DDL 直接生成 DDL（database 即 owner）
                    var ddlType = type?.ToLowerInvariant() switch
                    {
                        "view" => "VIEW",
                        "proc" => "PROCEDURE",
                        "func" => "FUNCTION",
                        _ => "TABLE",
                    };
                    await using var oraCmd = conn.CreateCommand();
                    oraCmd.CommandText = $"SELECT DBMS_METADATA.GET_DDL('{ddlType}', '{SqlEscape.Literal(name)}', '{SqlEscape.Literal(database)}') FROM dual";
                    await using var oraReader = await oraCmd.ExecuteReaderAsync();
                    if (await oraReader.ReadAsync() && !await oraReader.IsDBNullAsync(0))
                        return new SqlScriptDto { Script = oraReader.GetString(0) };
                    throw new BadRequestException("未找到对象定义");
                }

                if (dbType == "sqlite")
                {
                    // SQLite：sqlite_master 保存了原始建表/建视图语句
                    await using var liteCmd = conn.CreateCommand();
                    liteCmd.CommandText = $"SELECT sql FROM sqlite_master WHERE name = '{SqlEscape.Literal(name)}'";
                    var liteScript = await liteCmd.ExecuteScalarAsync() as string;
                    if (string.IsNullOrEmpty(liteScript))
                        throw new BadRequestException("未找到对象定义");
                    return new SqlScriptDto { Script = liteScript + ";" };
                }

                if (string.IsNullOrWhiteSpace(schema))
                    throw new BadRequestException("参数不完整");

                var db = SqlEscape.Identifier(database);
                if (string.Equals(type, "table", StringComparison.OrdinalIgnoreCase))
                {
                    var script = await BuildCreateTableScript(conn, db, schema, name);
                    if (script == null)
                        throw new BadRequestException("表不存在或无列信息");
                    return new SqlScriptDto { Script = script };
                }

                // 视图/存储过程/函数：取原始 T-SQL 定义
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $@"
SELECT m.definition
FROM {db}.sys.sql_modules m
JOIN {db}.sys.objects o ON m.object_id = o.object_id
JOIN {db}.sys.schemas s ON o.schema_id = s.schema_id
WHERE s.name = @schema AND o.name = @name";
                DbConnectionHelper.AddParam(cmd, "@schema", schema);
                DbConnectionHelper.AddParam(cmd, "@name", name);
                var def = await cmd.ExecuteScalarAsync() as string;
                if (string.IsNullOrEmpty(def))
                    throw new BadRequestException("未找到对象定义（可能已加密）");
                return new SqlScriptDto { Script = def };
            }
            catch (DbException ex)
            {
                throw new BadRequestException($"生成脚本失败：{ex.Message}");
            }
        }

        public async Task<SqlStatementsDto> GetAlterScriptAsync(string dataSource, string database, string schema, string name)
        {
            var fsql = _dataSourceService.Resolve(dataSource, out var dbType);
            if (string.IsNullOrWhiteSpace(database) || string.IsNullOrWhiteSpace(name))
                throw new BadRequestException("参数不完整");

            try
            {
                // FreeSql 池化连接（取到即已打开，using 归还连接池）
                using var pooled = await fsql.Ado.MasterPool.GetAsync();
                var conn = pooled.Value;
                await DbConnectionHelper.ApplyNoLockAsync(conn, dbType);

                if (dbType == "postgresql" && string.IsNullOrWhiteSpace(schema)) schema = "public";
                if (dbType == "sqlserver" && string.IsNullOrWhiteSpace(schema))
                    throw new BadRequestException("参数不完整");

                var cols = await FetchColumnMetaAsync(conn, dbType, database, schema, name);
                if (cols.Count == 0)
                    throw new BadRequestException("表不存在或无列信息");
                return new SqlStatementsDto { Statements = BuildAlterStatements(dbType, database, schema, name, cols) };
            }
            catch (DbException ex)
            {
                throw new BadRequestException($"生成脚本失败：{ex.Message}");
            }
        }

        public async Task<SqlStatementsDto> GetAllScriptAsync(string dataSource, string database, string schema, string name)
        {
            var fsql = _dataSourceService.Resolve(dataSource, out var dbType);
            if (string.IsNullOrWhiteSpace(database) || string.IsNullOrWhiteSpace(name))
                throw new BadRequestException("参数不完整");

            try
            {
                // FreeSql 池化连接（取到即已打开，using 归还连接池）
                using var pooled = await fsql.Ado.MasterPool.GetAsync();
                var conn = pooled.Value;
                await DbConnectionHelper.ApplyNoLockAsync(conn, dbType);

                var statements = new List<string>();

                if (dbType == "clickhouse")
                {
                    // ClickHouse：SHOW CREATE TABLE 已包含引擎/排序键/注释等完整定义
                    await using var chCmd = conn.CreateCommand();
                    chCmd.CommandText = $"SHOW CREATE TABLE {SqlEscape.ChIdent(database)}.{SqlEscape.ChIdent(name)}";
                    var chScript = await chCmd.ExecuteScalarAsync() as string;
                    if (string.IsNullOrEmpty(chScript))
                        throw new BadRequestException("未找到对象定义");
                    statements.Add($"-- ========== 建表语句 ==========\n{chScript}");
                    return new SqlStatementsDto { Statements = statements };
                }

                if (dbType == "mysql")
                {
                    // 建表语句（含列注释/索引/外键/表注释）
                    await using (var myCmd = conn.CreateCommand())
                    {
                        myCmd.CommandText = $"SHOW CREATE TABLE {SqlEscape.ChIdent(database)}.{SqlEscape.ChIdent(name)}";
                        await using var r = await myCmd.ExecuteReaderAsync();
                        if (await r.ReadAsync())
                        {
                            for (var i = 0; i < r.FieldCount; i++)
                            {
                                if (r.GetName(i).StartsWith("Create", StringComparison.OrdinalIgnoreCase) && !r.IsDBNull(i))
                                {
                                    statements.Add($"-- ========== 建表语句（含索引/外键/注释） ==========\n{r.GetString(i)};");
                                    break;
                                }
                            }
                        }
                    }
                    if (statements.Count == 0)
                        throw new BadRequestException("未找到对象定义");
                    // 触发器（SHOW CREATE TABLE 不含）
                    await using (var trCmd = conn.CreateCommand())
                    {
                        trCmd.CommandText = $"SELECT trigger_name, action_timing, event_manipulation, action_statement FROM information_schema.triggers WHERE event_object_schema = '{SqlEscape.MyLiteral(database)}' AND event_object_table = '{SqlEscape.MyLiteral(name)}' ORDER BY trigger_name";
                        await using var r = await trCmd.ExecuteReaderAsync();
                        while (await r.ReadAsync())
                            statements.Add($"-- ========== 触发器 {r.GetString(0)} ==========\nDELIMITER $$\nCREATE TRIGGER {SqlEscape.ChIdent(r.GetString(0))} {r.GetString(1)} {r.GetString(2)} ON {SqlEscape.ChIdent(database)}.{SqlEscape.ChIdent(name)} FOR EACH ROW\n{r.GetString(3)}$$\nDELIMITER ;");
                    }
                    return new SqlStatementsDto { Statements = statements };
                }

                if (dbType == "sqlite")
                {
                    // sqlite_master 一次取全：表 + 索引 + 触发器原始定义
                    await using var sCmd = conn.CreateCommand();
                    sCmd.CommandText = $"SELECT type, name, sql FROM sqlite_master WHERE tbl_name = '{SqlEscape.Literal(name)}' AND sql IS NOT NULL ORDER BY CASE type WHEN 'table' THEN 0 WHEN 'index' THEN 1 ELSE 2 END, name";
                    await using var r = await sCmd.ExecuteReaderAsync();
                    while (await r.ReadAsync())
                    {
                        var kind = r.GetString(0) switch { "table" => "建表语句", "index" => "索引", "trigger" => "触发器", var other => other };
                        statements.Add($"-- ========== {kind} {r.GetString(1)} ==========\n{r.GetString(2)};");
                    }
                    if (statements.Count == 0)
                        throw new BadRequestException("未找到对象定义");
                    return new SqlStatementsDto { Statements = statements };
                }

                if (dbType == "postgresql")
                {
                    var pgSchema = string.IsNullOrWhiteSpace(schema) ? "public" : schema;
                    var pgCreate = await BuildPgCreateTableScript(conn, pgSchema, name);
                    if (string.IsNullOrEmpty(pgCreate))
                        throw new BadRequestException("未找到对象定义");
                    statements.Add($"-- ========== 建表语句 ==========\n{pgCreate}");
                    var pgTbl = $"{SqlEscape.QuoteIdent(pgSchema)}.{SqlEscape.QuoteIdent(name)}";
                    // 表注释 + 列注释
                    await using (var cCmd = conn.CreateCommand())
                    {
                        cCmd.CommandText = $"SELECT obj_description(('{SqlEscape.Literal(pgTbl)}')::regclass, 'pg_class')";
                        var tblComment = await cCmd.ExecuteScalarAsync() as string;
                        var sb = new StringBuilder("-- ========== 注释 ==========\n");
                        if (!string.IsNullOrEmpty(tblComment))
                            sb.AppendLine($"COMMENT ON TABLE {pgTbl} IS '{SqlEscape.Literal(tblComment)}';");
                        foreach (var c in await FetchColumnMetaAsync(conn, dbType, database, pgSchema, name))
                        {
                            if (!string.IsNullOrEmpty(c.Comment))
                                sb.AppendLine($"COMMENT ON COLUMN {pgTbl}.{SqlEscape.QuoteIdent(c.Name)} IS '{SqlEscape.Literal(c.Comment)}';");
                        }
                        var cText = sb.ToString().TrimEnd();
                        if (cText.Contains('\n')) statements.Add(cText);
                    }
                    // 索引（排除主键/唯一约束自动生成的索引）
                    await using (var iCmd = conn.CreateCommand())
                    {
                        iCmd.CommandText = $@"
SELECT indexname, indexdef FROM pg_indexes pi
WHERE schemaname = '{SqlEscape.Literal(pgSchema)}' AND tablename = '{SqlEscape.Literal(name)}'
  AND NOT EXISTS (SELECT 1 FROM pg_constraint con JOIN pg_class ci ON con.conindid = ci.oid
                  JOIN pg_namespace ni ON ci.relnamespace = ni.oid
                  WHERE ci.relname = pi.indexname AND ni.nspname = pi.schemaname)
ORDER BY indexname";
                        await using var r = await iCmd.ExecuteReaderAsync();
                        while (await r.ReadAsync())
                            statements.Add($"-- ========== 索引 {r.GetString(0)} ==========\n{r.GetString(1)};");
                    }
                    // 触发器（排除内部触发器）
                    await using (var tCmd = conn.CreateCommand())
                    {
                        tCmd.CommandText = $@"
SELECT t.tgname, pg_get_triggerdef(t.oid)
FROM pg_trigger t
JOIN pg_class c ON t.tgrelid = c.oid
JOIN pg_namespace n ON c.relnamespace = n.oid
WHERE n.nspname = '{SqlEscape.Literal(pgSchema)}' AND c.relname = '{SqlEscape.Literal(name)}' AND NOT t.tgisinternal
ORDER BY t.tgname";
                        await using var r = await tCmd.ExecuteReaderAsync();
                        while (await r.ReadAsync())
                            statements.Add($"-- ========== 触发器 {r.GetString(0)} ==========\n{r.GetString(1)};");
                    }
                    return new SqlStatementsDto { Statements = statements };
                }

                if (dbType == "oracle")
                {
                    // DBMS_METADATA：表 DDL + 各类依赖对象 DDL（无对应对象时会抛异常，逐类忽略）
                    await using (var dCmd = conn.CreateCommand())
                    {
                        dCmd.CommandText = $"SELECT DBMS_METADATA.GET_DDL('TABLE', '{SqlEscape.Literal(name)}', '{SqlEscape.Literal(database)}') FROM dual";
                        var ddl = (await dCmd.ExecuteScalarAsync())?.ToString();
                        if (string.IsNullOrWhiteSpace(ddl))
                            throw new BadRequestException("未找到对象定义");
                        statements.Add($"-- ========== 建表语句 ==========\n{ddl.Trim()}");
                    }
                    var depKinds = new (string Type, string Label)[] { ("INDEX", "索引"), ("CONSTRAINT", "约束"), ("REF_CONSTRAINT", "外键"), ("TRIGGER", "触发器"), ("COMMENT", "注释") };
                    foreach (var (depType, label) in depKinds)
                    {
                        try
                        {
                            await using var dep = conn.CreateCommand();
                            dep.CommandText = $"SELECT DBMS_METADATA.GET_DEPENDENT_DDL('{depType}', '{SqlEscape.Literal(name)}', '{SqlEscape.Literal(database)}') FROM dual";
                            var ddl = (await dep.ExecuteScalarAsync())?.ToString();
                            if (!string.IsNullOrWhiteSpace(ddl))
                                statements.Add($"-- ========== {label} ==========\n{ddl.Trim()}");
                        }
                        catch (DbException) { /* 该类依赖对象不存在时 DBMS_METADATA 抛 ORA-31608，忽略 */ }
                    }
                    return new SqlStatementsDto { Statements = statements };
                }

                // sqlserver
                if (string.IsNullOrWhiteSpace(schema))
                    throw new BadRequestException("参数不完整");
                statements = await BuildSqlServerAllStatementsAsync(conn, database, schema, name);
                if (statements.Count == 0)
                    throw new BadRequestException("未找到对象定义");
                return new SqlStatementsDto { Statements = statements };
            }
            catch (DbException ex)
            {
                throw new BadRequestException($"生成脚本失败：{ex.Message}");
            }
        }

        // ============ 列元数据与语句拼接 ============

        private sealed record ColMeta(string Name, string Type, bool Nullable, bool IsIdentity, string? Comment);

        /// <summary>获取表的列元数据（类型/可空/自增/注释），供生成修改语句与注释脚本使用</summary>
        private static async Task<List<ColMeta>> FetchColumnMetaAsync(DbConnection conn, string dbType, string database, string schema, string name)
        {
            var cols = new List<ColMeta>();
            await using var cmd = conn.CreateCommand();
            if (dbType == "mysql")
            {
                cmd.CommandText = $"SELECT column_name, column_type, is_nullable, extra, column_comment FROM information_schema.columns WHERE table_schema = '{SqlEscape.MyLiteral(database)}' AND table_name = '{SqlEscape.MyLiteral(name)}' ORDER BY ordinal_position";
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                    cols.Add(new ColMeta(r.GetString(0), r.GetString(1),
                        string.Equals(r.GetString(2), "YES", StringComparison.OrdinalIgnoreCase),
                        !r.IsDBNull(3) && r.GetString(3).Contains("auto_increment", StringComparison.OrdinalIgnoreCase),
                        r.IsDBNull(4) || r.GetString(4).Length == 0 ? null : r.GetString(4)));
                return cols;
            }
            if (dbType == "postgresql")
            {
                cmd.CommandText = $@"
SELECT c.column_name,
       CASE WHEN c.data_type = 'character varying' THEN 'varchar(' || COALESCE(c.character_maximum_length::text, '') || ')'
            WHEN c.data_type = 'character' THEN 'char(' || COALESCE(c.character_maximum_length::text, '') || ')'
            WHEN c.data_type = 'numeric' AND c.numeric_precision IS NOT NULL THEN 'numeric(' || c.numeric_precision || ',' || COALESCE(c.numeric_scale, 0) || ')'
            ELSE c.data_type END AS type_str,
       c.is_nullable,
       (c.is_identity = 'YES' OR COALESCE(c.column_default, '') LIKE 'nextval%') AS is_ident,
       col_description(('""' || replace(c.table_schema, '""', '""""') || '"".""' || replace(c.table_name, '""', '""""') || '""')::regclass, c.ordinal_position) AS col_comment
FROM information_schema.columns c
WHERE c.table_schema = '{SqlEscape.Literal(schema)}' AND c.table_name = '{SqlEscape.Literal(name)}'
ORDER BY c.ordinal_position";
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                    cols.Add(new ColMeta(r.GetString(0), r.GetString(1),
                        string.Equals(r.GetString(2), "YES", StringComparison.OrdinalIgnoreCase),
                        !r.IsDBNull(3) && r.GetBoolean(3),
                        r.IsDBNull(4) ? null : r.GetString(4)));
                return cols;
            }
            if (dbType == "oracle")
            {
                cmd.CommandText = $@"
SELECT col.column_name,
       col.data_type || CASE WHEN col.data_type IN ('VARCHAR2', 'NVARCHAR2', 'CHAR', 'NCHAR', 'RAW') THEN '(' || col.data_length || ')'
                             WHEN col.data_type = 'NUMBER' AND col.data_precision IS NOT NULL THEN '(' || col.data_precision || ',' || NVL(col.data_scale, 0) || ')'
                             ELSE '' END AS type_str,
       col.nullable, com.comments
FROM all_tab_columns col
LEFT JOIN all_col_comments com ON com.owner = col.owner AND com.table_name = col.table_name AND com.column_name = col.column_name
WHERE col.owner = '{SqlEscape.Literal(database)}' AND col.table_name = '{SqlEscape.Literal(name)}'
ORDER BY col.column_id";
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                    cols.Add(new ColMeta(r.GetString(0), r.GetString(1),
                        string.Equals(r.GetString(2), "Y", StringComparison.OrdinalIgnoreCase),
                        false, r.IsDBNull(3) ? null : r.GetString(3)));
                return cols;
            }
            if (dbType == "sqlite")
            {
                cmd.CommandText = $"PRAGMA table_info('{SqlEscape.Literal(name)}')";
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                    cols.Add(new ColMeta(r.GetString(1), r.IsDBNull(2) || r.GetString(2).Length == 0 ? "TEXT" : r.GetString(2),
                        Convert.ToInt32(r.GetValue(3)) == 0, false, null));
                return cols;
            }
            if (dbType == "clickhouse")
            {
                cmd.CommandText = $"SELECT name, type, comment FROM system.columns WHERE database = '{SqlEscape.ChString(database)}' AND table = '{SqlEscape.ChString(name)}' ORDER BY position";
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    var typeStr = r.GetString(1);
                    cols.Add(new ColMeta(r.GetString(0), typeStr,
                        typeStr.StartsWith("Nullable(", StringComparison.OrdinalIgnoreCase),
                        false, r.IsDBNull(2) || r.GetString(2).Length == 0 ? null : r.GetString(2)));
                }
                return cols;
            }
            // sqlserver：注释取扩展属性 MS_Description
            var db = SqlEscape.Identifier(database);
            cmd.CommandText = $@"
SELECT c.name, ty.name AS typeName, c.max_length, c.precision, c.scale, c.is_nullable, c.is_identity,
       CAST(ep.value AS NVARCHAR(4000)) AS col_comment
FROM {db}.sys.columns c
JOIN {db}.sys.objects o ON c.object_id = o.object_id
JOIN {db}.sys.schemas s ON o.schema_id = s.schema_id
JOIN {db}.sys.types ty ON c.user_type_id = ty.user_type_id
LEFT JOIN {db}.sys.extended_properties ep ON ep.class = 1 AND ep.major_id = c.object_id AND ep.minor_id = c.column_id AND ep.name = 'MS_Description'
WHERE s.name = @schema AND o.name = @name
ORDER BY c.column_id";
            DbConnectionHelper.AddParam(cmd, "@schema", schema);
            DbConnectionHelper.AddParam(cmd, "@name", name);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                cols.Add(new ColMeta(reader.GetString(0),
                    SqlEscape.FormatColumnType(reader.GetString(1), reader.GetInt16(2), reader.GetByte(3), reader.GetByte(4)),
                    reader.GetBoolean(5), reader.GetBoolean(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7)));
            return cols;
        }

        /// <summary>SQL Server 扩展属性注释语句（新增/更新列注释）；存储过程前加库名限定，保证在任意库上下文都作用到目标库</summary>
        private static string SqlServerCommentExec(string dbEscaped, string proc, string schema, string table, string column, string comment) =>
            $"EXEC {dbEscaped}.sys.{proc} @name=N'MS_Description', @value=N'{SqlEscape.Literal(comment)}', @level0type=N'SCHEMA', @level0name=N'{SqlEscape.Literal(schema)}', @level1type=N'TABLE', @level1name=N'{SqlEscape.Literal(table)}', @level2type=N'COLUMN', @level2name=N'{SqlEscape.Literal(column)}';";

        /// <summary>按数据库类型生成新增/修改/删除字段语句块（均带注释处理）</summary>
        private static List<string> BuildAlterStatements(string dbType, string database, string schema, string name, List<ColMeta> cols)
        {
            var list = new List<string>();
            if (dbType is "mysql" or "clickhouse")
            {
                var tbl = $"{SqlEscape.ChIdent(database)}.{SqlEscape.ChIdent(name)}";
                var addType = dbType == "mysql" ? "VARCHAR(50) NULL" : "String";
                list.Add($"-- 新增字段（模板：替换字段名/类型/注释后执行）\nALTER TABLE {tbl} ADD COLUMN `新字段名` {addType} COMMENT '字段注释';");
                foreach (var c in cols)
                {
                    var comment = dbType == "mysql" ? SqlEscape.MyLiteral(c.Comment ?? "字段注释") : SqlEscape.ChString(c.Comment ?? "字段注释");
                    var def = dbType == "mysql"
                        ? $"{c.Type} {(c.Nullable ? "NULL" : "NOT NULL")}{(c.IsIdentity ? " AUTO_INCREMENT" : "")}"
                        : c.Type;
                    list.Add($"-- 修改字段 {c.Name}（当前定义，按需调整后执行）\nALTER TABLE {tbl} MODIFY COLUMN {SqlEscape.ChIdent(c.Name)} {def} COMMENT '{comment}';");
                }
                var drop = new StringBuilder("-- 删除字段（按需取用）\n");
                foreach (var c in cols) drop.AppendLine($"ALTER TABLE {tbl} DROP COLUMN {SqlEscape.ChIdent(c.Name)};");
                list.Add(drop.ToString().TrimEnd());
                return list;
            }
            if (dbType == "postgresql")
            {
                var tbl = $"{SqlEscape.QuoteIdent(schema)}.{SqlEscape.QuoteIdent(name)}";
                list.Add($"-- 新增字段（模板：替换字段名/类型/注释后执行）\nALTER TABLE {tbl} ADD COLUMN \"新字段名\" varchar(50);\nCOMMENT ON COLUMN {tbl}.\"新字段名\" IS '字段注释';");
                foreach (var c in cols)
                {
                    var col = SqlEscape.QuoteIdent(c.Name);
                    list.Add($"-- 修改字段 {c.Name}（当前定义，按需调整后执行）\n" +
                             $"ALTER TABLE {tbl} ALTER COLUMN {col} TYPE {c.Type};\n" +
                             $"ALTER TABLE {tbl} ALTER COLUMN {col} {(c.Nullable ? "DROP NOT NULL" : "SET NOT NULL")};\n" +
                             $"COMMENT ON COLUMN {tbl}.{col} IS '{SqlEscape.Literal(c.Comment ?? "字段注释")}';");
                }
                var drop = new StringBuilder("-- 删除字段（按需取用）\n");
                foreach (var c in cols) drop.AppendLine($"ALTER TABLE {tbl} DROP COLUMN {SqlEscape.QuoteIdent(c.Name)};");
                list.Add(drop.ToString().TrimEnd());
                return list;
            }
            if (dbType == "oracle")
            {
                var tbl = $"{SqlEscape.QuoteIdent(database)}.{SqlEscape.QuoteIdent(name)}";
                list.Add($"-- 新增字段（模板：替换字段名/类型/注释后执行）\nALTER TABLE {tbl} ADD (\"新字段名\" VARCHAR2(50));\nCOMMENT ON COLUMN {tbl}.\"新字段名\" IS '字段注释';");
                foreach (var c in cols)
                {
                    var col = SqlEscape.QuoteIdent(c.Name);
                    list.Add($"-- 修改字段 {c.Name}（当前定义；可空性与当前一致时需去掉 NULL/NOT NULL，否则报 ORA-01442）\n" +
                             $"ALTER TABLE {tbl} MODIFY ({col} {c.Type} {(c.Nullable ? "NULL" : "NOT NULL")});\n" +
                             $"COMMENT ON COLUMN {tbl}.{col} IS '{SqlEscape.Literal(c.Comment ?? "字段注释")}';");
                }
                var drop = new StringBuilder("-- 删除字段（按需取用）\n");
                foreach (var c in cols) drop.AppendLine($"ALTER TABLE {tbl} DROP COLUMN {SqlEscape.QuoteIdent(c.Name)};");
                list.Add(drop.ToString().TrimEnd());
                return list;
            }
            if (dbType == "sqlite")
            {
                var tbl = SqlEscape.QuoteIdent(name);
                list.Add($"-- SQLite 不支持字段注释与修改类型（需重建表），仅支持新增/重命名/删除字段\n-- 新增字段（模板）\nALTER TABLE {tbl} ADD COLUMN \"新字段名\" TEXT;");
                var rename = new StringBuilder("-- 重命名字段（模板，按需取用）\n");
                foreach (var c in cols) rename.AppendLine($"ALTER TABLE {tbl} RENAME COLUMN {SqlEscape.QuoteIdent(c.Name)} TO \"新名称\";");
                list.Add(rename.ToString().TrimEnd());
                var drop = new StringBuilder("-- 删除字段（按需取用）\n");
                foreach (var c in cols) drop.AppendLine($"ALTER TABLE {tbl} DROP COLUMN {SqlEscape.QuoteIdent(c.Name)};");
                list.Add(drop.ToString().TrimEnd());
                return list;
            }
            // sqlserver：三段式命名（库.架构.表），保证在任意库上下文下执行都作用到目标库
            var dbEsc = SqlEscape.Identifier(database);
            var t = $"{dbEsc}.{SqlEscape.Identifier(schema)}.{SqlEscape.Identifier(name)}";
            list.Add($"-- 新增字段（模板：替换字段名/类型/注释后执行）\nALTER TABLE {t} ADD [新字段名] NVARCHAR(50) NULL;\n{SqlServerCommentExec(dbEsc, "sp_addextendedproperty", schema, name, "新字段名", "字段注释")}");
            foreach (var c in cols)
            {
                var sb = new StringBuilder();
                sb.AppendLine($"-- 修改字段 {c.Name}（当前定义，按需调整类型/可空后执行）");
                if (c.IsIdentity) sb.AppendLine($"-- 注意：{c.Name} 为自增列，自增属性本身无法通过 ALTER COLUMN 修改");
                sb.AppendLine($"ALTER TABLE {t} ALTER COLUMN {SqlEscape.Identifier(c.Name)} {c.Type} {(c.Nullable ? "NULL" : "NOT NULL")};");
                sb.Append(c.Comment != null
                    ? SqlServerCommentExec(dbEsc, "sp_updateextendedproperty", schema, name, c.Name, c.Comment)
                    : SqlServerCommentExec(dbEsc, "sp_addextendedproperty", schema, name, c.Name, "字段注释"));
                list.Add(sb.ToString());
            }
            var dropSql = new StringBuilder("-- 删除字段（按需取用；列上有默认值/索引等依赖时需先删除对应约束）\n");
            foreach (var c in cols) dropSql.AppendLine($"ALTER TABLE {t} DROP COLUMN {SqlEscape.Identifier(c.Name)};");
            list.Add(dropSql.ToString().TrimEnd());
            return list;
        }

        /// <summary>SQL Server：建表 + 默认值/CHECK/外键约束 + 索引 + 注释 + 触发器 全量语句</summary>
        private static async Task<List<string>> BuildSqlServerAllStatementsAsync(DbConnection conn, string database, string schema, string name)
        {
            var statements = new List<string>();
            var db = SqlEscape.Identifier(database);
            // 三段式命名（库.架构.表），保证在任意库上下文下执行都作用到目标库
            var tbl = $"{db}.{SqlEscape.Identifier(schema)}.{SqlEscape.Identifier(name)}";

            // 1. 建表语句（列/主键，复用建表脚本拼接）
            var create = await BuildCreateTableScript(conn, db, schema, name);
            if (string.IsNullOrEmpty(create)) return statements;
            statements.Add($"-- ========== 建表语句 ==========\n{create}");

            // 2. 默认值约束
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $@"
SELECT dc.name, c.name AS col_name, dc.definition
FROM {db}.sys.default_constraints dc
JOIN {db}.sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
JOIN {db}.sys.objects o ON dc.parent_object_id = o.object_id
JOIN {db}.sys.schemas s ON o.schema_id = s.schema_id
WHERE s.name = @schema AND o.name = @name
ORDER BY dc.name";
                DbConnectionHelper.AddParam(cmd, "@schema", schema);
                DbConnectionHelper.AddParam(cmd, "@name", name);
                var sb = new StringBuilder("-- ========== 默认值约束 ==========\n");
                var any = false;
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    sb.AppendLine($"ALTER TABLE {tbl} ADD CONSTRAINT {SqlEscape.Identifier(r.GetString(0))} DEFAULT {r.GetString(2)} FOR {SqlEscape.Identifier(r.GetString(1))};");
                    any = true;
                }
                if (any) statements.Add(sb.ToString().TrimEnd());
            }

            // 3. CHECK 约束
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $@"
SELECT cc.name, cc.definition
FROM {db}.sys.check_constraints cc
JOIN {db}.sys.objects o ON cc.parent_object_id = o.object_id
JOIN {db}.sys.schemas s ON o.schema_id = s.schema_id
WHERE s.name = @schema AND o.name = @name
ORDER BY cc.name";
                DbConnectionHelper.AddParam(cmd, "@schema", schema);
                DbConnectionHelper.AddParam(cmd, "@name", name);
                var sb = new StringBuilder("-- ========== CHECK 约束 ==========\n");
                var any = false;
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    sb.AppendLine($"ALTER TABLE {tbl} ADD CONSTRAINT {SqlEscape.Identifier(r.GetString(0))} CHECK {r.GetString(1)};");
                    any = true;
                }
                if (any) statements.Add(sb.ToString().TrimEnd());
            }

            // 4. 外键约束（多列外键按约束名分组拼接）
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $@"
SELECT fk.name, pc.name AS col_name, rs.name AS ref_schema, ro.name AS ref_table, rc.name AS ref_col,
       fk.delete_referential_action_desc, fk.update_referential_action_desc
FROM {db}.sys.foreign_keys fk
JOIN {db}.sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
JOIN {db}.sys.columns pc ON fkc.parent_object_id = pc.object_id AND fkc.parent_column_id = pc.column_id
JOIN {db}.sys.columns rc ON fkc.referenced_object_id = rc.object_id AND fkc.referenced_column_id = rc.column_id
JOIN {db}.sys.objects ro ON fkc.referenced_object_id = ro.object_id
JOIN {db}.sys.schemas rs ON ro.schema_id = rs.schema_id
JOIN {db}.sys.objects o ON fk.parent_object_id = o.object_id
JOIN {db}.sys.schemas s ON o.schema_id = s.schema_id
WHERE s.name = @schema AND o.name = @name
ORDER BY fk.name, fkc.constraint_column_id";
                DbConnectionHelper.AddParam(cmd, "@schema", schema);
                DbConnectionHelper.AddParam(cmd, "@name", name);
                // 约束名 → (父表列集合, 引用表, 引用列集合, 删除/更新行为)
                var fks = new Dictionary<string, (List<string> Cols, string RefTable, List<string> RefCols, string OnDelete, string OnUpdate)>();
                await using (var r = await cmd.ExecuteReaderAsync())
                {
                    while (await r.ReadAsync())
                    {
                        var fkName = r.GetString(0);
                        if (!fks.TryGetValue(fkName, out var fk))
                        {
                            fk = (new List<string>(), $"{db}.{SqlEscape.Identifier(r.GetString(2))}.{SqlEscape.Identifier(r.GetString(3))}", new List<string>(), r.GetString(5), r.GetString(6));
                            fks[fkName] = fk;
                        }
                        fk.Cols.Add(SqlEscape.Identifier(r.GetString(1)));
                        fk.RefCols.Add(SqlEscape.Identifier(r.GetString(4)));
                    }
                }
                if (fks.Count > 0)
                {
                    var sb = new StringBuilder("-- ========== 外键约束 ==========\n");
                    foreach (var (fkName, fk) in fks)
                    {
                        sb.Append($"ALTER TABLE {tbl} ADD CONSTRAINT {SqlEscape.Identifier(fkName)} FOREIGN KEY ({string.Join(", ", fk.Cols)}) REFERENCES {fk.RefTable} ({string.Join(", ", fk.RefCols)})");
                        if (fk.OnDelete != "NO_ACTION") sb.Append($" ON DELETE {fk.OnDelete.Replace('_', ' ')}");
                        if (fk.OnUpdate != "NO_ACTION") sb.Append($" ON UPDATE {fk.OnUpdate.Replace('_', ' ')}");
                        sb.AppendLine(";");
                    }
                    statements.Add(sb.ToString().TrimEnd());
                }
            }

            // 5. 索引（排除主键/唯一约束自动生成的索引）
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $@"
SELECT i.name, i.is_unique, i.type_desc, c.name AS col_name, ic.is_descending_key, ic.is_included_column
FROM {db}.sys.indexes i
JOIN {db}.sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
JOIN {db}.sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
JOIN {db}.sys.objects o ON i.object_id = o.object_id
JOIN {db}.sys.schemas s ON o.schema_id = s.schema_id
WHERE s.name = @schema AND o.name = @name AND i.is_primary_key = 0 AND i.is_unique_constraint = 0 AND i.type > 0
ORDER BY i.name, ic.key_ordinal";
                DbConnectionHelper.AddParam(cmd, "@schema", schema);
                DbConnectionHelper.AddParam(cmd, "@name", name);
                // 索引名 → (唯一, 类型, 键列集合, INCLUDE 列集合)
                var idxs = new Dictionary<string, (bool Unique, string TypeDesc, List<string> KeyCols, List<string> InclCols)>();
                await using (var r = await cmd.ExecuteReaderAsync())
                {
                    while (await r.ReadAsync())
                    {
                        var idxName = r.GetString(0);
                        if (!idxs.TryGetValue(idxName, out var idx))
                        {
                            idx = (r.GetBoolean(1), r.GetString(2), new List<string>(), new List<string>());
                            idxs[idxName] = idx;
                        }
                        if (r.GetBoolean(5))
                            idx.InclCols.Add(SqlEscape.Identifier(r.GetString(3)));
                        else
                            idx.KeyCols.Add(SqlEscape.Identifier(r.GetString(3)) + (r.GetBoolean(4) ? " DESC" : ""));
                    }
                }
                foreach (var (idxName, idx) in idxs)
                {
                    var sql = $"CREATE {(idx.Unique ? "UNIQUE " : "")}{idx.TypeDesc} INDEX {SqlEscape.Identifier(idxName)} ON {tbl} ({string.Join(", ", idx.KeyCols)})";
                    if (idx.InclCols.Count > 0) sql += $" INCLUDE ({string.Join(", ", idx.InclCols)})";
                    statements.Add($"-- ========== 索引 {idxName} ==========\n{sql};");
                }
            }

            // 6. 注释（表 + 列扩展属性 MS_Description）
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $@"
SELECT CAST(ep.value AS NVARCHAR(4000))
FROM {db}.sys.extended_properties ep
JOIN {db}.sys.objects o ON ep.major_id = o.object_id
JOIN {db}.sys.schemas s ON o.schema_id = s.schema_id
WHERE ep.class = 1 AND ep.minor_id = 0 AND ep.name = 'MS_Description' AND s.name = @schema AND o.name = @name";
                DbConnectionHelper.AddParam(cmd, "@schema", schema);
                DbConnectionHelper.AddParam(cmd, "@name", name);
                var tblComment = await cmd.ExecuteScalarAsync() as string;
                var sb = new StringBuilder("-- ========== 注释 ==========\n");
                var any = false;
                if (!string.IsNullOrEmpty(tblComment))
                {
                    sb.AppendLine($"EXEC {db}.sys.sp_addextendedproperty @name=N'MS_Description', @value=N'{SqlEscape.Literal(tblComment)}', @level0type=N'SCHEMA', @level0name=N'{SqlEscape.Literal(schema)}', @level1type=N'TABLE', @level1name=N'{SqlEscape.Literal(name)}';");
                    any = true;
                }
                foreach (var c in await FetchColumnMetaAsync(conn, "sqlserver", database, schema, name))
                {
                    if (string.IsNullOrEmpty(c.Comment)) continue;
                    sb.AppendLine(SqlServerCommentExec(db, "sp_addextendedproperty", schema, name, c.Name, c.Comment));
                    any = true;
                }
                if (any) statements.Add(sb.ToString().TrimEnd());
            }

            // 7. 触发器（原始定义）
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $@"
SELECT tr.name, m.definition
FROM {db}.sys.triggers tr
JOIN {db}.sys.sql_modules m ON tr.object_id = m.object_id
JOIN {db}.sys.objects o ON tr.parent_id = o.object_id
JOIN {db}.sys.schemas s ON o.schema_id = s.schema_id
WHERE s.name = @schema AND o.name = @name
ORDER BY tr.name";
                DbConnectionHelper.AddParam(cmd, "@schema", schema);
                DbConnectionHelper.AddParam(cmd, "@name", name);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                    statements.Add($"-- ========== 触发器 {r.GetString(0)} ==========\n{r.GetString(1).Trim()}");
            }

            return statements;
        }

        /// <summary>由 information_schema 拼接 PostgreSQL 简版 CREATE TABLE 脚本（含类型/可空/主键）</summary>
        private static async Task<string?> BuildPgCreateTableScript(DbConnection conn, string schema, string name)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
SELECT c.column_name,
       CASE WHEN c.data_type = 'character varying' THEN 'varchar(' || COALESCE(c.character_maximum_length::text, '') || ')'
            WHEN c.data_type = 'character' THEN 'char(' || COALESCE(c.character_maximum_length::text, '') || ')'
            WHEN c.data_type = 'numeric' AND c.numeric_precision IS NOT NULL THEN 'numeric(' || c.numeric_precision || ',' || COALESCE(c.numeric_scale, 0) || ')'
            ELSE c.data_type END AS type_str,
       c.is_nullable,
       COALESCE(c.column_default, '') AS col_default,
       (SELECT COUNT(*) FROM information_schema.table_constraints tc
        JOIN information_schema.key_column_usage kcu
          ON tc.constraint_name = kcu.constraint_name AND tc.table_schema = kcu.table_schema
        WHERE tc.constraint_type = 'PRIMARY KEY' AND tc.table_schema = c.table_schema
          AND tc.table_name = c.table_name AND kcu.column_name = c.column_name) AS pk_count
FROM information_schema.columns c
WHERE c.table_schema = '{SqlEscape.Literal(schema)}' AND c.table_name = '{SqlEscape.Literal(name)}'
ORDER BY c.ordinal_position";

            var colLines = new List<string>();
            var pkCols = new List<string>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var colName = reader.GetString(0);
                var line = $"    {SqlEscape.QuoteIdent(colName)} {reader.GetString(1)}";
                var colDefault = reader.GetString(3);
                if (!string.IsNullOrEmpty(colDefault))
                    line += $" DEFAULT {colDefault}";
                line += reader.GetString(2) == "YES" ? " NULL" : " NOT NULL";
                colLines.Add(line);
                if (Convert.ToInt32(reader.GetValue(4)) > 0)
                    pkCols.Add(colName);
            }

            if (colLines.Count == 0)
                return null;

            if (pkCols.Count > 0)
                colLines.Add($"    PRIMARY KEY ({string.Join(", ", pkCols.Select(SqlEscape.QuoteIdent))})");

            return $"CREATE TABLE {SqlEscape.QuoteIdent(schema)}.{SqlEscape.QuoteIdent(name)} (\n{string.Join(",\n", colLines)}\n);";
        }

        /// <summary>由列元数据拼接 CREATE TABLE 脚本（含类型/可空/自增/主键）</summary>
        private static async Task<string?> BuildCreateTableScript(DbConnection conn, string dbEscaped, string schema, string name)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
SELECT c.name, ty.name AS typeName, c.max_length, c.precision, c.scale, c.is_nullable, c.is_identity,
       ISNULL(pkc.key_ordinal, 0) AS pk_ordinal
FROM {dbEscaped}.sys.columns c
JOIN {dbEscaped}.sys.objects o ON c.object_id = o.object_id
JOIN {dbEscaped}.sys.schemas s ON o.schema_id = s.schema_id
JOIN {dbEscaped}.sys.types ty ON c.user_type_id = ty.user_type_id
LEFT JOIN (
    SELECT ic.object_id, ic.column_id, ic.key_ordinal
    FROM {dbEscaped}.sys.index_columns ic
    JOIN {dbEscaped}.sys.indexes i ON ic.object_id = i.object_id AND ic.index_id = i.index_id
    WHERE i.is_primary_key = 1
) pkc ON pkc.object_id = c.object_id AND pkc.column_id = c.column_id
WHERE s.name = @schema AND o.name = @name
ORDER BY c.column_id";
            DbConnectionHelper.AddParam(cmd, "@schema", schema);
            DbConnectionHelper.AddParam(cmd, "@name", name);

            var colLines = new List<string>();
            var pkCols = new List<(int Ordinal, string Name)>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var colName = reader.GetString(0);
                var typeStr = SqlEscape.FormatColumnType(reader.GetString(1), reader.GetInt16(2), reader.GetByte(3), reader.GetByte(4));
                var line = $"    {SqlEscape.Identifier(colName)} {typeStr}";
                if (reader.GetBoolean(6))
                    line += " IDENTITY(1,1)";
                line += reader.GetBoolean(5) ? " NULL" : " NOT NULL";
                colLines.Add(line);

                var pkOrdinal = Convert.ToInt32(reader.GetValue(7));
                if (pkOrdinal > 0)
                    pkCols.Add((pkOrdinal, colName));
            }

            if (colLines.Count == 0)
                return null;

            if (pkCols.Count > 0)
            {
                var pkList = string.Join(", ", pkCols.OrderBy(p => p.Ordinal).Select(p => SqlEscape.Identifier(p.Name)));
                colLines.Add($"    CONSTRAINT {SqlEscape.Identifier($"PK_{name}")} PRIMARY KEY ({pkList})");
            }

            return $"CREATE TABLE {dbEscaped}.{SqlEscape.Identifier(schema)}.{SqlEscape.Identifier(name)} (\n{string.Join(",\n", colLines)}\n);";
        }
    }
}

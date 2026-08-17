using ConvenientSystem.Shared.Common.Exceptions;
using ConvenientSystem.Shared.Common.Sql;
using ConvenientSystem.Shared.Model.Common;
using System.Data.Common;

namespace ConvenientSystem.Service.Common.SqlQuery
{
    /// <summary>
    /// 数据库对象浏览服务实现：统一走 FreeSql 池化连接读取各库的元数据视图。
    /// SQL Server 用参数化查询，其余库因驱动参数风格差异沿用字面量转义。
    /// </summary>
    public class SchemaService : ISchemaService
    {
        private readonly IDataSourceService _dataSourceService;

        public SchemaService(IDataSourceService dataSourceService)
        {
            _dataSourceService = dataSourceService;
        }

        public async Task<DatabaseListDto> GetDatabasesAsync(string dataSource)
        {
            var fsql = _dataSourceService.Resolve(dataSource, out var dbType);
            // 从连接串中提取默认数据库名
            var defaultDb = _dataSourceService.GetDefaultDatabase(dataSource);

            try
            {
                // FreeSql 池化连接（取到即已打开，using 归还连接池）
                using var pooled = await fsql.Ado.MasterPool.GetAsync();
                var conn = pooled.Value;
                await DbConnectionHelper.ApplyNoLockAsync(conn, dbType);
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = dbType switch
                {
                    "clickhouse" => "SELECT name FROM system.databases ORDER BY name",
                    "mysql" => "SELECT schema_name FROM information_schema.schemata WHERE schema_name NOT IN ('information_schema','performance_schema','sys') ORDER BY schema_name",
                    "postgresql" => "SELECT current_database()", // PG 连接绑定单库，只展示当前库
                    "oracle" => "SELECT username FROM all_users ORDER BY username", // Oracle 以 schema（用户）为库
                    "sqlite" => "SELECT 'main'",
                    _ => "SELECT name FROM sys.databases WHERE state = 0 ORDER BY CASE WHEN database_id <= 4 THEN 0 ELSE 1 END, name"
                };
                var list = new List<string>();
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    list.Add(reader.GetString(0));
                return new DatabaseListDto { Databases = list, DefaultDatabase = defaultDb };
            }
            catch (DbException ex)
            {
                throw new BadRequestException($"获取数据库列表失败：{ex.Message}");
            }
        }

        public async Task<SchemaObjectsDto> GetObjectsAsync(string dataSource, string database)
        {
            var fsql = _dataSourceService.Resolve(dataSource, out var dbType);
            if (string.IsNullOrWhiteSpace(database))
                throw new BadRequestException("数据库名不能为空");

            try
            {
                // FreeSql 池化连接（取到即已打开，using 归还连接池）
                using var pooled = await fsql.Ado.MasterPool.GetAsync();
                var conn = pooled.Value;
                await DbConnectionHelper.ApplyNoLockAsync(conn, dbType);

                var result = new SchemaObjectsDto();

                if (dbType == "clickhouse")
                {
                    // ClickHouse：无 schema 概念，用库名充当 schema；无存储过程
                    await using var chCmd = conn.CreateCommand();
                    chCmd.CommandText = $"SELECT name, engine, comment FROM system.tables WHERE database = '{SqlEscape.ChString(database)}' ORDER BY name";
                    await using var chReader = await chCmd.ExecuteReaderAsync();
                    while (await chReader.ReadAsync())
                    {
                        var item = new SchemaObjectItemDto { Schema = database, Name = chReader.GetString(0), Description = chReader.IsDBNull(2) ? null : chReader.GetString(2) };
                        if (chReader.GetString(1).Contains("View", StringComparison.OrdinalIgnoreCase)) result.Views.Add(item);
                        else result.Tables.Add(item);
                    }
                    return result;
                }

                if (dbType == "mysql")
                {
                    var dbEsc = SqlEscape.MyLiteral(database);
                    await using (var tCmd = conn.CreateCommand())
                    {
                        tCmd.CommandText = $"SELECT table_name, table_type, table_comment FROM information_schema.tables WHERE table_schema = '{dbEsc}' ORDER BY table_name";
                        await using var r = await tCmd.ExecuteReaderAsync();
                        while (await r.ReadAsync())
                        {
                            var item = new SchemaObjectItemDto { Schema = database, Name = r.GetString(0), Description = r.IsDBNull(2) ? null : r.GetString(2) };
                            if (r.GetString(1).Contains("VIEW", StringComparison.OrdinalIgnoreCase)) result.Views.Add(item);
                            else result.Tables.Add(item);
                        }
                    }
                    await using (var rCmd = conn.CreateCommand())
                    {
                        rCmd.CommandText = $"SELECT routine_name, routine_type, routine_comment FROM information_schema.routines WHERE routine_schema = '{dbEsc}' ORDER BY routine_name";
                        await using var r = await rCmd.ExecuteReaderAsync();
                        while (await r.ReadAsync())
                        {
                            var item = new SchemaObjectItemDto { Schema = database, Name = r.GetString(0), Description = r.IsDBNull(2) ? null : r.GetString(2) };
                            if (string.Equals(r.GetString(1), "PROCEDURE", StringComparison.OrdinalIgnoreCase)) result.Procedures.Add(item);
                            else result.Functions.Add(item);
                        }
                    }
                    return result;
                }

                if (dbType == "postgresql")
                {
                    await using (var tCmd = conn.CreateCommand())
                    {
                        tCmd.CommandText = $@"
SELECT table_schema, table_name, table_type,
       obj_description((quote_ident(table_schema) || '.' || quote_ident(table_name))::regclass, 'pg_class') AS table_comment
FROM information_schema.tables
WHERE table_schema NOT IN ('pg_catalog','information_schema')
ORDER BY table_schema, table_name";
                        await using var r = await tCmd.ExecuteReaderAsync();
                        while (await r.ReadAsync())
                        {
                            var item = new SchemaObjectItemDto { Schema = r.GetString(0), Name = r.GetString(1), Description = r.IsDBNull(3) ? null : r.GetString(3) };
                            if (r.GetString(2).Contains("VIEW", StringComparison.OrdinalIgnoreCase)) result.Views.Add(item);
                            else result.Tables.Add(item);
                        }
                    }
                    await using (var rCmd = conn.CreateCommand())
                    {
                        rCmd.CommandText = "SELECT routine_schema, routine_name, routine_type FROM information_schema.routines WHERE routine_schema NOT IN ('pg_catalog','information_schema') ORDER BY routine_schema, routine_name";
                        await using var r = await rCmd.ExecuteReaderAsync();
                        while (await r.ReadAsync())
                        {
                            var item = new SchemaObjectItemDto { Schema = r.GetString(0), Name = r.GetString(1) };
                            if (string.Equals(r.GetString(2), "PROCEDURE", StringComparison.OrdinalIgnoreCase)) result.Procedures.Add(item);
                            else result.Functions.Add(item);
                        }
                    }
                    return result;
                }

                if (dbType == "oracle")
                {
                    // Oracle：database 参数即 schema（用户）
                    await using var oCmd = conn.CreateCommand();
                    oCmd.CommandText = $@"
SELECT ao.object_name, ao.object_type, tc.comments
FROM all_objects ao
LEFT JOIN all_tab_comments tc ON tc.owner = ao.owner AND tc.table_name = ao.object_name
WHERE ao.owner = '{SqlEscape.Literal(database)}' AND ao.object_type IN ('TABLE','VIEW','PROCEDURE','FUNCTION')
ORDER BY ao.object_name";
                    await using var oReader = await oCmd.ExecuteReaderAsync();
                    while (await oReader.ReadAsync())
                    {
                        var item = new SchemaObjectItemDto { Schema = database, Name = oReader.GetString(0), Description = oReader.IsDBNull(2) ? null : oReader.GetString(2) };
                        switch (oReader.GetString(1))
                        {
                            case "TABLE": result.Tables.Add(item); break;
                            case "VIEW": result.Views.Add(item); break;
                            case "PROCEDURE": result.Procedures.Add(item); break;
                            default: result.Functions.Add(item); break;
                        }
                    }
                    return result;
                }

                if (dbType == "sqlite")
                {
                    await using var sCmd = conn.CreateCommand();
                    sCmd.CommandText = "SELECT name, type FROM sqlite_master WHERE type IN ('table','view') AND name NOT LIKE 'sqlite_%' ORDER BY name";
                    await using var sReader = await sCmd.ExecuteReaderAsync();
                    while (await sReader.ReadAsync())
                    {
                        var item = new SchemaObjectItemDto { Schema = "main", Name = sReader.GetString(0) };
                        if (string.Equals(sReader.GetString(1), "view", StringComparison.OrdinalIgnoreCase)) result.Views.Add(item);
                        else result.Tables.Add(item);
                    }
                    return result;
                }

                var db = SqlEscape.Identifier(database);
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $@"
SELECT s.name AS sch, o.name, RTRIM(o.type) AS type, CAST(ep.value AS NVARCHAR(500)) AS description
FROM {db}.sys.objects o
JOIN {db}.sys.schemas s ON o.schema_id = s.schema_id
LEFT JOIN {db}.sys.extended_properties ep ON ep.class = 1 AND ep.major_id = o.object_id AND ep.minor_id = 0 AND ep.name = N'MS_Description'
WHERE o.type IN ('U','V','P','FN','IF','TF') AND o.is_ms_shipped = 0
ORDER BY s.name, o.name";

                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var item = new SchemaObjectItemDto { Schema = reader.GetString(0), Name = reader.GetString(1), Description = reader.IsDBNull(3) ? null : reader.GetString(3) };
                    switch (reader.GetString(2))
                    {
                        case "U": result.Tables.Add(item); break;
                        case "V": result.Views.Add(item); break;
                        case "P": result.Procedures.Add(item); break;
                        default: result.Functions.Add(item); break; // FN/IF/TF
                    }
                }
                return result;
            }
            catch (DbException ex)
            {
                throw new BadRequestException($"获取对象列表失败：{ex.Message}");
            }
        }

        public async Task<SchemaColumnListDto> GetColumnsAsync(string dataSource, string database, string schema, string name)
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

                var result = new SchemaColumnListDto();

                if (dbType == "clickhouse")
                {
                    await using var chCmd = conn.CreateCommand();
                    chCmd.CommandText = $"SELECT name, type, is_in_primary_key, comment FROM system.columns WHERE database = '{SqlEscape.ChString(database)}' AND table = '{SqlEscape.ChString(name)}' ORDER BY position";
                    await using var chReader = await chCmd.ExecuteReaderAsync();
                    while (await chReader.ReadAsync())
                    {
                        var typeStr = chReader.GetString(1);
                        result.Columns.Add(new SchemaColumnDto
                        {
                            Name = chReader.GetString(0),
                            Type = typeStr,
                            Nullable = typeStr.StartsWith("Nullable(", StringComparison.OrdinalIgnoreCase),
                            IsPk = Convert.ToInt32(chReader.GetValue(2)) == 1,
                            Description = chReader.IsDBNull(3) ? null : chReader.GetString(3)
                        });
                    }
                    return result;
                }

                if (dbType == "mysql")
                {
                    await using var myCmd = conn.CreateCommand();
                    myCmd.CommandText = $"SELECT column_name, column_type, is_nullable, column_key, column_comment FROM information_schema.columns WHERE table_schema = '{SqlEscape.MyLiteral(database)}' AND table_name = '{SqlEscape.MyLiteral(name)}' ORDER BY ordinal_position";
                    await using var r = await myCmd.ExecuteReaderAsync();
                    while (await r.ReadAsync())
                    {
                        result.Columns.Add(new SchemaColumnDto
                        {
                            Name = r.GetString(0),
                            Type = r.GetString(1),
                            Nullable = string.Equals(r.GetString(2), "YES", StringComparison.OrdinalIgnoreCase),
                            IsPk = string.Equals(r.GetString(3), "PRI", StringComparison.OrdinalIgnoreCase),
                            Description = r.IsDBNull(4) ? null : r.GetString(4)
                        });
                    }
                    return result;
                }

                if (dbType == "postgresql")
                {
                    var sch = string.IsNullOrWhiteSpace(schema) ? "public" : schema;
                    await using var pgCmd = conn.CreateCommand();
                    pgCmd.CommandText = $@"
SELECT c.column_name, c.data_type, c.is_nullable,
       (SELECT COUNT(*) FROM information_schema.table_constraints tc
        JOIN information_schema.key_column_usage kcu
          ON tc.constraint_name = kcu.constraint_name AND tc.table_schema = kcu.table_schema
        WHERE tc.constraint_type = 'PRIMARY KEY' AND tc.table_schema = c.table_schema
          AND tc.table_name = c.table_name AND kcu.column_name = c.column_name) AS pk,
       col_description((quote_ident(c.table_schema) || '.' || quote_ident(c.table_name))::regclass, c.ordinal_position) AS col_comment
FROM information_schema.columns c
WHERE c.table_schema = '{SqlEscape.Literal(sch)}' AND c.table_name = '{SqlEscape.Literal(name)}'
ORDER BY c.ordinal_position";
                    await using var r = await pgCmd.ExecuteReaderAsync();
                    while (await r.ReadAsync())
                    {
                        result.Columns.Add(new SchemaColumnDto
                        {
                            Name = r.GetString(0),
                            Type = r.GetString(1),
                            Nullable = string.Equals(r.GetString(2), "YES", StringComparison.OrdinalIgnoreCase),
                            IsPk = Convert.ToInt32(r.GetValue(3)) > 0,
                            Description = r.IsDBNull(4) ? null : r.GetString(4)
                        });
                    }
                    return result;
                }

                if (dbType == "oracle")
                {
                    await using var oCmd = conn.CreateCommand();
                    oCmd.CommandText = $@"
SELECT col.column_name, col.data_type, col.nullable,
  (SELECT COUNT(*) FROM all_constraints ac
   JOIN all_cons_columns acc ON ac.constraint_name = acc.constraint_name AND ac.owner = acc.owner
   WHERE ac.constraint_type = 'P' AND ac.owner = col.owner
     AND ac.table_name = col.table_name AND acc.column_name = col.column_name) AS pk,
  cc.comments AS col_comment
FROM all_tab_columns col
LEFT JOIN all_col_comments cc ON cc.owner = col.owner AND cc.table_name = col.table_name AND cc.column_name = col.column_name
WHERE col.owner = '{SqlEscape.Literal(database)}' AND col.table_name = '{SqlEscape.Literal(name)}'
ORDER BY col.column_id";
                    await using var r = await oCmd.ExecuteReaderAsync();
                    while (await r.ReadAsync())
                    {
                        result.Columns.Add(new SchemaColumnDto
                        {
                            Name = r.GetString(0),
                            Type = r.GetString(1),
                            Nullable = string.Equals(r.GetString(2), "Y", StringComparison.OrdinalIgnoreCase),
                            IsPk = Convert.ToInt32(r.GetValue(3)) > 0,
                            Description = r.IsDBNull(4) ? null : r.GetString(4)
                        });
                    }
                    return result;
                }

                if (dbType == "sqlite")
                {
                    await using var sCmd = conn.CreateCommand();
                    sCmd.CommandText = $"PRAGMA table_info('{SqlEscape.Literal(name)}')";
                    await using var r = await sCmd.ExecuteReaderAsync();
                    while (await r.ReadAsync())
                    {
                        // cid(0), name(1), type(2), notnull(3), dflt_value(4), pk(5)
                        result.Columns.Add(new SchemaColumnDto
                        {
                            Name = r.GetString(1),
                            Type = r.IsDBNull(2) ? "" : r.GetString(2),
                            Nullable = Convert.ToInt32(r.GetValue(3)) == 0,
                            IsPk = Convert.ToInt32(r.GetValue(5)) > 0
                        });
                    }
                    return result;
                }

                if (string.IsNullOrWhiteSpace(schema))
                    throw new BadRequestException("参数不完整");

                var db = SqlEscape.Identifier(database);
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $@"
SELECT c.name, ty.name AS typeName, c.max_length, c.precision, c.scale, c.is_nullable,
       CASE WHEN pkc.column_id IS NOT NULL THEN 1 ELSE 0 END AS is_pk,
       CAST(ep.value AS NVARCHAR(500)) AS col_comment
FROM {db}.sys.columns c
JOIN {db}.sys.objects o ON c.object_id = o.object_id
JOIN {db}.sys.schemas s ON o.schema_id = s.schema_id
JOIN {db}.sys.types ty ON c.user_type_id = ty.user_type_id
LEFT JOIN (
    SELECT ic.object_id, ic.column_id
    FROM {db}.sys.index_columns ic
    JOIN {db}.sys.indexes i ON ic.object_id = i.object_id AND ic.index_id = i.index_id
    WHERE i.is_primary_key = 1
) pkc ON pkc.object_id = c.object_id AND pkc.column_id = c.column_id
LEFT JOIN {db}.sys.extended_properties ep ON ep.class = 1 AND ep.major_id = c.object_id AND ep.minor_id = c.column_id AND ep.name = N'MS_Description'
WHERE s.name = @schema AND o.name = @name
ORDER BY c.column_id";
                DbConnectionHelper.AddParam(cmd, "@schema", schema);
                DbConnectionHelper.AddParam(cmd, "@name", name);

                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    result.Columns.Add(new SchemaColumnDto
                    {
                        Name = reader.GetString(0),
                        Type = SqlEscape.FormatColumnType(reader.GetString(1), reader.GetInt16(2), reader.GetByte(3), reader.GetByte(4)),
                        Nullable = reader.GetBoolean(5),
                        IsPk = reader.GetInt32(6) == 1,
                        Description = reader.IsDBNull(7) ? null : reader.GetString(7)
                    });
                }
                return result;
            }
            catch (DbException ex)
            {
                throw new BadRequestException($"获取列信息失败：{ex.Message}");
            }
        }

        public async Task<TableChildrenDto> GetTableChildrenAsync(string dataSource, string database, string schema, string name, string kind)
        {
            var fsql = _dataSourceService.Resolve(dataSource, out var dbType);
            if (string.IsNullOrWhiteSpace(database) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(kind))
                throw new BadRequestException("参数不完整");

            try
            {
                // FreeSql 池化连接（取到即已打开，using 归还连接池）
                using var pooled = await fsql.Ado.MasterPool.GetAsync();
                var conn = pooled.Value;
                await DbConnectionHelper.ApplyNoLockAsync(conn, dbType);

                kind = kind.Trim().ToLowerInvariant();
                string? sql = null;
                var useParams = false; // sqlserver 用参数化，其余库沿用字面量转义风格

                if (dbType == "mysql")
                {
                    var dbEsc = SqlEscape.MyLiteral(database);
                    var nameEsc = SqlEscape.MyLiteral(name);
                    sql = kind switch
                    {
                        "keys" => $"SELECT constraint_name, CASE constraint_type WHEN 'PRIMARY KEY' THEN 'PK' WHEN 'FOREIGN KEY' THEN 'FK' ELSE 'UQ' END FROM information_schema.table_constraints WHERE table_schema = '{dbEsc}' AND table_name = '{nameEsc}' AND constraint_type IN ('PRIMARY KEY','UNIQUE','FOREIGN KEY') ORDER BY 2, 1",
                        "constraints" => $"SELECT constraint_name, 'CHECK' FROM information_schema.table_constraints WHERE table_schema = '{dbEsc}' AND table_name = '{nameEsc}' AND constraint_type = 'CHECK' ORDER BY 1",
                        "triggers" => $"SELECT trigger_name, CONCAT(action_timing, ' ', event_manipulation) FROM information_schema.triggers WHERE event_object_schema = '{dbEsc}' AND event_object_table = '{nameEsc}' ORDER BY trigger_name",
                        "indexes" => $"SELECT index_name, CASE WHEN MIN(non_unique) = 0 THEN 'unique' ELSE NULL END FROM information_schema.statistics WHERE table_schema = '{dbEsc}' AND table_name = '{nameEsc}' GROUP BY index_name ORDER BY index_name",
                        _ => null,
                    };
                }
                else if (dbType == "postgresql")
                {
                    var sch = SqlEscape.Literal(string.IsNullOrWhiteSpace(schema) ? "public" : schema);
                    var nameEsc = SqlEscape.Literal(name);
                    sql = kind switch
                    {
                        "keys" => $"SELECT constraint_name, CASE constraint_type WHEN 'PRIMARY KEY' THEN 'PK' WHEN 'FOREIGN KEY' THEN 'FK' ELSE 'UQ' END FROM information_schema.table_constraints WHERE table_schema = '{sch}' AND table_name = '{nameEsc}' AND constraint_type IN ('PRIMARY KEY','UNIQUE','FOREIGN KEY') ORDER BY 2, 1",
                        // 排除 NOT NULL 自动生成的 CHECK 约束
                        "constraints" => $"SELECT constraint_name, 'CHECK' FROM information_schema.table_constraints WHERE table_schema = '{sch}' AND table_name = '{nameEsc}' AND constraint_type = 'CHECK' AND constraint_name NOT LIKE '%_not_null' ORDER BY 1",
                        "triggers" => $"SELECT t.tgname, NULL::text FROM pg_trigger t JOIN pg_class c ON t.tgrelid = c.oid JOIN pg_namespace ns ON c.relnamespace = ns.oid WHERE ns.nspname = '{sch}' AND c.relname = '{nameEsc}' AND NOT t.tgisinternal ORDER BY t.tgname",
                        "indexes" => $"SELECT indexname, NULL::text FROM pg_indexes WHERE schemaname = '{sch}' AND tablename = '{nameEsc}' ORDER BY indexname",
                        _ => null,
                    };
                }
                else if (dbType == "oracle")
                {
                    var owner = SqlEscape.Literal(database);
                    var nameEsc = SqlEscape.Literal(name);
                    sql = kind switch
                    {
                        "keys" => $"SELECT constraint_name, CASE constraint_type WHEN 'P' THEN 'PK' WHEN 'R' THEN 'FK' ELSE 'UQ' END FROM all_constraints WHERE owner = '{owner}' AND table_name = '{nameEsc}' AND constraint_type IN ('P','U','R') ORDER BY 2, 1",
                        "constraints" => $"SELECT constraint_name, 'CHECK' FROM all_constraints WHERE owner = '{owner}' AND table_name = '{nameEsc}' AND constraint_type = 'C' ORDER BY 1",
                        "triggers" => $"SELECT trigger_name, trigger_type FROM all_triggers WHERE table_owner = '{owner}' AND table_name = '{nameEsc}' ORDER BY trigger_name",
                        "indexes" => $"SELECT index_name, CASE uniqueness WHEN 'UNIQUE' THEN 'unique' ELSE NULL END FROM all_indexes WHERE table_owner = '{owner}' AND table_name = '{nameEsc}' ORDER BY index_name",
                        _ => null,
                    };
                }
                else if (dbType == "sqlite")
                {
                    var nameEsc = SqlEscape.Literal(name);
                    sql = kind switch
                    {
                        "keys" => $"SELECT name, CASE origin WHEN 'pk' THEN 'PK' ELSE 'UQ' END FROM pragma_index_list('{nameEsc}') WHERE origin IN ('pk','u') ORDER BY name",
                        "triggers" => $"SELECT name, NULL FROM sqlite_master WHERE type = 'trigger' AND tbl_name = '{nameEsc}' ORDER BY name",
                        "indexes" => $"SELECT name, CASE \"unique\" WHEN 1 THEN 'unique' ELSE NULL END FROM pragma_index_list('{nameEsc}') WHERE origin = 'c' ORDER BY name",
                        _ => null,
                    };
                }
                else if (dbType != "clickhouse") // sqlserver（ClickHouse 无对应对象，返回空列表）
                {
                    if (string.IsNullOrWhiteSpace(schema))
                        throw new BadRequestException("参数不完整");
                    var db = SqlEscape.Identifier(database);
                    useParams = true;
                    sql = kind switch
                    {
                        "keys" => $@"
SELECT kc.name, CASE kc.type WHEN 'PK' THEN 'PK' ELSE 'UQ' END
FROM {db}.sys.key_constraints kc
JOIN {db}.sys.objects o ON kc.parent_object_id = o.object_id
JOIN {db}.sys.schemas s ON o.schema_id = s.schema_id
WHERE s.name = @schema AND o.name = @name
UNION ALL
SELECT fk.name, 'FK'
FROM {db}.sys.foreign_keys fk
JOIN {db}.sys.objects o ON fk.parent_object_id = o.object_id
JOIN {db}.sys.schemas s ON o.schema_id = s.schema_id
WHERE s.name = @schema AND o.name = @name
ORDER BY 2, 1",
                        "constraints" => $@"
SELECT dc.name, 'DEFAULT'
FROM {db}.sys.default_constraints dc
JOIN {db}.sys.objects o ON dc.parent_object_id = o.object_id
JOIN {db}.sys.schemas s ON o.schema_id = s.schema_id
WHERE s.name = @schema AND o.name = @name
UNION ALL
SELECT cc.name, 'CHECK'
FROM {db}.sys.check_constraints cc
JOIN {db}.sys.objects o ON cc.parent_object_id = o.object_id
JOIN {db}.sys.schemas s ON o.schema_id = s.schema_id
WHERE s.name = @schema AND o.name = @name
ORDER BY 2, 1",
                        "triggers" => $@"
SELECT tr.name, CASE WHEN tr.is_disabled = 1 THEN '已禁用' END
FROM {db}.sys.triggers tr
JOIN {db}.sys.objects o ON tr.parent_id = o.object_id
JOIN {db}.sys.schemas s ON o.schema_id = s.schema_id
WHERE s.name = @schema AND o.name = @name
ORDER BY tr.name",
                        "indexes" => $@"
SELECT i.name, LOWER(i.type_desc) + CASE WHEN i.is_unique = 1 THEN ', unique' ELSE '' END
FROM {db}.sys.indexes i
JOIN {db}.sys.objects o ON i.object_id = o.object_id
JOIN {db}.sys.schemas s ON o.schema_id = s.schema_id
WHERE s.name = @schema AND o.name = @name AND i.type > 0
ORDER BY i.name",
                        "stats" => $@"
SELECT st.name, CASE WHEN st.auto_created = 1 THEN '自动创建' END
FROM {db}.sys.stats st
JOIN {db}.sys.objects o ON st.object_id = o.object_id
JOIN {db}.sys.schemas s ON o.schema_id = s.schema_id
WHERE s.name = @schema AND o.name = @name
ORDER BY st.name",
                        _ => null,
                    };
                }

                var result = new TableChildrenDto();
                if (sql != null)
                {
                    await using var cmd = conn.CreateCommand();
                    cmd.CommandText = sql;
                    if (useParams)
                    {
                        DbConnectionHelper.AddParam(cmd, "@schema", schema);
                        DbConnectionHelper.AddParam(cmd, "@name", name);
                    }
                    await using var r = await cmd.ExecuteReaderAsync();
                    while (await r.ReadAsync())
                    {
                        result.Items.Add(new TableChildItemDto
                        {
                            Name = r.GetValue(0)?.ToString() ?? "",
                            Suffix = r.IsDBNull(1) ? null : r.GetValue(1)?.ToString()
                        });
                    }
                }
                return result;
            }
            catch (DbException ex)
            {
                throw new BadRequestException($"获取对象列表失败：{ex.Message}");
            }
        }
    }
}

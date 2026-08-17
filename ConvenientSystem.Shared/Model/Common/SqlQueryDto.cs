namespace ConvenientSystem.Shared.Model.Common
{
    /// <summary>SQL 执行/导出/执行计划的统一请求体</summary>
    public class SqlQueryRequest
    {
        public string Sql { get; set; } = "";
        /// <summary>数据源名称（对应 SysDataSource 表中的 Name）</summary>
        public string DataSource { get; set; } = "";
        /// <summary>执行语句时的目标数据库（空则用连接串默认库；PG/Oracle/SQLite 连接绑定单库忽略此参数）</summary>
        public string? Database { get; set; }
        /// <summary>页码（从 1 开始）</summary>
        public int Page { get; set; } = 1;
        /// <summary>每页行数</summary>
        public int PageSize { get; set; } = 100;
        /// <summary>是否需要统计实际总行数（首次执行为 true，翻页时为 false 以避免重复扫描）</summary>
        public bool NeedTotal { get; set; } = true;
    }

    /// <summary>数据源配置</summary>
    public class DataSourceDto
    {
        /// <summary>主键 Id（内置数据源为 0）</summary>
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string ConnectionString { get; set; } = "";
        /// <summary>数据库类型：sqlserver / mysql / postgresql / oracle / sqlite / clickhouse</summary>
        public string DbType { get; set; } = "sqlserver";
        /// <summary>是否内置数据源（ConvenientSystemDb：不允许修改删除，允许执行任意 SQL）</summary>
        public bool IsBuiltIn { get; set; }
    }

    /// <summary>单个结果集（列名、当前页数据、总行数；翻页请求的总行数为 -1）</summary>
    public class SqlResultSetDto
    {
        public List<string> Columns { get; set; } = new();
        public List<Dictionary<string, object?>> Rows { get; set; } = new();
        /// <summary>实际扫描到的总行数；NeedTotal 为 false 时返回 -1</summary>
        public long TotalRows { get; set; }
    }

    /// <summary>SQL 执行结果：多结果集 + 受影响行数（有结果集时为 -1）</summary>
    public class SqlExecuteResultDto
    {
        public List<SqlResultSetDto> ResultSets { get; set; } = new();
        /// <summary>无结果集（INSERT/UPDATE/DELETE/DDL）时的受影响行数，DDL 为 -1</summary>
        public int AffectedRows { get; set; }
    }

    /// <summary>导出结果：不分页的全量数据（上限 100000 行）</summary>
    public class SqlExportResultDto
    {
        public List<string> Columns { get; set; } = new();
        public List<Dictionary<string, object?>> Rows { get; set; } = new();
        public int TotalRows { get; set; }
    }

    /// <summary>执行计划：原始文本 + 结构化语句（仅 SQL Server 解析成功时非空）</summary>
    public class ExplainPlanDto
    {
        public string Plan { get; set; } = "";
        public List<PlanStatementDto> Statements { get; set; } = new();
    }

    /// <summary>执行计划中的单条语句</summary>
    public class PlanStatementDto
    {
        public int Index { get; set; }
        public string Sql { get; set; } = "";
        public double CostPercent { get; set; }
        public double SubtreeCost { get; set; }
        public string EstimatedRows { get; set; } = "";
        public List<PlanOperatorDto> Operators { get; set; } = new();
    }

    /// <summary>执行计划中的单个操作符（含悬浮提示所需的明细字段）</summary>
    public class PlanOperatorDto
    {
        public string PhysicalOp { get; set; } = "";
        public string LogicalOp { get; set; } = "";
        public double CostPercent { get; set; }
        public string EstimatedRows { get; set; } = "";
        public string EstimatedRowsRead { get; set; } = "";
        public string Executions { get; set; } = "";
        public string ObjectName { get; set; } = "";
        public string NodeId { get; set; } = "";
        public string EstIoCost { get; set; } = "";
        public string EstCpuCost { get; set; } = "";
        public string EstSubtreeCost { get; set; } = "";
        public string AvgRowSize { get; set; } = "";
        public string ObjectFullName { get; set; } = "";
        public List<string> OutputColumns { get; set; } = new();
    }

    /// <summary>数据库列表 + 连接串配置的默认库名</summary>
    public class DatabaseListDto
    {
        public List<string> Databases { get; set; } = new();
        public string? DefaultDatabase { get; set; }
    }

    /// <summary>数据库对象项（架构 + 名称 + 注释）</summary>
    public class SchemaObjectItemDto
    {
        public string Schema { get; set; } = "";
        public string Name { get; set; } = "";
        /// <summary>表/视图/存储过程/函数的中文注释</summary>
        public string? Description { get; set; }
    }

    /// <summary>指定数据库下的对象清单</summary>
    public class SchemaObjectsDto
    {
        public List<SchemaObjectItemDto> Tables { get; set; } = new();
        public List<SchemaObjectItemDto> Views { get; set; } = new();
        public List<SchemaObjectItemDto> Procedures { get; set; } = new();
        public List<SchemaObjectItemDto> Functions { get; set; } = new();
    }

    /// <summary>表/视图的单列信息（含注释）</summary>
    public class SchemaColumnDto
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public bool Nullable { get; set; }
        public bool IsPk { get; set; }
        /// <summary>列中文注释</summary>
        public string? Description { get; set; }
    }

    /// <summary>列信息响应（前端读取 columns 字段）</summary>
    public class SchemaColumnListDto
    {
        public List<SchemaColumnDto> Columns { get; set; } = new();
    }

    /// <summary>表下分组子对象项（名称 + 后缀说明）</summary>
    public class TableChildItemDto
    {
        public string Name { get; set; } = "";
        public string? Suffix { get; set; }
    }

    /// <summary>表下分组子对象响应（前端读取 items 字段）</summary>
    public class TableChildrenDto
    {
        public List<TableChildItemDto> Items { get; set; } = new();
    }

    /// <summary>单段脚本响应（前端读取 script 字段）</summary>
    public class SqlScriptDto
    {
        public string Script { get; set; } = "";
    }

    /// <summary>多段脚本响应（前端读取 statements 字段）</summary>
    public class SqlStatementsDto
    {
        public List<string> Statements { get; set; } = new();
    }

    /// <summary>SQL 快捷输入配置</summary>
    public class SnippetDto
    {
        public int Id { get; set; }
        public string Shortcut { get; set; } = "";
        public string Expansion { get; set; } = "";
        public string? Remark { get; set; }
        public int SortOrder { get; set; }
    }

    /// <summary>快捷输入重置请求（需登录密码确认）</summary>
    public class SnippetResetDto
    {
        public string Password { get; set; } = "";
    }

    /// <summary>SQL 查询收藏</summary>
    public class SqlFavoriteDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string SqlContent { get; set; } = "";
        public string? Remark { get; set; }
        public string? DataSource { get; set; }
        public int SortOrder { get; set; }
    }
}

using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Common.SqlQuery
{
    /// <summary>
    /// SQL 语句执行服务：分页查询、全量导出与执行计划。
    /// 内置本地数据源允许执行任意 SQL，其余数据源仅允许 SELECT（多层安全校验）。
    /// </summary>
    public interface ISqlExecuteService
    {
        /// <summary>执行 SQL 并按服务端分页返回多结果集</summary>
        Task<SqlExecuteResultDto> ExecuteAsync(SqlQueryRequest request, CancellationToken ct);

        /// <summary>导出查询结果：返回全部数据（不分页，上限 100000 行）</summary>
        Task<SqlExportResultDto> ExportAsync(SqlQueryRequest request, CancellationToken ct);

        /// <summary>获取 SQL 执行计划（SQL Server 为 SHOWPLAN_XML，其余库为 EXPLAIN）</summary>
        Task<ExplainPlanDto> ExplainPlanAsync(SqlQueryRequest request);
    }
}

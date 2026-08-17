using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Common.SqlQuery
{
    /// <summary>
    /// 数据库对象脚本生成服务：建表/建视图脚本、字段修改语句模板、表的全量对象语句，
    /// 按数据源类型（sqlserver / mysql / postgresql / oracle / sqlite / clickhouse）分别取原生 DDL 或由元数据拼接。
    /// </summary>
    public interface ISqlScriptService
    {
        /// <summary>生成对象创建脚本：表由元数据拼接 CREATE TABLE，视图/存储过程/函数取原始定义</summary>
        Task<SqlScriptDto> GetCreateScriptAsync(string dataSource, string database, string schema, string name, string type);

        /// <summary>生成表字段修改语句（新增/修改/删除字段模板，均含注释处理）</summary>
        Task<SqlStatementsDto> GetAlterScriptAsync(string dataSource, string database, string schema, string name);

        /// <summary>生成表的全部对象语句（建表/约束/索引/触发器/注释等，按数据库类型取全）</summary>
        Task<SqlStatementsDto> GetAllScriptAsync(string dataSource, string database, string schema, string name);
    }
}

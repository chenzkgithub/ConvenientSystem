using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Common.SqlQuery
{
    /// <summary>
    /// 数据库对象浏览服务：库列表、对象清单、列信息与表下分组子对象，
    /// 按数据源类型（sqlserver / mysql / postgresql / oracle / sqlite / clickhouse）分别取元数据。
    /// </summary>
    public interface ISchemaService
    {
        /// <summary>获取数据库列表（同时返回连接串配置的默认库名）</summary>
        Task<DatabaseListDto> GetDatabasesAsync(string dataSource);

        /// <summary>获取指定数据库下的对象（表/视图/存储过程/函数）</summary>
        Task<SchemaObjectsDto> GetObjectsAsync(string dataSource, string database);

        /// <summary>获取表/视图的列信息</summary>
        Task<SchemaColumnListDto> GetColumnsAsync(string dataSource, string database, string schema, string name);

        /// <summary>获取表下分组子对象（键/约束/触发器/索引/统计信息），供对象树表节点分组懒加载</summary>
        Task<TableChildrenDto> GetTableChildrenAsync(string dataSource, string database, string schema, string name, string kind);
    }
}

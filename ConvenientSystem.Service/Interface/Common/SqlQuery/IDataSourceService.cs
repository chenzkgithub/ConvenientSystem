using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Common.SqlQuery
{
    /// <summary>
    /// SQL 查询工具的数据源服务：SysDataSource 表读写、连接测试，
    /// 并为其余 SQL 工具服务提供「数据源名称 → IFreeSql」的统一解析入口。
    /// </summary>
    public interface IDataSourceService
    {
        /// <summary>读取全部数据源（内置 ConvenientSystemDb 固定置顶）</summary>
        List<DataSourceDto> GetList();

        /// <summary>添加数据源；名称重复或占用内置名称时抛 BadRequestException</summary>
        void Add(DataSourceDto dto);

        /// <summary>按主键 Id 修改数据源（名称也可修改），并失效连接缓存</summary>
        void Update(DataSourceDto dto);

        /// <summary>按名称删除数据源，并失效连接缓存</summary>
        void Remove(DataSourceDto dto);

        /// <summary>按表单中的类型与连接字符串直接试连（临时实例，不入缓存），返回成功提示</summary>
        Task<string> TestAsync(DataSourceDto dto);

        /// <summary>
        /// 按数据源名称解析 IFreeSql（工厂缓存实例），同时输出归一化后的 dbType；
        /// 数据源不存在或配置不完整时抛 BadRequestException。
        /// </summary>
        IFreeSql Resolve(string dsName, out string dbType);

        /// <summary>
        /// 数据源是否允许执行全部 SQL：须同时满足名称为内置数据源、
        /// 且其连接字符串确实指向本地服务（防止配置被改到远程库后仍放开全部语句）。
        /// </summary>
        bool IsFullAccessSource(string? dsName);

        /// <summary>从数据源连接串中提取默认数据库名（支持各类型连接串格式）</summary>
        string? GetDefaultDatabase(string dsName);
    }
}

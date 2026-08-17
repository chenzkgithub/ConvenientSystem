using ConvenientSystem.Api.Auth;
using ConvenientSystem.Shared.Model.Common;
using ConvenientSystem.Service.Common.SqlQuery;
using Microsoft.AspNetCore.Mvc;

namespace ConvenientSystem.Api.Controllers.Common
{
    /// <summary>
    /// SQL 查询工具接口：内置 ConvenientSystemDb 数据源允许执行任意 SQL，其余数据源仅允许 SELECT 语句（多层安全校验）。
    /// 数据源配置存储在本地数据库 ConvenientSystem 的 SysDataSource 表中（见 db/init.sql）；
    /// ConvenientSystemDb 为程序内置数据源（指向本机配置库所在实例的 master 库），不落库、不允许修改删除。
    /// </summary>
    [Area("Common")]
    [PermissionAuthorize("sql-query")]
    public class SqlQueryController : BaseController
    {
        private readonly IDataSourceService _dataSourceService;
        private readonly ISqlExecuteService _sqlExecuteService;
        private readonly ISchemaService _schemaService;
        private readonly ISqlScriptService _sqlScriptService;
        private readonly ISqlSnippetService _sqlSnippetService;
        private readonly ISqlFavoriteService _sqlFavoriteService;

        public SqlQueryController(
            IDataSourceService dataSourceService,
            ISqlExecuteService sqlExecuteService,
            ISchemaService schemaService,
            ISqlScriptService sqlScriptService,
            ISqlSnippetService sqlSnippetService,
            ISqlFavoriteService sqlFavoriteService)
        {
            _dataSourceService = dataSourceService;
            _sqlExecuteService = sqlExecuteService;
            _schemaService = schemaService;
            _sqlScriptService = sqlScriptService;
            _sqlSnippetService = sqlSnippetService;
            _sqlFavoriteService = sqlFavoriteService;
        }

        // ============ 数据源管理接口 ============

        /// <summary>获取数据源列表（内置数据源固定置顶）</summary>
        [HttpGet]
        public IActionResult GetDataSources() => Ok(_dataSourceService.GetList());

        /// <summary>添加数据源</summary>
        [HttpPost]
        public IActionResult AddDataSource([FromBody] DataSourceDto dto)
        {
            _dataSourceService.Add(dto);
            return Ok(new { message = "已添加" });
        }

        /// <summary>修改数据源</summary>
        [HttpPost]
        public IActionResult UpdateDataSource([FromBody] DataSourceDto dto)
        {
            _dataSourceService.Update(dto);
            return Ok(new { message = "已保存" });
        }

        /// <summary>删除数据源</summary>
        [HttpPost]
        public IActionResult RemoveDataSource([FromBody] DataSourceDto dto)
        {
            _dataSourceService.Remove(dto);
            return Ok(new { message = "已删除" });
        }

        /// <summary>测试数据源连接</summary>
        [HttpPost]
        public async Task<IActionResult> TestDataSource([FromBody] DataSourceDto dto)
            => Ok(new { message = await _dataSourceService.TestAsync(dto) });

        // ============ SQL 执行接口 ============

        /// <summary>执行 SQL 语句（支持多结果集与服务端分页）</summary>
        [HttpPost]
        public async Task<IActionResult> Execute([FromBody] SqlQueryRequest request, CancellationToken cancellationToken)
            => Ok(await _sqlExecuteService.ExecuteAsync(request, cancellationToken));

        /// <summary>导出查询结果全量数据（上限 10 万行）</summary>
        [HttpPost]
        public async Task<IActionResult> ExportData([FromBody] SqlQueryRequest request, CancellationToken cancellationToken)
            => Ok(await _sqlExecuteService.ExportAsync(request, cancellationToken));

        /// <summary>获取 SQL 执行计划</summary>
        [HttpPost]
        public async Task<IActionResult> ExplainPlan([FromBody] SqlQueryRequest request)
            => Ok(await _sqlExecuteService.ExplainPlanAsync(request));

        // ============ 数据库对象浏览接口 ============

        /// <summary>获取数据库列表</summary>
        [HttpGet]
        public async Task<IActionResult> GetDatabases([FromQuery] string dataSource)
            => Ok(await _schemaService.GetDatabasesAsync(dataSource));

        /// <summary>获取指定数据库下的对象（表/视图/存储过程/函数）</summary>
        [HttpGet]
        public async Task<IActionResult> GetSchemaObjects([FromQuery] string dataSource, [FromQuery] string database)
            => Ok(await _schemaService.GetObjectsAsync(dataSource, database));

        /// <summary>获取表/视图的列信息</summary>
        [HttpGet]
        public async Task<IActionResult> GetSchemaColumns([FromQuery] string dataSource, [FromQuery] string database, [FromQuery] string schema, [FromQuery] string name)
            => Ok(await _schemaService.GetColumnsAsync(dataSource, database, schema, name));

        /// <summary>获取表下分组子对象（键/约束/触发器/索引/统计信息）</summary>
        [HttpGet]
        public async Task<IActionResult> GetTableChildren([FromQuery] string dataSource, [FromQuery] string database, [FromQuery] string schema, [FromQuery] string name, [FromQuery] string kind)
            => Ok(await _schemaService.GetTableChildrenAsync(dataSource, database, schema, name, kind));

        // ============ 脚本生成接口 ============

        /// <summary>生成对象创建脚本</summary>
        [HttpGet]
        public async Task<IActionResult> GetCreateScript([FromQuery] string dataSource, [FromQuery] string database, [FromQuery] string schema, [FromQuery] string name, [FromQuery] string type)
            => Ok(await _sqlScriptService.GetCreateScriptAsync(dataSource, database, schema, name, type));

        /// <summary>生成表字段修改语句</summary>
        [HttpGet]
        public async Task<IActionResult> GetAlterScript([FromQuery] string dataSource, [FromQuery] string database, [FromQuery] string schema, [FromQuery] string name)
            => Ok(await _sqlScriptService.GetAlterScriptAsync(dataSource, database, schema, name));

        /// <summary>生成表的全部对象语句</summary>
        [HttpGet]
        public async Task<IActionResult> GetAllScript([FromQuery] string dataSource, [FromQuery] string database, [FromQuery] string schema, [FromQuery] string name)
            => Ok(await _sqlScriptService.GetAllScriptAsync(dataSource, database, schema, name));

        // ============ SQL 快捷输入管理接口 ============

        /// <summary>获取全部快捷输入配置</summary>
        [HttpGet]
        public IActionResult GetSnippets() => Ok(_sqlSnippetService.GetList());

        /// <summary>添加快捷输入</summary>
        [HttpPost]
        public IActionResult AddSnippet([FromBody] SnippetDto dto)
        {
            _sqlSnippetService.Add(dto);
            return Ok(new { message = "已添加" });
        }

        /// <summary>修改快捷输入</summary>
        [HttpPost]
        public IActionResult UpdateSnippet([FromBody] SnippetDto dto)
        {
            _sqlSnippetService.Update(dto);
            return Ok(new { message = "已保存" });
        }

        /// <summary>删除快捷输入</summary>
        [HttpPost]
        public IActionResult RemoveSnippet([FromBody] SnippetDto dto)
        {
            _sqlSnippetService.Remove(dto);
            return Ok(new { message = "已删除" });
        }

        /// <summary>重置快捷输入为初始数据（需输入登录密码确认）</summary>
        [HttpPost]
        public async Task<IActionResult> ResetSnippets([FromBody] SnippetResetDto dto)
        {
            await _sqlSnippetService.ResetAsync(dto);
            return Ok(new { message = "已重置为初始数据" });
        }

        // ============ SQL 查询收藏接口 ============

        /// <summary>获取 SQL 收藏列表</summary>
        [HttpGet]
        public IActionResult GetFavorites() => Ok(_sqlFavoriteService.GetList());

        /// <summary>添加 SQL 收藏</summary>
        [HttpPost]
        public IActionResult AddFavorite([FromBody] SqlFavoriteDto dto)
        {
            _sqlFavoriteService.Add(dto);
            return Ok(new { message = "已收藏" });
        }

        /// <summary>修改 SQL 收藏</summary>
        [HttpPost]
        public IActionResult UpdateFavorite([FromBody] SqlFavoriteDto dto)
        {
            _sqlFavoriteService.Update(dto);
            return Ok(new { message = "已保存" });
        }

        /// <summary>删除 SQL 收藏</summary>
        [HttpPost]
        public IActionResult RemoveFavorite([FromBody] SqlFavoriteDto dto)
        {
            _sqlFavoriteService.Remove(dto);
            return Ok(new { message = "已删除" });
        }
    }
}

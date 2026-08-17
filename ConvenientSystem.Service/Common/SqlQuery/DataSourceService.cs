using ConvenientSystem.Shared.Common;
using ConvenientSystem.Shared.Common.Exceptions;
using ConvenientSystem.Shared.Common.Security;
using ConvenientSystem.Shared.Entity.Common;
using ConvenientSystem.Shared.Model.Common;
using Microsoft.Data.SqlClient;

namespace ConvenientSystem.Service.Common.SqlQuery
{
    /// <summary>
    /// 数据源服务实现：配置存储在本地库 ConvenientSystem 的 SysDataSource 表；
    /// ConvenientSystemDb 为程序内置数据源（指向本机配置库所在实例的 master 库），不落库、不允许修改删除。
    /// </summary>
    public class DataSourceService : IDataSourceService
    {
        /// <summary>内置本地数据源名称</summary>
        public const string LocalDbSourceName = "ConvenientSystemDb";

        /// <summary>本机主机名别名（含本机机器名，用于判断连接字符串是否指向本地服务）</summary>
        private static readonly HashSet<string> LocalHostAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            "localhost", "127.0.0.1", "::1", ".", "(local)", Environment.MachineName
        };

        // 读取 ConvenientSystemDb 连接串，仅用于构造内置数据源
        private readonly IConfiguration _config;
        // 本地配置库 FreeSql（SysDataSource 所在库）
        private readonly IFreeSql _configDb;
        // 动态数据源 IFreeSql 工厂（按数据源名称缓存实例）
        private readonly DynamicFreeSqlFactory _dsFactory;
        private readonly ICurrentUser _currentUser;

        public DataSourceService(
            IConfiguration config,
            [FromKeyedServices("ConvenientSystemDb")] IFreeSql configDb,
            DynamicFreeSqlFactory dsFactory,
            ICurrentUser currentUser)
        {
            _config = config;
            _configDb = configDb;
            _dsFactory = dsFactory;
            _currentUser = currentUser;
        }

        private bool IsDataScopeAll => _currentUser.DataScope == DataScope.All;
        private bool IsOwner(Guid? createdById)
            => _currentUser.UserId.HasValue && createdById == _currentUser.UserId;
        private void EnsureOwner(SysDataSourceEntity entity)
        {
            if (!IsDataScopeAll && !IsOwner(entity.CreatedById))
                throw new ForbiddenException("无权操作该数据源");
        }

        public List<DataSourceDto> GetList()
        {
            try
            {
                return ReadDataSources();
            }
            catch (Exception ex)
            {
                throw new BizException($"读取数据源失败：{ex.Message}", StatusCodes.Status500InternalServerError);
            }
        }

        public void Add(DataSourceDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Name) || string.IsNullOrWhiteSpace(dto.ConnectionString))
                throw new BadRequestException("名称和连接字符串不能为空");
            if (IsLocalDbSource(dto.Name))
                throw new BadRequestException($"'{LocalDbSourceName}' 为内置数据源名称，不允许使用");

            var name = dto.Name.Trim();
            try
            {
                // 先查重名给出友好提示（Name + CreatedById 唯一索引兜底）
                var dupQuery = _configDb.Select<SysDataSourceEntity>().Where(d => d.Name == name);
                if (!IsDataScopeAll && _currentUser.UserId.HasValue)
                    dupQuery = dupQuery.Where(d => d.CreatedById == _currentUser.UserId);
                if (dupQuery.Any())
                    throw new BadRequestException($"数据源 '{name}' 已存在");

                _configDb.Insert(new SysDataSourceEntity
                {
                    Name = name,
                    ConnectionString = dto.ConnectionString.Trim(),
                    DbType = string.IsNullOrWhiteSpace(dto.DbType) ? "sqlserver" : dto.DbType.Trim().ToLowerInvariant(),
                    CreatedById = _currentUser.UserId
                }).ExecuteAffrows();
            }
            catch (BizException)
            {
                throw;
            }
            catch (SqlException ex) when (ex.Number is 2627 or 2601) // 违反 Name 唯一约束
            {
                throw new BadRequestException($"数据源 '{dto.Name}' 已存在");
            }
            catch (Exception ex)
            {
                throw new BizException($"添加数据源失败：{ex.Message}", StatusCodes.Status500InternalServerError);
            }
        }

        public void Update(DataSourceDto dto)
        {
            if (dto.Id <= 0)
                throw new BadRequestException("缺少数据源主键 Id");
            if (string.IsNullOrWhiteSpace(dto.Name) || string.IsNullOrWhiteSpace(dto.ConnectionString))
                throw new BadRequestException("名称和连接字符串不能为空");
            if (IsLocalDbSource(dto.Name))
                throw new BadRequestException($"'{LocalDbSourceName}' 为内置数据源名称，不允许使用");

            var name = dto.Name.Trim();
            try
            {
                // 取旧行：确认存在，并在改名时用旧名失效工厂缓存
                var oldRow = _configDb.Select<SysDataSourceEntity>().Where(d => d.Id == dto.Id).First()
                    ?? throw new BadRequestException($"数据源不存在（Id={dto.Id}）");
                EnsureOwner(oldRow);
                // 先查重名（排除自身）给出友好提示（Name + CreatedById 唯一索引兜底）
                var dupQuery = _configDb.Select<SysDataSourceEntity>().Where(d => d.Name == name && d.Id != dto.Id);
                if (!IsDataScopeAll && _currentUser.UserId.HasValue)
                    dupQuery = dupQuery.Where(d => d.CreatedById == _currentUser.UserId);
                if (dupQuery.Any())
                    throw new BadRequestException($"数据源 '{name}' 已存在");

                _configDb.Update<SysDataSourceEntity>()
                    .Set(d => d.Name, name)
                    .Set(d => d.ConnectionString, dto.ConnectionString.Trim())
                    .Set(d => d.DbType, string.IsNullOrWhiteSpace(dto.DbType) ? "sqlserver" : dto.DbType.Trim().ToLowerInvariant())
                    .Where(d => d.Id == dto.Id)
                    .ExecuteAffrows();
                // 失效工厂缓存（改名时旧名与新名都清理；连接串变化时 Get 也会自动重建）
                _dsFactory.Remove(oldRow.Name);
                _dsFactory.Remove(name);
            }
            catch (BizException)
            {
                throw;
            }
            catch (SqlException ex) when (ex.Number is 2627 or 2601) // 违反 Name 唯一约束
            {
                throw new BadRequestException($"数据源 '{dto.Name}' 已存在");
            }
            catch (Exception ex)
            {
                throw new BizException($"修改数据源失败：{ex.Message}", StatusCodes.Status500InternalServerError);
            }
        }

        public void Remove(DataSourceDto dto)
        {
            if (IsLocalDbSource(dto.Name))
                throw new BadRequestException($"'{LocalDbSourceName}' 为内置数据源，不允许删除");
            try
            {
                var query = _configDb.Select<SysDataSourceEntity>().Where(d => d.Name == dto.Name);
                if (!IsDataScopeAll && _currentUser.UserId.HasValue)
                    query = query.Where(d => d.CreatedById == _currentUser.UserId);
                var entity = query.First()
                    ?? throw new BadRequestException($"数据源 '{dto.Name}' 不存在");
                EnsureOwner(entity);
                var removed = _configDb.Delete<SysDataSourceEntity>()
                    .Where(d => d.Name == dto.Name)
                    .ExecuteAffrows();
                if (removed == 0)
                    throw new BadRequestException($"数据源 '{dto.Name}' 不存在");
                _dsFactory.Remove(dto.Name); // 失效工厂缓存
            }
            catch (BizException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new BizException($"删除数据源失败：{ex.Message}", StatusCodes.Status500InternalServerError);
            }
        }

        public async Task<string> TestAsync(DataSourceDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.ConnectionString))
                throw new BadRequestException("连接字符串不能为空");

            // 数据范围为本人时，只能测试自己创建的数据源（新建时无名称则跳过校验）
            if (!IsDataScopeAll && !string.IsNullOrWhiteSpace(dto.Name))
            {
                var existing = _configDb.Select<SysDataSourceEntity>()
                    .Where(d => d.Name == dto.Name.Trim())
                    .First();
                if (existing != null)
                    EnsureOwner(existing);
            }

            var dbType = string.IsNullOrWhiteSpace(dto.DbType) ? "sqlserver" : dto.DbType.Trim().ToLowerInvariant();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            IFreeSql? fsql = null;
            try
            {
                fsql = _dsFactory.CreateTransient(dto.ConnectionString.Trim(), ref dbType);
                // 限制测试时长，避免错误地址长时间阻塞：后台取池化连接（首次获取即真实建连）
                var testFsql = fsql;
                var openTask = Task.Run(() =>
                {
                    using var pooled = testFsql.Ado.MasterPool.Get();
                    _ = pooled.Value;
                });
                var finished = await Task.WhenAny(openTask, Task.Delay(TimeSpan.FromSeconds(10)));
                if (finished != openTask)
                {
                    // 观察后台任务异常，避免 UnobservedTaskException
                    _ = openTask.ContinueWith(t => _ = t.Exception, TaskScheduler.Default);
                    throw new BadRequestException("连接超时（10 秒），请检查地址与网络");
                }
                await openTask; // 失败时在此抛出原始异常
                sw.Stop();
                return $"连接成功（耗时 {sw.ElapsedMilliseconds} ms）";
            }
            catch (BizException)
            {
                throw;
            }
            catch (Exception ex)
            {
                var (hint, downloadUrl) = DetectMissingDriver(dbType, ex);
                // 缺驱动/环境类失败额外带 hint、downloadUrl，前端据此弹窗引导下载
                if (hint != null)
                {
                    throw new BizException($"连接失败：{ex.Message}")
                    {
                        Extras = new Dictionary<string, object?> { ["hint"] = hint, ["downloadUrl"] = downloadUrl }
                    };
                }
                throw new BadRequestException($"连接失败：{ex.Message}");
            }
            finally
            {
                fsql?.Dispose();
            }
        }

        public IFreeSql Resolve(string dsName, out string dbType)
        {
            dbType = "sqlserver";
            if (string.IsNullOrWhiteSpace(dsName))
                throw new BadRequestException("数据源名称不能为空");

            // 内置 ConvenientSystemDb：仅数据范围为全部时可用；直连本机配置库实例，允许写入；
            // 若配置库连接串被指向非本机服务，则降级为只读（与 SQL 校验的双重判断保持一致）
            if (IsLocalDbSource(dsName))
            {
                if (!IsDataScopeAll)
                    throw new ForbiddenException($"无权访问内置数据源 '{LocalDbSourceName}'");
                var builtinConnStr = BuildLocalDbSource().ConnectionString;
                var readOnly = !IsLocalServerConnStr(builtinConnStr);
                return _dsFactory.Get(LocalDbSourceName, builtinConnStr, "sqlserver", readOnly);
            }

            DataSourceDto? ds;
            try
            {
                ds = FindDataSource(dsName);
            }
            catch (Exception ex)
            {
                throw new BadRequestException($"读取数据源配置失败：{ex.Message}");
            }
            if (ds == null)
                throw new BadRequestException($"数据源 '{dsName}' 不存在");

            // 数据范围为本人时，只能解析自己创建的数据源
            if (!IsDataScopeAll)
            {
                var entity = _configDb.Select<SysDataSourceEntity>()
                    .Where(d => d.Name == dsName && d.CreatedById == _currentUser.UserId)
                    .First();
                if (entity == null)
                    throw new ForbiddenException("无权访问该数据源");
            }
            if (string.IsNullOrWhiteSpace(ds.ConnectionString))
                throw new BadRequestException("连接字符串不能为空");

            dbType = string.IsNullOrWhiteSpace(ds.DbType) ? "sqlserver" : ds.DbType.Trim().ToLowerInvariant();
            DynamicFreeSqlFactory.MapDataType(ref dbType); // 未知类型归一为 sqlserver
            // 非内置数据源一律只读（sqlserver 追加 ApplicationIntent=ReadOnly）
            return _dsFactory.Get(ds.Name, ds.ConnectionString.Trim(), dbType, readOnly: true);
        }

        public bool IsFullAccessSource(string? dsName) =>
            IsLocalDbSource(dsName) && IsLocalServerConnStr(BuildLocalDbSource().ConnectionString);

        public string? GetDefaultDatabase(string dsName)
        {
            string? connStr;
            if (IsLocalDbSource(dsName))
            {
                // 内置数据源：从原始配置连接串取默认库（而非 BuildLocalDbSource 修改后的 master）
                connStr = _config.GetConnectionString(LocalDbSourceName);
            }
            else
            {
                connStr = FindDataSource(dsName)?.ConnectionString;
            }
            if (string.IsNullOrWhiteSpace(connStr)) return null;

            // 按分号拆分键值对，查找常见的数据库名键
            // SQL Server: Initial Catalog / Database；MySQL / PostgreSQL / ClickHouse: Database
            foreach (var part in connStr.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var idx = part.IndexOf('=');
                if (idx <= 0) continue;
                var key = part[..idx].Trim().ToLowerInvariant();
                var value = part[(idx + 1)..].Trim();
                if (string.IsNullOrEmpty(value)) continue;
                if (key is "initial catalog" or "database" or "db")
                    return value;
            }
            return null;
        }

        // ============ 内置本地数据源与表读写 ============

        /// <summary>是否为内置本地数据源</summary>
        private static bool IsLocalDbSource(string? name) =>
            string.Equals(name?.Trim(), LocalDbSourceName, StringComparison.OrdinalIgnoreCase);

        /// <summary>从连接字符串中提取服务器地址（兼容 Server/Data Source/Address/Addr/Host 等常见键名）</summary>
        private static string? ExtractServerHost(string? connStr)
        {
            if (string.IsNullOrWhiteSpace(connStr)) return null;
            foreach (var part in connStr.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var idx = part.IndexOf('=');
                if (idx <= 0) continue;
                var key = part[..idx].Trim();
                if (key.Equals("server", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("data source", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("address", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("addr", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("host", StringComparison.OrdinalIgnoreCase))
                    return part[(idx + 1)..].Trim();
            }
            return null;
        }

        /// <summary>
        /// 连接字符串是否指向本地服务：LocalDB 实例、本地命名管道，
        /// 或主机为 localhost/127.0.0.1/::1/./(local)/本机机器名（自动剔除协议前缀、实例名与端口）。
        /// </summary>
        private static bool IsLocalServerConnStr(string? connStr)
        {
            var host = ExtractServerHost(connStr);
            if (string.IsNullOrWhiteSpace(host)) return false;

            // (localdb)\实例名与本地命名管道必然是本机
            if (host.StartsWith("(localdb)", StringComparison.OrdinalIgnoreCase)) return true;
            if (host.StartsWith(@"np:\\.\", StringComparison.OrdinalIgnoreCase) ||
                host.StartsWith(@"\\.\", StringComparison.OrdinalIgnoreCase)) return true;

            // 去掉协议前缀（tcp:/np:/lpc:），再去掉实例名（\实例）与端口（,端口 或 :端口）
            var colonIdx = host.IndexOf(':');
            if (colonIdx is 2 or 3) host = host[(colonIdx + 1)..];
            host = host.Split('\\')[0].Split(',')[0].Trim();
            var portIdx = host.LastIndexOf(':');
            if (portIdx > 0 && host.IndexOf(':') == portIdx) host = host[..portIdx]; // host:端口（排除 IPv6 ::1）

            return LocalHostAliases.Contains(host);
        }

        /// <summary>
        /// 构造内置 ConvenientSystemDb 数据源：连接串取自配置库 ConvenientSystemDb（保证与程序自身连接一致，必然可连通），
        /// 仅把数据库切到 master 以便浏览本机全部数据库。
        /// </summary>
        private DataSourceDto BuildLocalDbSource()
        {
            var builder = new SqlConnectionStringBuilder(_config.GetConnectionString(LocalDbSourceName))
            {
                InitialCatalog = "master"
            };
            return new DataSourceDto
            {
                Id = 0,
                Name = LocalDbSourceName,
                ConnectionString = builder.ConnectionString,
                DbType = "sqlserver",
                IsBuiltIn = true
            };
        }

        /// <summary>读取全部数据源（内置 ConvenientSystemDb 仅数据范围为全部时可见，表中同名历史行忽略）</summary>
        private List<DataSourceDto> ReadDataSources()
        {
            var list = new List<DataSourceDto>();
            if (IsDataScopeAll)
                list.Add(BuildLocalDbSource());

            var query = _configDb.Select<SysDataSourceEntity>()
                .Where(d => d.Name != LocalDbSourceName);
            if (!IsDataScopeAll && _currentUser.UserId.HasValue)
                query = query.Where(d => d.CreatedById == _currentUser.UserId);

            var rows = query.OrderBy(d => d.Id).ToList();
            list.AddRange(rows.Select(d => new DataSourceDto
            {
                Id = d.Id,
                Name = d.Name,
                ConnectionString = d.ConnectionString,
                DbType = string.IsNullOrWhiteSpace(d.DbType) ? "sqlserver" : d.DbType
            }));
            return list;
        }

        /// <summary>按名称查找单个数据源，不存在返回 null</summary>
        private DataSourceDto? FindDataSource(string name)
        {
            if (IsLocalDbSource(name))
                return BuildLocalDbSource();
            var query = _configDb.Select<SysDataSourceEntity>()
                .Where(d => d.Name == name);
            if (!IsDataScopeAll && _currentUser.UserId.HasValue)
                query = query.Where(d => d.CreatedById == _currentUser.UserId);
            var row = query.First();
            if (row == null)
                return null;
            return new DataSourceDto
            {
                Id = row.Id,
                Name = row.Name,
                ConnectionString = row.ConnectionString,
                DbType = string.IsNullOrWhiteSpace(row.DbType) ? "sqlserver" : row.DbType
            };
        }

        /// <summary>识别“缺驱动/环境”类连接异常，返回提示与下载地址（无法识别时返回 null）</summary>
        private static (string? hint, string? downloadUrl) DetectMissingDriver(string dbType, Exception ex)
        {
            // SQLite 原生库缺失（极端情况：发布产物被手工删减）
            if (dbType == "sqlite" && ex is DllNotFoundException)
                return ("SQLite 原生组件缺失，发布产物可能不完整，请重新发布程序。", null);
            return (null, null);
        }
    }
}

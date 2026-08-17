using FreeSql;
using System.Collections.Concurrent;

namespace ConvenientSystem.Shared.Common
{
    /// <summary>
    /// SQL 查询工具动态数据源的 IFreeSql 工厂（单例注册）：
    /// 所有数据库访问统一经由 FreeSql，不再直接 new 原生 ADO 连接。
    /// 按数据源名称缓存实例，连接串/类型变化时自动释放旧实例重建。
    /// </summary>
    public class DynamicFreeSqlFactory : IDisposable
    {
        /// <summary>缓存项：指纹（类型+只读+连接串）用于检测数据源配置变化</summary>
        private sealed class Entry
        {
            public required string Fingerprint { get; init; }
            public required IFreeSql Fsql { get; init; }
        }

        private readonly ConcurrentDictionary<string, Entry> _cache = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _buildLock = new();

        /// <summary>dbType 文本映射为 FreeSql DataType（未知类型归为 sqlserver）</summary>
        public static DataType MapDataType(ref string dbType)
        {
            switch (dbType)
            {
                case "clickhouse": return DataType.ClickHouse;
                case "mysql": return DataType.MySql;
                case "postgresql": return DataType.PostgreSQL;
                case "oracle": return DataType.Oracle;
                case "sqlite": return DataType.Sqlite;
                default:
                    dbType = "sqlserver";
                    return DataType.SqlServer;
            }
        }

        /// <summary>
        /// sqlserver 连接串预处理：readOnly 时追加 ApplicationIntent=ReadOnly；
        /// 未显式配置时自动信任服务器证书（新版 SqlClient 默认 Encrypt=True，自签名证书会报“证书链不受信任”）。
        /// </summary>
        public static string NormalizeSqlServerConnStr(string connStr, bool readOnly)
        {
            if (readOnly && !connStr.Contains("ApplicationIntent", StringComparison.OrdinalIgnoreCase))
                connStr += ";ApplicationIntent=ReadOnly";
            if (!connStr.Contains("TrustServerCertificate", StringComparison.OrdinalIgnoreCase))
                connStr += ";TrustServerCertificate=True";
            return connStr;
        }

        /// <summary>
        /// 获取（或构建并缓存）指定数据源的 IFreeSql。
        /// readOnly 仅对 sqlserver 生效（追加 ApplicationIntent=ReadOnly）。
        /// </summary>
        public IFreeSql Get(string name, string connStr, string dbType, bool readOnly)
        {
            var dataType = MapDataType(ref dbType);
            if (dataType == DataType.SqlServer)
                connStr = NormalizeSqlServerConnStr(connStr, readOnly);
            var fingerprint = $"{dbType}|{readOnly}|{connStr}";

            if (_cache.TryGetValue(name, out var entry) && entry.Fingerprint == fingerprint)
                return entry.Fsql;

            // 数据源很少且构建开销大，直接串行化构建，避免竞态下实例泄漏
            lock (_buildLock)
            {
                if (_cache.TryGetValue(name, out entry))
                {
                    if (entry.Fingerprint == fingerprint)
                        return entry.Fsql;
                    // 配置已变化：释放旧实例
                    if (_cache.TryRemove(name, out var old))
                        old.Fsql.Dispose();
                }
                var created = new Entry { Fingerprint = fingerprint, Fsql = Build(dataType, connStr) };
                _cache[name] = created;
                return created.Fsql;
            }
        }

        /// <summary>构建不入缓存的临时实例（测试连接用），调用方负责 Dispose</summary>
        public IFreeSql CreateTransient(string connStr, ref string dbType)
        {
            var dataType = MapDataType(ref dbType);
            if (dataType == DataType.SqlServer)
                connStr = NormalizeSqlServerConnStr(connStr, readOnly: false);
            return Build(dataType, connStr);
        }

        /// <summary>数据源修改/删除后失效缓存并释放实例</summary>
        public void Remove(string name)
        {
            if (_cache.TryRemove(name, out var entry))
                entry.Fsql.Dispose();
        }

        private static IFreeSql Build(DataType dataType, string connStr) =>
            new FreeSqlBuilder()
                .UseConnectionString(dataType, connStr)
                .UseAutoSyncStructure(false)
                .Build();

        public void Dispose()
        {
            foreach (var key in _cache.Keys.ToArray())
                Remove(key);
            GC.SuppressFinalize(this);
        }
    }
}

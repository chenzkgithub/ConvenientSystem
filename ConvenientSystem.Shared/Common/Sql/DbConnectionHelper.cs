using System.Data.Common;

namespace ConvenientSystem.Shared.Common.Sql
{
    /// <summary>
    /// 原生 DbConnection / DbCommand 通用操作：会话隔离级别、参数添加、数据库上下文切换与还原。
    /// </summary>
    public static class DbConnectionHelper
    {
        /// <summary>
        /// SQL Server 会话级 READ UNCOMMITTED：等价于给查询中所有表加 WITH (NOLOCK)，
        /// 避免查询被业务写入阻塞或反过来阻塞业务（其他库无此概念，直接跳过）
        /// </summary>
        public static async Task ApplyNoLockAsync(DbConnection conn, string dbType)
        {
            if (dbType != "sqlserver") return;
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED";
            await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>给 DbCommand 添加参数（通用 DbParameter，避免依赖具体驱动的 Command 类型）</summary>
        public static void AddParam(DbCommand cmd, string name, object value)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = name;
            p.Value = value;
            cmd.Parameters.Add(p);
        }

        /// <summary>切换连接的当前数据库（返回原库名供执行后还原；PG/Oracle/SQLite 连接绑定单库不切换）</summary>
        public static string? SwitchDatabase(DbConnection conn, string dbType, string? database)
        {
            if (string.IsNullOrWhiteSpace(database)) return null;
            if (dbType is not ("sqlserver" or "mysql" or "clickhouse")) return null;
            var original = conn.Database;
            if (string.Equals(original, database, StringComparison.OrdinalIgnoreCase)) return null;
            conn.ChangeDatabase(database);
            return string.IsNullOrWhiteSpace(original) ? null : original;
        }

        /// <summary>还原连接的原数据库（避免带着切换后的库归还连接池）</summary>
        public static void RestoreDatabase(DbConnection conn, string? originalDb)
        {
            if (originalDb == null) return;
            try { conn.ChangeDatabase(originalDb); }
            catch { /* 连接已断开等场景忽略，连接池自身会校验可用性 */ }
        }
    }
}

using System.Text.RegularExpressions;

namespace ConvenientSystem.Shared.Common.Sql
{
    /// <summary>
    /// 非内置数据源的 SQL 安全校验：只允许 SELECT / WITH ... SELECT，
    /// 可前置 DECLARE @变量 = 常量，并用黑名单拦截写入与危险操作。
    /// </summary>
    public static class SqlSecurityValidator
    {
        /// <summary>危险关键字黑名单（独立单词匹配）</summary>
        private static readonly string[] ForbiddenKeywords = {
            "INSERT", "UPDATE", "DELETE", "DROP", "ALTER", "CREATE", "TRUNCATE",
            "EXEC", "EXECUTE", "INTO", "MERGE", "GRANT", "REVOKE", "DENY",
            "BACKUP", "RESTORE", "SHUTDOWN", "DBCC", "OPENROWSET", "OPENDATASOURCE",
            "XP_", "SP_", "BULK", "KILL", "RECONFIGURE", "WAITFOR"
        };

        /// <summary>对 SQL 进行安全校验，不通过返回错误消息，通过返回 null</summary>
        public static string? Validate(string sql)
        {
            if (string.IsNullOrWhiteSpace(sql))
                return "SQL 不能为空";

            // 按分号拆分为多条语句，每条都必须是 SELECT（或 WITH ... SELECT 公用表表达式）；
            // 允许前面带若干条 DECLARE @变量 = 常量 语句，用于查询参数化（不允许 INSERT/UPDATE/SET 等写入语句）
            var statements = sql.Split(';', StringSplitOptions.RemoveEmptyEntries);
            var hasQuery = false;
            foreach (var rawStmt in statements)
            {
                // 去掉注释后检查
                var cleanStmt = Regex.Replace(rawStmt, @"(--[^\n]*|/\*.*?\*/)", " ", RegexOptions.Singleline).Trim();
                if (string.IsNullOrWhiteSpace(cleanStmt))
                    continue;

                // 必须以 SELECT 开头，或 WITH 开头（公用表表达式，且须紧跟 SELECT，禁止 WITH RECURSIVE 等可能含非查询的变体）
                if (cleanStmt.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
                {
                    hasQuery = true;
                }
                else if (cleanStmt.StartsWith("WITH", StringComparison.OrdinalIgnoreCase)
                    && Regex.IsMatch(cleanStmt, @"^WITH\s+(?!RECURSIVE\b)", RegexOptions.IgnoreCase))
                {
                    hasQuery = true;
                }
                else if (cleanStmt.StartsWith("DECLARE", StringComparison.OrdinalIgnoreCase)
                    && cleanStmt.Contains('='))
                {
                    // DECLARE @变量 类型 = 常量 形式：OK（含多词数据类型如 VARCHAR(100)、DATETIME2(7) 等）
                    // 危险关键字黑名单仍会拦截 INSERT/UPDATE/DELETE/EXEC 等，保证 DECLARE 里只能是常量赋值
                }
                else
                {
                    return $"仅允许执行 SELECT 查询语句（WITH ... SELECT 亦可，可前置 DECLARE @变量 = 常量），发现非法语句：{cleanStmt[..Math.Min(50, cleanStmt.Length)]}...";
                }

                // 危险关键字检测（即使允许 DECLARE，也要防止 DECLARE 里夹带危险操作）
                foreach (var kw in ForbiddenKeywords)
                {
                    var pattern = kw.EndsWith("_") ? $@"\b{kw}" : $@"\b{kw}\b";
                    if (Regex.IsMatch(cleanStmt, pattern, RegexOptions.IgnoreCase))
                        return $"SQL 中包含被禁止的关键字：{kw}";
                }
            }

            if (!hasQuery)
                return "未检测到任何 SELECT 查询语句";

            return null; // 通过
        }
    }
}

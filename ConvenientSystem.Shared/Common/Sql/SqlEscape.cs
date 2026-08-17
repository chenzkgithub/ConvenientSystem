namespace ConvenientSystem.Shared.Common.Sql
{
    /// <summary>
    /// SQL 标识符与字面量转义（防注入），以及列类型显示格式化。
    /// 各数据库引号风格不同，按类型选用对应方法。
    /// </summary>
    public static class SqlEscape
    {
        /// <summary>SQL Server 标识符转义（方括号）</summary>
        public static string Identifier(string name) => "[" + name.Replace("]", "]]") + "]";

        /// <summary>ClickHouse/MySQL 标识符转义（反引号）</summary>
        public static string ChIdent(string name) => "`" + name.Replace("`", "``") + "`";

        /// <summary>ClickHouse 字符串字面量转义</summary>
        public static string ChString(string s) => s.Replace("\\", "\\\\").Replace("'", "\\'");

        /// <summary>通用字符串字面量转义（单引号翻倍，PG/Oracle/SQLite/SQL Server）</summary>
        public static string Literal(string s) => s.Replace("'", "''");

        /// <summary>MySQL 字符串字面量转义（反斜杠 + 单引号）</summary>
        public static string MyLiteral(string s) => s.Replace("\\", "\\\\").Replace("'", "''");

        /// <summary>双引号标识符转义（PG/Oracle/SQLite）</summary>
        public static string QuoteIdent(string name) => "\"" + name.Replace("\"", "\"\"") + "\"";

        /// <summary>格式化列类型显示，如 nvarchar(50)、decimal(18,2)</summary>
        public static string FormatColumnType(string typeName, int maxLength, int precision, int scale)
        {
            switch (typeName.ToLowerInvariant())
            {
                case "nvarchar":
                case "nchar":
                    return maxLength == -1 ? $"{typeName}(MAX)" : $"{typeName}({maxLength / 2})";
                case "varchar":
                case "char":
                case "varbinary":
                case "binary":
                    return maxLength == -1 ? $"{typeName}(MAX)" : $"{typeName}({maxLength})";
                case "decimal":
                case "numeric":
                    return $"{typeName}({precision},{scale})";
                case "datetime2":
                case "datetimeoffset":
                case "time":
                    return $"{typeName}({scale})";
                default:
                    return typeName;
            }
        }
    }
}

using System.Linq.Expressions;
using System.Reflection;
using FreeSql;

namespace ConvenientSystem.Shared.Common
{
    /// <summary>
    /// FreeSql 动态排序扩展：根据前端传入的字段名和方向安全排序。
    /// 字段名必须匹配实体属性名（大小写不敏感），未匹配时静默忽略（保持原排序）。
    /// </summary>
    public static class FreeSqlSortExtensions
    {
        /// <summary>
        /// 按属性名动态排序。
        /// field 为空、或不是 T 的合法属性时静默跳过，不改变原排序。
        /// order 仅接受 "asc"（升序）或 "desc"（降序），其它值视为 asc。
        /// </summary>
        public static ISelect<T> OrderByDynamic<T>(this ISelect<T> query, string? field, string? order)
        {
            if (string.IsNullOrWhiteSpace(field)) return query;

            var prop = typeof(T).GetProperty(field!,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop == null) return query;

            // 构建 Expression<Func<T, TMember>> 并通过反射调用泛型方法
            var param = Expression.Parameter(typeof(T), "x");
            var body = Expression.Property(param, prop);
            var lambdaType = typeof(Func<,>).MakeGenericType(typeof(T), prop.PropertyType);
            var lambda = Expression.Lambda(lambdaType, body, param);

            var isDesc = string.Equals(order, "desc", StringComparison.OrdinalIgnoreCase);
            var methodName = isDesc ? "OrderByDescending" : "OrderBy";

            var method = typeof(ISelect<T>).GetMethods()
                .FirstOrDefault(m => m.Name == methodName
                    && m.GetParameters().Length == 1
                    && m.GetParameters()[0].ParameterType.IsGenericType);
            if (method == null) return query;

            method = method.MakeGenericMethod(prop.PropertyType);
            return (ISelect<T>)method.Invoke(query, [lambda])!;
        }
    }
}

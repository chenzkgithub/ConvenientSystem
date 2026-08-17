namespace ConvenientSystem.Shared.Common.Security
{
    /// <summary>
    /// 角色数据范围：决定角色下用户能查看哪些数据。
    /// 数值越大越宽松；用户拥有多个角色时取最大值（最宽松）。
    /// </summary>
    public enum DataScope
    {
        /// <summary>仅查看本人创建的数据。</summary>
        Self = 0,

        /// <summary>查看所有数据。</summary>
        All = 1,

        // Dept = 2, // 未来可扩展：本部门数据
    }
}

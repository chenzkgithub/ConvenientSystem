namespace ConvenientSystem.Shared.Model.Common
{
    /// <summary>
    /// 分页查询结果：序列化为 { total, list }，与前端既有约定保持一致。
    /// </summary>
    public class PagedResult<T>
    {
        /// <summary>符合条件的总行数</summary>
        public long Total { get; set; }

        /// <summary>当前页数据</summary>
        public List<T> List { get; set; } = new();
    }
}

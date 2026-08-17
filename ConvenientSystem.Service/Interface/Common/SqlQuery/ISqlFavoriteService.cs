using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Common.SqlQuery
{
    /// <summary>
    /// SQL 查询收藏服务：保存用户常用的 SQL 查询，方便快速调用。
    /// </summary>
    public interface ISqlFavoriteService
    {
        /// <summary>获取收藏列表（按排序号、Id 升序）</summary>
        List<SqlFavoriteDto> GetList();

        /// <summary>添加收藏</summary>
        void Add(SqlFavoriteDto dto);

        /// <summary>修改收藏</summary>
        void Update(SqlFavoriteDto dto);

        /// <summary>删除收藏</summary>
        void Remove(SqlFavoriteDto dto);
    }
}

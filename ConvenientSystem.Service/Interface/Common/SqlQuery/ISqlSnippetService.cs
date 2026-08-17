using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Common.SqlQuery
{
    /// <summary>
    /// SQL 快捷输入（代码片段）管理服务：查询工具编辑器中输入简写后展开为完整 SQL 片段。
    /// </summary>
    public interface ISqlSnippetService
    {
        /// <summary>获取全部快捷输入配置（按排序号、Id 升序）</summary>
        List<SnippetDto> GetList();

        /// <summary>添加快捷输入</summary>
        void Add(SnippetDto dto);

        /// <summary>修改快捷输入</summary>
        void Update(SnippetDto dto);

        /// <summary>删除快捷输入</summary>
        void Remove(SnippetDto dto);

        /// <summary>重置快捷输入为初始数据（需输入登录密码确认）</summary>
        Task ResetAsync(SnippetResetDto dto);
    }
}

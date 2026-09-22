using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Common.SqlQuery
{
    /// <summary>
    /// AI 辅助 SQL（NL2SQL）：根据自然语言需求 + 当前数据源表结构生成 SQL。
    /// 复用 AI 配额体系（ai.quota.*）；生成结果只返回纯 SQL，由前端填入编辑器、用户手动执行。
    /// </summary>
    public interface IAiSqlService
    {
        /// <summary>生成 SQL：校验开关/配额 → 解析模型 → 组装 schema 上下文 → 调用模型 → 提取纯 SQL</summary>
        Task<AiSqlGenerateResult> GenerateAsync(Guid userId, AiSqlGenerateRequest request, CancellationToken ct);
    }
}

using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Common;

/// <summary>代码扫描服务接口。</summary>
public interface ICodeScanService
{
    /// <summary>获取所有规则。</summary>
    List<CodeScanRuleDto> ListRules();

    /// <summary>新建或更新规则。</summary>
    void SaveRule(CodeScanRuleSaveDto dto);

    /// <summary>删除规则。</summary>
    void DeleteRule(int id);

    /// <summary>导入内置规则模板（幂等）。</summary>
    void ImportTemplates();

    /// <summary>执行扫描。</summary>
    CodeScanResultDto Scan(CodeScanRequestDto request);

    /// <summary>获取扫描历史记录（最近 N 条）。</summary>
    List<CodeScanResultDto> ListResults(int limit = 20);

    /// <summary>获取某次扫描的详细结果（含问题列表）。</summary>
    CodeScanResultDto GetResult(int scanId);
}

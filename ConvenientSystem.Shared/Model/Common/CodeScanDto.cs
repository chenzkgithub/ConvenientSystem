namespace ConvenientSystem.Shared.Model.Common;

/// <summary>检测规则 DTO。</summary>
public class CodeScanRuleDto
{
    public int Id { get; set; }
    /// <summary>规则名称（如"硬编码密码"）。</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>正则表达式模式。</summary>
    public string Pattern { get; set; } = string.Empty;
    /// <summary>严重等级：Error / Warning / Info。</summary>
    public string Severity { get; set; } = "Warning";
    /// <summary>文件 glob 过滤（如 "*.cs"、"*.json"，空=全部文件）。</summary>
    public string FileGlob { get; set; } = string.Empty;
    /// <summary>是否启用。</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>规则说明。</summary>
    public string Description { get; set; } = string.Empty;
}

/// <summary>扫描执行记录 DTO。</summary>
public class CodeScanResultDto
{
    public int Id { get; set; }
    /// <summary>扫描目录。</summary>
    public string TargetPath { get; set; } = string.Empty;
    /// <summary>扫描模式：full（全量）/ diff（增量）。</summary>
    public string ScanMode { get; set; } = "full";
    /// <summary>扫描文件数。</summary>
    public int FileCount { get; set; }
    /// <summary>发现问题总数。</summary>
    public int IssueCount { get; set; }
    /// <summary>Error 数量。</summary>
    public int ErrorCount { get; set; }
    /// <summary>Warning 数量。</summary>
    public int WarningCount { get; set; }
    /// <summary>Info 数量。</summary>
    public int InfoCount { get; set; }
    /// <summary>扫描耗时（毫秒）。</summary>
    public long DurationMs { get; set; }
    /// <summary>扫描时间。</summary>
    public string CreateTime { get; set; } = string.Empty;
    /// <summary>关联的问题列表。</summary>
    public List<CodeScanIssueDto> Issues { get; set; } = [];
}

/// <summary>具体问题 DTO。</summary>
public class CodeScanIssueDto
{
    public int Id { get; set; }
    /// <summary>触发的规则名称。</summary>
    public string RuleName { get; set; } = string.Empty;
    /// <summary>严重等级。</summary>
    public string Severity { get; set; } = string.Empty;
    /// <summary>文件路径（相对路径）。</summary>
    public string FilePath { get; set; } = string.Empty;
    /// <summary>行号。</summary>
    public int LineNumber { get; set; }
    /// <summary>匹配的代码片段（该行内容）。</summary>
    public string LineContent { get; set; } = string.Empty;
}

/// <summary>执行扫描请求 DTO。</summary>
public class CodeScanRequestDto
{
    /// <summary>扫描目标目录（绝对路径或相对路径）。</summary>
    public string TargetPath { get; set; } = string.Empty;
    /// <summary>扫描模式：full（全量）/ diff（增量，仅 git diff 变更文件）。</summary>
    public string ScanMode { get; set; } = "full";
}

/// <summary>新建/编辑规则请求 DTO。</summary>
public class CodeScanRuleSaveDto
{
    public int? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Pattern { get; set; } = string.Empty;
    public string Severity { get; set; } = "Warning";
    public string FileGlob { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public string Description { get; set; } = string.Empty;
}

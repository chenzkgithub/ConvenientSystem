using FreeSql.DataAnnotations;

namespace ConvenientSystem.Shared.Entity.Common;

/// <summary>代码扫描检测规则表。</summary>
[Table(Name = "CodeScanRule")]
public class CodeScanRuleEntity
{
    [Column(IsIdentity = true, IsPrimary = true)]
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Pattern { get; set; } = string.Empty;
    public string Severity { get; set; } = "Warning";
    public string FileGlob { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public string Description { get; set; } = string.Empty;
}

/// <summary>代码扫描执行记录表。</summary>
[Table(Name = "CodeScanResult")]
public class CodeScanResultEntity
{
    [Column(IsIdentity = true, IsPrimary = true)]
    public int Id { get; set; }
    public string TargetPath { get; set; } = string.Empty;
    public string ScanMode { get; set; } = "full";
    public int FileCount { get; set; }
    public int IssueCount { get; set; }
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
    public int InfoCount { get; set; }
    public long DurationMs { get; set; }
    public DateTime CreateTime { get; set; }
}

/// <summary>代码扫描问题明细表。</summary>
[Table(Name = "CodeScanIssue")]
public class CodeScanIssueEntity
{
    [Column(IsIdentity = true, IsPrimary = true)]
    public int Id { get; set; }
    public int ScanId { get; set; }
    public string RuleName { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public int LineNumber { get; set; }
    public string LineContent { get; set; } = string.Empty;
}

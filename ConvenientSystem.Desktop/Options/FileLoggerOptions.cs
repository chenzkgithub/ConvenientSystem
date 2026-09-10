using Microsoft.Extensions.Logging;

namespace ConvenientSystem;

/// <summary>桌面文件日志选项。</summary>
public sealed class FileLoggerOptions
{
    /// <summary>日志文件目录，相对 exe 目录。</summary>
    public string LogDirectory { get; set; } = "logs";

    /// <summary>文件名前缀。</summary>
    public string FileNamePrefix { get; set; } = "desktop";

    /// <summary>单文件大小上限（字节），默认 10MB。</summary>
    public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>保留文件数量，默认 7 个。</summary>
    public int RetainedFileCount { get; set; } = 7;

    /// <summary>最低日志级别，默认 Information。</summary>
    public LogLevel MinimumLevel { get; set; } = LogLevel.Information;
}

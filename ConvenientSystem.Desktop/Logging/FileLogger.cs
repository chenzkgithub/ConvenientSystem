using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace ConvenientSystem;

/// <summary>简单的文件日志记录器：按天滚动、限制保留数量、线程安全。</summary>
internal sealed class FileLogger : ILogger
{
    private readonly string _category;
    private readonly FileLoggerProvider _provider;

    public FileLogger(string category, FileLoggerProvider provider)
    {
        _category = category;
        _provider = provider;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= _provider.Options.MinimumLevel;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var message = formatter(state, exception);
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{logLevel}] [{_category}] {message}";
        if (exception != null)
        {
            line += Environment.NewLine + exception;
        }

        _provider.WriteLine(line);
    }
}

/// <summary>文件日志提供器：管理日志目录、文件滚动与清理。</summary>
internal sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _buffer = new();
    private readonly System.Threading.Timer _flushTimer;
    private readonly string _logFilePath;
    private readonly object _rollLock = new();

    public FileLoggerOptions Options { get; }

    public FileLoggerProvider(FileLoggerOptions options)
    {
        Options = options;
        var logDir = Path.Combine(AppContext.BaseDirectory, options.LogDirectory);
        Directory.CreateDirectory(logDir);
        _logFilePath = Path.Combine(logDir, $"{options.FileNamePrefix}-{DateTime.Now:yyyyMMdd}.log");
        _flushTimer = new System.Threading.Timer(_ => Flush(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, this);

    public void WriteLine(string line)
    {
        _buffer.Enqueue(line);
    }

    private void Flush()
    {
        if (_buffer.IsEmpty) return;

        var lines = new List<string>();
        while (_buffer.TryDequeue(out var line))
        {
            lines.Add(line);
        }

        if (lines.Count == 0) return;

        lock (_rollLock)
        {
            try
            {
                RollIfNeeded();
                File.AppendAllLines(_logFilePath, lines);
            }
            catch
            {
                // 日志写入失败不应影响主程序，丢弃即可
            }
        }
    }

    private void RollIfNeeded()
    {
        try
        {
            var fileInfo = new FileInfo(_logFilePath);
            if (!fileInfo.Exists || fileInfo.Length < Options.MaxFileSizeBytes) return;

            var rolledPath = Path.Combine(
                fileInfo.DirectoryName!,
                $"{Options.FileNamePrefix}-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            File.Move(_logFilePath, rolledPath);
            CleanupOldFiles(fileInfo.DirectoryName!);
        }
        catch
        {
            // 滚动失败不阻塞写入
        }
    }

    private void CleanupOldFiles(string directory)
    {
        try
        {
            var files = Directory
                .GetFiles(directory, $"{Options.FileNamePrefix}-*.log")
                .OrderByDescending(File.GetLastWriteTime)
                .Skip(Options.RetainedFileCount);
            foreach (var file in files)
            {
                try { File.Delete(file); }
                catch { /* 忽略删除失败 */ }
            }
        }
        catch
        {
            // 清理失败不影响运行
        }
    }

    public void Dispose()
    {
        _flushTimer.Dispose();
        Flush();
    }
}

/// <summary>文件日志扩展方法。</summary>
internal static class FileLoggerExtensions
{
    public static ILoggingBuilder AddFile(this ILoggingBuilder builder, Action<FileLoggerOptions>? configure = null)
    {
        var options = new FileLoggerOptions();
        configure?.Invoke(options);
        builder.AddProvider(new FileLoggerProvider(options));
        return builder;
    }
}

using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace ConvenientSystem.Api.Middleware
{
    /// <summary>
    /// 内存日志缓冲：捕获最近 500 条日志，供实时日志查看器使用。
    /// 记录 Information 及以上级别。
    /// </summary>
    public class MemoryLogBuffer
    {
        private const int MaxEntries = 500;
        private readonly ConcurrentQueue<LogEntry> _entries = new();

        public void Add(LogLevel level, string category, string message, string? exception)
        {
            if (level < LogLevel.Information) return;

            var entry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = level.ToString(),
                Category = category.Length > 60 ? category[^60..] : category,
                Message = message,
                Exception = exception,
            };
            _entries.Enqueue(entry);

            // 超出上限时移除最旧的
            while (_entries.Count > MaxEntries && _entries.TryDequeue(out _)) { }
        }

        public List<LogEntry> GetRecent(int count = 100, string? keyword = null, string? level = null)
        {
            var query = _entries.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(level))
                query = query.Where(e => e.Level.Equals(level, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(keyword))
                query = query.Where(e => e.Message.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    || e.Category.Contains(keyword, StringComparison.OrdinalIgnoreCase));
            return query.TakeLast(count).ToList();
        }

        public void Clear()
        {
            while (_entries.TryDequeue(out _)) { }
        }
    }

    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public string Level { get; set; } = "";
        public string Category { get; set; } = "";
        public string Message { get; set; } = "";
        public string? Exception { get; set; }
    }

    /// <summary>
    /// 自定义 ILogger，将日志写入 MemoryLogBuffer
    /// </summary>
    public class MemoryLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly MemoryLogBuffer _buffer;

        public MemoryLogger(string categoryName, MemoryLogBuffer buffer)
        {
            _categoryName = categoryName;
            _buffer = buffer;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var message = formatter(state, exception);
            _buffer.Add(logLevel, _categoryName, message, exception?.ToString());
        }
    }

    /// <summary>
    /// 自定义 ILoggerProvider，注册后所有日志都会写入内存缓冲
    /// </summary>
    [ProviderAlias("Memory")]
    public class MemoryLoggerProvider : ILoggerProvider
    {
        private readonly MemoryLogBuffer _buffer;

        public MemoryLoggerProvider(MemoryLogBuffer buffer)
        {
            _buffer = buffer;
        }

        public ILogger CreateLogger(string categoryName) => new MemoryLogger(categoryName, _buffer);

        public void Dispose() { }
    }
}

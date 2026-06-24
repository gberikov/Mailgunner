using Microsoft.Extensions.Logging;

namespace Mailgunner.Tests.Fakes;

/// <summary>A single captured log record: its level, category, rendered message, and event id.</summary>
internal readonly record struct LogRecord(LogLevel Level, string Category, string Message, int EventId);

/// <summary>
/// An <see cref="ILoggerProvider"/> that captures every emitted record, letting a test assert the
/// exhaustion Warning was logged and inspect its contents (to confirm no secret leaks).
/// </summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly List<LogRecord> _records = new();

    /// <summary>Gets every captured record, in order.</summary>
    public IReadOnlyList<LogRecord> Records => _records;

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _records);

    /// <inheritdoc />
    public void Dispose()
    {
    }

    private sealed class CapturingLogger : ILogger
    {
        private readonly string _category;
        private readonly List<LogRecord> _records;

        public CapturingLogger(string category, List<LogRecord> records)
        {
            _category = category;
            _records = records;
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _records.Add(new LogRecord(logLevel, _category, formatter(state, exception), eventId.Id));
        }
    }
}

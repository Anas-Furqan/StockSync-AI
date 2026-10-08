using System.Collections.Concurrent;

namespace StockSyncAI.Api.Logging;

public sealed class DailyFileLoggerProvider(string logsDirectory) : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, DailyFileLogger> _loggers = new();

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new DailyFileLogger(logsDirectory, name));

    public void Dispose() => _loggers.Clear();

    private sealed class DailyFileLogger(string directory, string category) : ILogger
    {
        private static readonly object WriteLock = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"stocksync-{DateTime.UtcNow:yyyyMMdd}.log");
            var line = $"{DateTimeOffset.UtcNow:O} [{logLevel}] {category}: {formatter(state, exception)}";
            if (exception is not null)
            {
                line += $"{Environment.NewLine}{exception}";
            }

            lock (WriteLock)
            {
                File.AppendAllText(path, line + Environment.NewLine);
            }
        }
    }
}

using Microsoft.Extensions.Logging;

namespace TmSync.Manager;

/// <summary>Routes log output to a callback so sync runs can stream into the GUI log pane.</summary>
public sealed class CallbackLoggerProvider : ILoggerProvider
{
    private readonly Action<string> _write;

    public CallbackLoggerProvider(Action<string> write) => _write = write;

    public ILogger CreateLogger(string categoryName) => new CallbackLogger(_write, categoryName);

    public void Dispose() { }

    private sealed class CallbackLogger : ILogger
    {
        private readonly Action<string> _write;
        private readonly string _category;

        public CallbackLogger(Action<string> write, string category)
        {
            _write = write;
            _category = category[(category.LastIndexOf('.') + 1)..];
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var line = $"[{DateTime.Now:HH:mm:ss}] {logLevel,-11} {_category}: {formatter(state, exception)}";
            if (exception != null) line += $" | {exception.GetType().Name}: {exception.Message}";
            _write(line);
        }
    }
}

using Microsoft.Extensions.Logging;
using TmSync.Core.Abstractions;

namespace TmSync.Core.State;

/// <summary>
/// Persists TmSync log output (Information and above) into the SyncLog table of the state
/// database so the GUI log viewer shows activity from the service, the CLI and GUI-triggered
/// runs alike. Logging failures are swallowed: the log must never break a sync.
/// </summary>
public sealed class StateStoreLoggerProvider : ILoggerProvider
{
    private readonly IStateStore _state;

    public StateStoreLoggerProvider(IStateStore state) => _state = state;

    public ILogger CreateLogger(string categoryName) => new StoreLogger(_state, categoryName);

    public void Dispose() { }

    private sealed class StoreLogger : ILogger
    {
        private readonly IStateStore _state;
        private readonly string _category;
        private readonly bool _enabled;

        public StoreLogger(IStateStore state, string category)
        {
            _state = state;
            _category = category;
            _enabled = category.StartsWith("TmSync", StringComparison.Ordinal);
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => _enabled && logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            try
            {
                var message = formatter(state, exception);
                if (exception != null) message += $" | {exception.GetType().Name}: {exception.Message}";
                var source = _category[(_category.LastIndexOf('.') + 1)..];
                _state.AddLogEntry(DateTime.UtcNow, logLevel.ToString(), source, message);
            }
            catch
            {
                // Never let log persistence break the sync itself.
            }
        }
    }
}

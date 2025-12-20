using Microsoft.Extensions.Logging;

namespace SharedUI.Logging;

public sealed class MogeFileLoggerProvider : ILoggerProvider
{
    private readonly MogeLogService _log;

    public MogeFileLoggerProvider(MogeLogService log)
    {
        _log = log;
    }

    public ILogger CreateLogger(string categoryName) => new MogeFileLogger(_log, categoryName);

    public void Dispose()
    {
    }

    private sealed class MogeFileLogger : ILogger
    {
        private readonly MogeLogService _log;
        private readonly string _category;

        public MogeFileLogger(MogeLogService log, string category)
        {
            _log = log;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            string message;
            try
            {
                message = formatter(state, exception);
            }
            catch
            {
                message = state?.ToString() ?? string.Empty;
            }

            _log.Log(logLevel, _category, message, exception);
        }
    }
}

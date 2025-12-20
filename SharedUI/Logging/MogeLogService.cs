using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace SharedUI.Logging;

public sealed class MogeLogService
{
    private readonly ILogFileStore _store;
    private readonly MogeLogOptions _options;

    private readonly Channel<string> _queue = Channel.CreateUnbounded<string>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

    private int _initialized;
    private CancellationTokenSource? _cts;

    public MogeLogService(ILogFileStore store, MogeLogOptions options)
    {
        _store = store;
        _options = options;
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 1)
            return Task.CompletedTask;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ct = _cts.Token;

        _ = Task.Run(() => WriterLoopAsync(ct), ct);
        _ = Task.Run(() => CleanupLoopAsync(ct), ct);

        EnqueueInternal($"{Timestamp()} [INF] MOGE: logging initialized (platform={_options.PlatformSubfolder})");
        return Task.CompletedTask;
    }

    public void Log(LogLevel level, string category, string message, Exception? exception = null)
    {
        var line = FormatLine(level, category, message, exception);
        EnqueueInternal(line);
    }

    public void Info(string message) => Log(LogLevel.Information, "MOGE", message);

    internal void EnqueueInternal(string line)
    {
        // Best-effort logging: never throw to caller.
        try
        {
            _queue.Writer.TryWrite(line);
        }
        catch
        {
        }
    }

    private async Task WriterLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var line in _queue.Reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    var day = DateOnly.FromDateTime(DateTime.Now);
                    await _store.AppendLineAsync(day, line, cancellationToken);
                }
                catch
                {
                    // Swallow: logging must not crash the app.
                }
            }
        }
        catch
        {
        }
    }

    private async Task CleanupLoopAsync(CancellationToken cancellationToken)
    {
        // Run once on startup then every hour.
        try
        {
            await CleanupOnceAsync(cancellationToken);

            using var timer = new PeriodicTimer(_options.CleanupIntervalOrDefault);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await CleanupOnceAsync(cancellationToken);
            }
        }
        catch
        {
        }
    }

    private async Task CleanupOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            var deleteBefore = DateOnly.FromDateTime(DateTime.Now.Date.AddDays(-_options.RetentionDays));
            await _store.CleanupAsync(deleteBefore, cancellationToken);
        }
        catch
        {
        }
    }

    private static string Timestamp() => DateTimeOffset.Now.ToString("O");

    private static string LevelShort(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "UNK"
    };

    private static string FormatLine(LogLevel level, string category, string message, Exception? exception)
    {
        var line = $"{Timestamp()} [{LevelShort(level)}] {category}: {message}";
        if (exception is not null)
        {
            line += Environment.NewLine + exception;
        }

        return line;
    }
}

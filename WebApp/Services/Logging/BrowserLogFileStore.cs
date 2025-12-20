using Microsoft.JSInterop;
using SharedUI.Logging;

namespace WebApp.Services.Logging;

internal sealed class BrowserLogFileStore : ILogFileStore
{
    private readonly IJSRuntime _js;
    private readonly MogeLogOptions _options;

    public BrowserLogFileStore(IJSRuntime js, MogeLogOptions options)
    {
        _js = js;
        _options = options;
    }

    public async Task AppendLineAsync(DateOnly day, string line, CancellationToken cancellationToken = default)
    {
        try
        {
            await _js.InvokeVoidAsync(
                "mogeLogger.appendDailyLog",
                cancellationToken,
                _options.PlatformSubfolder,
                day.ToString("yyyy-MM-dd"),
                line);
        }
        catch
        {
            // Best-effort: ignore if OPFS not supported.
        }
    }

    public async Task CleanupAsync(DateOnly deleteBeforeDay, CancellationToken cancellationToken = default)
    {
        try
        {
            await _js.InvokeVoidAsync(
                "mogeLogger.cleanup",
                cancellationToken,
                _options.PlatformSubfolder,
                deleteBeforeDay.ToString("yyyy-MM-dd"));
        }
        catch
        {
        }
    }
}

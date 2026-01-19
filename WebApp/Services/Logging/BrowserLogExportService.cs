using Microsoft.JSInterop;
using SharedUI.Logging;
using SharedUI.Services;

namespace WebApp.Services.Logging;

internal sealed class BrowserLogExportService(IJSRuntime js, ILogFileStore store) : ILogExportService
{
    public async Task<LogExportResult> ExportLatestAsync(string suggestedFileName, CancellationToken cancellationToken = default)
    {
        var day = DateOnly.FromDateTime(DateTime.Now);
        var text = await store.ReadAllTextAsync(day, cancellationToken);
        if (string.IsNullOrWhiteSpace(text))
            return new LogExportResult(false, "No logs found for today.");

        var safeName = FileNameUtil.GetSafeFileName(suggestedFileName, $"moge-log-{day:yyyy-MM-dd}.txt", ".txt");
        await js.InvokeVoidAsync("mogeLogger.downloadText", cancellationToken, safeName, text);
        return new LogExportResult(true, $"Exported: {safeName}");
    }
}

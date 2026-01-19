namespace SharedUI.Logging;

public interface ILogExportService
{
    Task<LogExportResult> ExportLatestAsync(string suggestedFileName, CancellationToken cancellationToken = default);
}

public sealed record LogExportResult(bool Success, string Message);

using SharedUI.Logging;

namespace HybridApp.Services.Logging;

internal sealed class MauiLogFileStore : ILogFileStore
{
    private readonly string _logDir;

    public MauiLogFileStore(MogeLogOptions options)
    {
        // AppDataDirectory/<base>/app or web
        var baseDir = Microsoft.Maui.Storage.FileSystem.AppDataDirectory;
        _logDir = Path.Combine(baseDir, "moge-logs", options.PlatformSubfolder);
        Directory.CreateDirectory(_logDir);
    }

    public async Task AppendLineAsync(DateOnly day, string line, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(_logDir, LogPath.DailyFileName(day));
        Directory.CreateDirectory(_logDir);

        // Ensure each entry is separated.
        var payload = line + Environment.NewLine;
        await File.AppendAllTextAsync(path, payload, cancellationToken);
    }

    public Task CleanupAsync(DateOnly deleteBeforeDay, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!Directory.Exists(_logDir))
                return Task.CompletedTask;

            foreach (var file in Directory.EnumerateFiles(_logDir, "*.log", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (!DateOnly.TryParseExact(name, "yyyy-MM-dd", out var day))
                    continue;

                if (day < deleteBeforeDay)
                {
                    try { File.Delete(file); } catch { }
                }
            }
        }
        catch
        {
        }

        return Task.CompletedTask;
    }
}

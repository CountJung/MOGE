namespace SharedUI.Logging;

public interface ILogFileStore
{
    Task AppendLineAsync(DateOnly day, string line, CancellationToken cancellationToken = default);

    Task CleanupAsync(DateOnly deleteBeforeDay, CancellationToken cancellationToken = default);

    Task<string?> ReadAllTextAsync(DateOnly day, CancellationToken cancellationToken = default);
}

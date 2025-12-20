namespace SharedUI.Logging;

public interface ILogFileStore
{
    Task AppendLineAsync(DateOnly day, string line, CancellationToken cancellationToken = default);

    Task CleanupAsync(DateOnly deleteBeforeDay, CancellationToken cancellationToken = default);
}

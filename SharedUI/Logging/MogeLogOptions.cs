namespace SharedUI.Logging;

public sealed record MogeLogOptions(
    string PlatformSubfolder,
    int RetentionDays = 10,
    TimeSpan CleanupInterval = default)
{
    public TimeSpan CleanupIntervalOrDefault => CleanupInterval == default
        ? TimeSpan.FromHours(1)
        : CleanupInterval;
}

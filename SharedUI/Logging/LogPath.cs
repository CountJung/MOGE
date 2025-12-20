namespace SharedUI.Logging;

public static class LogPath
{
    public static string DailyFileName(DateOnly day) => $"{day:yyyy-MM-dd}.log";
}

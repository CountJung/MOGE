using SharedUI.Logging;
using SharedUI.Services;

namespace HybridApp.Services.Logging;

internal sealed class WindowsLogExportService(ILogFileStore store) : ILogExportService
{
    public async Task<LogExportResult> ExportLatestAsync(string suggestedFileName, CancellationToken cancellationToken = default)
    {
        var day = DateOnly.FromDateTime(DateTime.Now);
        var text = await store.ReadAllTextAsync(day, cancellationToken);
        if (string.IsNullOrWhiteSpace(text))
            return new LogExportResult(false, "No logs found for today.");

        var safeName = FileNameUtil.GetSafeFileName(suggestedFileName, $"moge-log-{day:yyyy-MM-dd}.txt", ".txt");

#if WINDOWS
        var file = await PickLogSaveFileAsync(safeName);
        if (file is null)
            return new LogExportResult(false, "Log export canceled.");

        await Windows.Storage.FileIO.WriteTextAsync(file, text);
        return new LogExportResult(true, $"Exported: {file.Name}");
#else
        throw new PlatformNotSupportedException("Log export is only implemented for Windows in HybridApp.");
#endif
    }

#if WINDOWS
    private static async Task<Windows.Storage.StorageFile?> PickLogSaveFileAsync(string suggestedFileName)
    {
        var safeBaseName = FileNameUtil.GetSafeBaseName(suggestedFileName, "moge-log");
        var picker = new Windows.Storage.Pickers.FileSavePicker
        {
            SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary,
            SuggestedFileName = safeBaseName
        };

        picker.FileTypeChoices.Add("Log file", new List<string> { ".txt", ".log" });

        var hwnd = GetWindowHandle();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        return await picker.PickSaveFileAsync();
    }

    private static IntPtr GetWindowHandle()
    {
        var window = Application.Current?.Windows?.FirstOrDefault();
        if (window?.Handler?.PlatformView is null)
            return IntPtr.Zero;

        return WinRT.Interop.WindowNative.GetWindowHandle(window.Handler.PlatformView);
    }
#endif
}

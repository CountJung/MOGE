using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using SharedUI.Services;

namespace HybridApp.Services;

internal sealed class WindowsImageExportService(IJSRuntime js) : IImageExportService
{
    public async Task SaveAsync(ElementReference canvas, string suggestedFileName, ImageExportFormat format, CancellationToken cancellationToken = default)
    {
        var filename = format == ImageExportFormat.Jpeg
            ? FileNameUtil.GetSafeFileName(suggestedFileName, "image.jpg", ".jpg")
            : FileNameUtil.GetSafeFileName(suggestedFileName, "image.png", ".png");

        var fn = format == ImageExportFormat.Jpeg ? "mogeCanvas.exportJpegBase64" : "mogeCanvas.exportPngBase64";
        var base64 = await js.InvokeAsync<string?>(fn, cancellationToken, canvas);
        if (string.IsNullOrWhiteSpace(base64))
            return;

        var bytes = Convert.FromBase64String(base64);

#if WINDOWS
    var file = await PickSaveFileAsync(filename, format);
        if (file is null)
            return;

        await Windows.Storage.FileIO.WriteBytesAsync(file, bytes);
#else
        throw new PlatformNotSupportedException("PNG export is only implemented for Windows in HybridApp.");
#endif
    }

#if WINDOWS
    private static async Task<Windows.Storage.StorageFile?> PickSaveFileAsync(string suggestedFileName, ImageExportFormat format)
    {
        var safeBaseName = FileNameUtil.GetSafeBaseName(suggestedFileName, "image");

        var picker = new Windows.Storage.Pickers.FileSavePicker
        {
            SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary,
            SuggestedFileName = safeBaseName
        };

        if (format == ImageExportFormat.Jpeg)
            picker.FileTypeChoices.Add("JPEG Image", new List<string> { ".jpg", ".jpeg" });
        else
            picker.FileTypeChoices.Add("PNG Image", new List<string> { ".png" });

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

using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using SharedUI.Services;

namespace HybridApp.Services;

internal sealed class WindowsImageExportService(IJSRuntime js) : IImageExportService
{
    public async Task SavePngAsync(ElementReference canvas, string suggestedFileName, CancellationToken cancellationToken = default)
    {
        var filename = FileNameUtil.GetSafeFileName(suggestedFileName, "image.png", ".png");

        var base64 = await js.InvokeAsync<string?>("mogeCanvas.exportPngBase64", cancellationToken, canvas);
        if (string.IsNullOrWhiteSpace(base64))
            return;

        var bytes = Convert.FromBase64String(base64);

#if WINDOWS
        var file = await PickPngSaveFileAsync(filename);
        if (file is null)
            return;

        await Windows.Storage.FileIO.WriteBytesAsync(file, bytes);
#else
        throw new PlatformNotSupportedException("PNG export is only implemented for Windows in HybridApp.");
#endif
    }

#if WINDOWS
    private static async Task<Windows.Storage.StorageFile?> PickPngSaveFileAsync(string suggestedFileName)
    {
        var safeBaseName = FileNameUtil.GetSafeBaseName(suggestedFileName, "image");

        var picker = new Windows.Storage.Pickers.FileSavePicker
        {
            SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary,
            SuggestedFileName = safeBaseName
        };

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

using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using SharedUI.Services;

namespace WebApp.Services;

internal sealed class BrowserImageExportService(IJSRuntime js) : IImageExportService
{
    public Task SaveAsync(ElementReference canvas, string suggestedFileName, ImageExportFormat format, CancellationToken cancellationToken = default)
    {
        var filename = format == ImageExportFormat.Jpeg
            ? FileNameUtil.GetSafeFileName(suggestedFileName, "image.jpg", ".jpg")
            : FileNameUtil.GetSafeFileName(suggestedFileName, "image.png", ".png");

        var fn = format == ImageExportFormat.Jpeg ? "mogeCanvas.downloadJpeg" : "mogeCanvas.downloadPng";
        return js.InvokeVoidAsync(fn, cancellationToken, canvas, filename).AsTask();
    }
}

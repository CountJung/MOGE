using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using SharedUI.Services;

namespace WebApp.Services;

internal sealed class BrowserImageExportService(IJSRuntime js) : IImageExportService
{
    public Task SavePngAsync(ElementReference canvas, string suggestedFileName, CancellationToken cancellationToken = default)
    {
        var filename = FileNameUtil.GetSafeFileName(suggestedFileName, "image.png", ".png");

        return js.InvokeVoidAsync("mogeCanvas.downloadPng", cancellationToken, canvas, filename).AsTask();
    }
}

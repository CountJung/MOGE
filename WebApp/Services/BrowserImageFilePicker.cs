using System.Text;
using Microsoft.JSInterop;
using SharedUI.Services;
using SharedUI.Services.Raw;
using WebApp.Services.Raw;

namespace WebApp.Services;

internal sealed class BrowserImageFilePicker(IJSRuntime js, BrowserRawImageProvider rawProvider) : IImageFilePicker
{
    private readonly IJSRuntime _js = js;
    private readonly BrowserRawImageProvider _rawProvider = rawProvider;

    private sealed record Picked(string fileName, string contentType, string base64, int width, int height, string? rgbaBase64);

    public async Task<ImagePickResult?> PickImageAsync(CancellationToken cancellationToken = default)
    {
        var picks = await PickImagesAsync(cancellationToken);
        return picks.Count > 0 ? picks[0] : null;
    }

    public async Task<IReadOnlyList<ImagePickResult>> PickImagesAsync(CancellationToken cancellationToken = default)
    {
        var picked = await _js.InvokeAsync<Picked[]>(
            "mogeFilePicker.pickImages",
            cancellationToken);

        if (picked is null || picked.Length == 0)
            return Array.Empty<ImagePickResult>();

        var results = new List<ImagePickResult>(picked.Length);
        foreach (var item in picked)
        {
            var bytes = Convert.FromBase64String(item.base64);

            // Cache raw RGBA for OpenCV-decode fallback on WASM.
            if (item.width > 0 && item.height > 0 && !string.IsNullOrWhiteSpace(item.rgbaBase64))
            {
                try
                {
                    var rgba = Convert.FromBase64String(item.rgbaBase64!);
                    var signature = ImageSignature.Create(bytes);
                    _rawProvider.Set(signature, item.width, item.height, rgba);
                }
                catch
                {
                }
            }

            results.Add(new ImagePickResult(item.fileName, item.contentType, bytes));
        }

        return results;
    }

    // Signature generation moved to SharedUI.Services.Raw.ImageSignature
}

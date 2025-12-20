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
        var picked = await _js.InvokeAsync<Picked?>(
            "mogeFilePicker.pickImage",
            cancellationToken);

        if (picked is null)
            return null;

        var bytes = Convert.FromBase64String(picked.base64);

        // Cache raw RGBA for OpenCV-decode fallback on WASM.
        if (picked.width > 0 && picked.height > 0 && !string.IsNullOrWhiteSpace(picked.rgbaBase64))
        {
            try
            {
                var rgba = Convert.FromBase64String(picked.rgbaBase64!);
                var signature = ImageSignature.Create(bytes);
                _rawProvider.Set(signature, picked.width, picked.height, rgba);
            }
            catch
            {
            }
        }

        return new ImagePickResult(picked.fileName, picked.contentType, bytes);
    }

    // Signature generation moved to SharedUI.Services.Raw.ImageSignature
}

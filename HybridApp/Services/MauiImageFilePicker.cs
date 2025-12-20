using Microsoft.IO;
using SharedUI.Services;

namespace HybridApp.Services;

internal sealed class MauiImageFilePicker : IImageFilePicker
{
    private static readonly RecyclableMemoryStreamManager StreamManager = new();

    public async Task<ImagePickResult?> PickImageAsync(CancellationToken cancellationToken = default)
    {
        var result = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "Open image",
            FileTypes = FilePickerFileType.Images
        });

        if (result is null)
            return null;

        await using var input = await result.OpenReadAsync();
        await using var buffer = StreamManager.GetStream();
        await input.CopyToAsync(buffer, cancellationToken);

        return new ImagePickResult(
            result.FileName,
            result.ContentType ?? "application/octet-stream",
            buffer.ToArray());
    }
}

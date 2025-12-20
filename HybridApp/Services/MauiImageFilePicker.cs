using Microsoft.IO;
using SharedUI.Services;

namespace HybridApp.Services;

internal sealed class MauiImageFilePicker : IImageFilePicker
{
    private static readonly RecyclableMemoryStreamManager StreamManager = new();

    public async Task<ImagePickResult?> PickImageAsync(CancellationToken cancellationToken = default)
    {
        var results = await PickImagesAsync(cancellationToken);
        return results.Count > 0 ? results[0] : null;
    }

    public async Task<IReadOnlyList<ImagePickResult>> PickImagesAsync(CancellationToken cancellationToken = default)
    {
        var results = await FilePicker.Default.PickMultipleAsync(new PickOptions
        {
            PickerTitle = "Open image(s)",
            FileTypes = FilePickerFileType.Images
        });

        if (results is null)
            return Array.Empty<ImagePickResult>();

        var list = new List<ImagePickResult>();
        foreach (var item in results)
        {
            if (item is null)
                continue;

            await using var input = await item.OpenReadAsync();
            await using var buffer = StreamManager.GetStream();
            await input.CopyToAsync(buffer, cancellationToken);

            list.Add(new ImagePickResult(
                item.FileName,
                item.ContentType ?? "application/octet-stream",
                buffer.ToArray()));
        }

        return list;
    }
}

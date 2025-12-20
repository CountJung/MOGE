namespace SharedUI.Services;

public sealed record ImagePickResult(
    string FileName,
    string ContentType,
    byte[] Bytes);

public interface IImageFilePicker
{
    Task<ImagePickResult?> PickImageAsync(CancellationToken cancellationToken = default);
}

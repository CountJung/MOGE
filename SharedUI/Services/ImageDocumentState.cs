namespace SharedUI.Services;

public sealed class ImageDocumentState
{
    public event Action? Changed;

    public string? FileName { get; private set; }
    public string? ContentType { get; private set; }
    public byte[]? Bytes { get; private set; }

    public bool HasImage => Bytes is { Length: > 0 };

    public void Set(ImagePickResult pick)
    {
        FileName = pick.FileName;
        ContentType = pick.ContentType;
        Bytes = pick.Bytes;
        Changed?.Invoke();
    }

    public void Clear()
    {
        FileName = null;
        ContentType = null;
        Bytes = null;
        Changed?.Invoke();
    }
}

namespace SharedUI.Services.Raw;

public interface IRawImageProvider
{
    bool TryGet(string signature, out RawRgbaImage image);
}

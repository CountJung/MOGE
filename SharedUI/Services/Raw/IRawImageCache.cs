namespace SharedUI.Services.Raw;

public interface IRawImageCache : IRawImageProvider
{
    void Set(string signature, RawRgbaImage image);
}

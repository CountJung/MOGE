namespace SharedUI.Services.Raw;

public readonly record struct RawRgbaImage(int Width, int Height, byte[] RgbaBytes);

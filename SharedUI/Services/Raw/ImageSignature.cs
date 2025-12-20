namespace SharedUI.Services.Raw;

public static class ImageSignature
{
    public static string Create(byte[] bytes)
    {
        var prefixLen = Math.Min(16, bytes.Length);
        Span<byte> prefix = stackalloc byte[prefixLen];
        bytes.AsSpan(0, prefixLen).CopyTo(prefix);
        return $"{bytes.Length}:{Convert.ToHexString(prefix)}";
    }
}

using System.Security.Cryptography;

namespace SharedUI.Services.Raw;

public static class RawToken
{
    private static ReadOnlySpan<byte> Magic => "MOGERAW1"u8;

    public static bool IsToken(byte[] bytes)
    {
        if (bytes.Length < Magic.Length)
            return false;

        return bytes.AsSpan(0, Magic.Length).SequenceEqual(Magic);
    }

    public static byte[] Create()
    {
        // 8-byte magic + 16 random bytes
        var token = new byte[Magic.Length + 16];
        Magic.CopyTo(token);
        RandomNumberGenerator.Fill(token.AsSpan(Magic.Length));
        return token;
    }
}

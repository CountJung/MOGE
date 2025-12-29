namespace SharedUI.Services;

public readonly record struct Rgba32(byte R, byte G, byte B, byte A = 255)
{
    public static Rgba32 FromHexOrDefault(string? hex, Rgba32 @default)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return @default;

        var s = hex.Trim();
        if (s.StartsWith('#'))
            s = s[1..];

        if (s.Length != 6)
            return @default;

        try
        {
            var r = Convert.ToByte(s.Substring(0, 2), 16);
            var g = Convert.ToByte(s.Substring(2, 2), 16);
            var b = Convert.ToByte(s.Substring(4, 2), 16);
            return new Rgba32(r, g, b, 255);
        }
        catch
        {
            return @default;
        }
    }
}

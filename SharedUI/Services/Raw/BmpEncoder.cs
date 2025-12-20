namespace SharedUI.Services.Raw;

internal static class BmpEncoder
{
    // 24bpp BMP (BGR), bottom-up, padded to 4-byte rows
    public static byte[] EncodeBgr24(int width, int height, byte[] rgba)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));

        var expected = checked(width * height * 4);
        if (rgba.Length < expected)
            throw new ArgumentException("RGBA buffer too short.", nameof(rgba));

        var rowStride = width * 3;
        var rowPadded = (rowStride + 3) & ~3;
        var pixelDataSize = checked(rowPadded * height);
        var fileSize = checked(14 + 40 + pixelDataSize);

        var buf = new byte[fileSize];

        // BITMAPFILEHEADER
        buf[0] = (byte)'B';
        buf[1] = (byte)'M';
        WriteInt32LE(buf, 2, fileSize);
        // reserved1+2 = 0
        WriteInt32LE(buf, 10, 14 + 40);

        // BITMAPINFOHEADER
        WriteInt32LE(buf, 14, 40);          // biSize
        WriteInt32LE(buf, 18, width);       // biWidth
        WriteInt32LE(buf, 22, height);      // biHeight (positive => bottom-up)
        WriteInt16LE(buf, 26, 1);           // biPlanes
        WriteInt16LE(buf, 28, 24);          // biBitCount
        WriteInt32LE(buf, 30, 0);           // biCompression (BI_RGB)
        WriteInt32LE(buf, 34, pixelDataSize);
        WriteInt32LE(buf, 38, 2835);        // 72 DPI
        WriteInt32LE(buf, 42, 2835);
        WriteInt32LE(buf, 46, 0);
        WriteInt32LE(buf, 50, 0);

        var dstOffset = 14 + 40;

        // Write bottom-up rows
        for (var y = 0; y < height; y++)
        {
            var srcY = height - 1 - y;
            var srcRow = srcY * width * 4;
            var dstRow = dstOffset + y * rowPadded;

            var di = dstRow;
            for (var x = 0; x < width; x++)
            {
                var si = srcRow + x * 4;
                var r = rgba[si + 0];
                var g = rgba[si + 1];
                var b = rgba[si + 2];

                buf[di + 0] = b;
                buf[di + 1] = g;
                buf[di + 2] = r;
                di += 3;
            }

            // padding bytes are already 0
        }

        return buf;
    }

    private static void WriteInt16LE(byte[] buf, int offset, int value)
    {
        buf[offset + 0] = (byte)(value & 0xFF);
        buf[offset + 1] = (byte)((value >> 8) & 0xFF);
    }

    private static void WriteInt32LE(byte[] buf, int offset, int value)
    {
        buf[offset + 0] = (byte)(value & 0xFF);
        buf[offset + 1] = (byte)((value >> 8) & 0xFF);
        buf[offset + 2] = (byte)((value >> 16) & 0xFF);
        buf[offset + 3] = (byte)((value >> 24) & 0xFF);
    }
}

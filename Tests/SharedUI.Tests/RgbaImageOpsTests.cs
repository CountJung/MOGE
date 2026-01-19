using SharedUI.Services.Raw;
using Xunit;

namespace SharedUI.Tests;

public sealed class RgbaImageOpsTests
{
    [Fact]
    public void Grayscale_PreservesAlphaAndEqualizesChannels()
    {
        var src = new RawRgbaImage(1, 1, new byte[] { 100, 150, 200, 128 });

        var dst = RgbaImageOps.Grayscale(src);

        Assert.Equal(1, dst.Width);
        Assert.Equal(1, dst.Height);
        Assert.Equal(128, dst.RgbaBytes[3]);
        Assert.Equal(dst.RgbaBytes[0], dst.RgbaBytes[1]);
        Assert.Equal(dst.RgbaBytes[1], dst.RgbaBytes[2]);
        Assert.Equal(141, dst.RgbaBytes[0]);
    }

    [Fact]
    public void Invert_OnlyInvertsRgb()
    {
        var src = new RawRgbaImage(1, 1, new byte[] { 0, 127, 255, 64 });

        var dst = RgbaImageOps.Invert(src);

        Assert.Equal(255, dst.RgbaBytes[0]);
        Assert.Equal(128, dst.RgbaBytes[1]);
        Assert.Equal(0, dst.RgbaBytes[2]);
        Assert.Equal(64, dst.RgbaBytes[3]);
    }

    [Fact]
    public void Posterize_QuantizesChannels()
    {
        var src = new RawRgbaImage(1, 1, new byte[] { 10, 200, 128, 255 });

        var dst = RgbaImageOps.Posterize(src, levels: 2);

        Assert.Equal(0, dst.RgbaBytes[0]);
        Assert.Equal(255, dst.RgbaBytes[1]);
        Assert.Equal(255, dst.RgbaBytes[2]);
        Assert.Equal(255, dst.RgbaBytes[3]);
    }

    [Fact]
    public void Pixelize_AveragesBlocks()
    {
        var src = new RawRgbaImage(2, 2, new byte[]
        {
            0, 0, 0, 255,
            100, 0, 0, 255,
            0, 100, 0, 255,
            100, 100, 0, 255
        });

        var dst = RgbaImageOps.Pixelize(src, blockSize: 2);

        // Average of R: (0+100+0+100)/4 = 50
        // Average of G: (0+0+100+100)/4 = 50
        // Average of B: 0
        for (var i = 0; i < dst.RgbaBytes.Length; i += 4)
        {
            Assert.Equal(50, dst.RgbaBytes[i + 0]);
            Assert.Equal(50, dst.RgbaBytes[i + 1]);
            Assert.Equal(0, dst.RgbaBytes[i + 2]);
            Assert.Equal(255, dst.RgbaBytes[i + 3]);
        }
    }
}

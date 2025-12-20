using OpenCvSharp;
using System.Runtime.InteropServices;
using SharedUI.Services.Raw;

namespace SharedUI.Services;

public sealed class ImageProcessorService
{
    private readonly IRawImageProvider? _raw;

    public ImageProcessorService(IRawImageProvider? raw = null)
    {
        _raw = raw;
    }

    public sealed record ProcessingSettings(
        int BlurKernelSize,
        bool Grayscale,
        bool Sepia,
        bool Canny,
        double CannyThreshold1,
        double CannyThreshold2,
        double Contrast,
        double Brightness);

    public byte[] ApplyPipeline(byte[] imageBytes, ProcessingSettings settings)
    {
        if (OperatingSystem.IsBrowser())
            return ApplyPipelineBrowser(imageBytes, settings);

        using var src = Decode(imageBytes);
        using var work = src.Clone();

        if (Math.Abs(settings.Contrast - 1.0) > 0.0001 || Math.Abs(settings.Brightness) > 0.0001)
        {
            using var tmp = new Mat();
            work.ConvertTo(tmp, MatType.CV_8UC3, settings.Contrast, settings.Brightness);
            tmp.CopyTo(work);
        }

        if (settings.BlurKernelSize > 1)
        {
            var k = NormalizeOddKernel(settings.BlurKernelSize);
            using var tmp = new Mat();
            Cv2.GaussianBlur(work, tmp, new Size(k, k), 0);
            tmp.CopyTo(work);
        }

        if (settings.Sepia)
        {
            using var src32 = new Mat();
            work.ConvertTo(src32, MatType.CV_32FC3, 1.0 / 255.0);

            using var sepiaKernel = new Mat(3, 3, MatType.CV_32FC1);
            sepiaKernel.SetArray<float>(
                0.131f, 0.534f, 0.272f,
                0.168f, 0.686f, 0.349f,
                0.189f, 0.769f, 0.393f);

            using var dst32 = new Mat();
            Cv2.Transform(src32, dst32, sepiaKernel);
            Cv2.Min(dst32, Scalar.All(1.0), dst32);

            using var dst8 = new Mat();
            dst32.ConvertTo(dst8, MatType.CV_8UC3, 255.0);
            dst8.CopyTo(work);
        }
        else if (settings.Grayscale)
        {
            using var gray = new Mat();
            Cv2.CvtColor(work, gray, ColorConversionCodes.BGR2GRAY);
            using var dst = new Mat();
            Cv2.CvtColor(gray, dst, ColorConversionCodes.GRAY2BGR);
            dst.CopyTo(work);
        }

        if (settings.Canny)
        {
            using var gray = new Mat();
            Cv2.CvtColor(work, gray, ColorConversionCodes.BGR2GRAY);

            using var edges = new Mat();
            Cv2.Canny(gray, edges, settings.CannyThreshold1, settings.CannyThreshold2);

            using var dst = new Mat();
            Cv2.CvtColor(edges, dst, ColorConversionCodes.GRAY2BGR);
            dst.CopyTo(work);
        }

        return EncodeForDisplay(work);
    }

    public byte[] ToPng(byte[] imageBytes)
    {
        using var src = Decode(imageBytes);
        return EncodeForDisplay(src);
    }

    public string? CreateThumbnailDataUrl(byte[]? imageBytes, int maxSide = 64)
    {
        if (imageBytes is null || imageBytes.Length == 0)
            return null;

        maxSide = Math.Clamp(maxSide, 16, 256);

        // Browser: use cached raw RGBA to avoid relying on codecs/encoders.
        if (OperatingSystem.IsBrowser() && _raw is not null)
        {
            try
            {
                var sig = ImageSignature.Create(imageBytes);
                if (_raw.TryGet(sig, out var raw) && raw.RgbaBytes is { Length: > 0 })
                {
                    var thumb = ScaleToMaxSide(raw, maxSide);
                    var bmp = BmpEncoder.EncodeBgr24(thumb.Width, thumb.Height, thumb.RgbaBytes);
                    return "data:image/bmp;base64," + Convert.ToBase64String(bmp);
                }
            }
            catch
            {
            }

            return null;
        }

        // Native: decode/resize via OpenCV, encode PNG; fallback to BMP.
        try
        {
            using var src = Decode(imageBytes);
            if (src.Empty())
                return null;

            var (tw, th) = FitToMaxSide(src.Width, src.Height, maxSide);
            using var resized = new Mat();
            Cv2.Resize(src, resized, new Size(tw, th), 0, 0, InterpolationFlags.Area);

            try
            {
                Cv2.ImEncode(".png", resized, out var png);
                return "data:image/png;base64," + Convert.ToBase64String(png.ToArray());
            }
            catch
            {
                var raw = ExtractRgba(resized);
                var bmp = BmpEncoder.EncodeBgr24(raw.Width, raw.Height, raw.RgbaBytes);
                return "data:image/bmp;base64," + Convert.ToBase64String(bmp);
            }
        }
        catch
        {
            return null;
        }
    }

    public byte[] ApplyGaussianBlur(byte[] imageBytes, int kernelSize)
    {
        if (OperatingSystem.IsBrowser())
        {
            var raw = GetRawOrThrow(imageBytes);
            var blurred = Raw.RgbaImageOps.GaussianBlur(raw, kernelSize);
            return ReturnToken(blurred);
        }

        kernelSize = NormalizeOddKernel(kernelSize);

        using var src = Decode(imageBytes);
        using var dst = new Mat();
        Cv2.GaussianBlur(src, dst, new Size(kernelSize, kernelSize), 0);
        return EncodeForDisplay(dst);
    }

    public byte[] ApplyCanny(byte[] imageBytes, double threshold1, double threshold2)
    {
        if (OperatingSystem.IsBrowser())
        {
            var raw = GetRawOrThrow(imageBytes);
            var edged = Raw.RgbaImageOps.CannyEdge(raw, threshold1, threshold2);
            return ReturnToken(edged);
        }

        using var src = Decode(imageBytes);
        using var gray = new Mat();
        Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);

        using var edges = new Mat();
        Cv2.Canny(gray, edges, threshold1, threshold2);

        using var dst = new Mat();
        Cv2.CvtColor(edges, dst, ColorConversionCodes.GRAY2BGR);

        return EncodeForDisplay(dst);
    }

    public byte[] ApplyGrayscale(byte[] imageBytes)
    {
        if (OperatingSystem.IsBrowser())
        {
            var raw = GetRawOrThrow(imageBytes);
            var grayRaw = Raw.RgbaImageOps.Grayscale(raw);
            return ReturnToken(grayRaw);
        }

        using var src = Decode(imageBytes);
        using var gray = new Mat();
        Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);

        using var dst = new Mat();
        Cv2.CvtColor(gray, dst, ColorConversionCodes.GRAY2BGR);

        return EncodeForDisplay(dst);
    }

    public byte[] ApplySepia(byte[] imageBytes)
    {
        if (OperatingSystem.IsBrowser())
        {
            var raw = GetRawOrThrow(imageBytes);
            var sepia = Raw.RgbaImageOps.Sepia(raw);
            return ReturnToken(sepia);
        }

        using var src = Decode(imageBytes);
        using var src32 = new Mat();
        src.ConvertTo(src32, MatType.CV_32FC3, 1.0 / 255.0);

        using var sepiaKernel = new Mat(3, 3, MatType.CV_32FC1);
        sepiaKernel.SetArray<float>(
            0.131f, 0.534f, 0.272f,
            0.168f, 0.686f, 0.349f,
            0.189f, 0.769f, 0.393f);

        using var dst32 = new Mat();
        Cv2.Transform(src32, dst32, sepiaKernel);
        Cv2.Min(dst32, Scalar.All(1.0), dst32);

        using var dst8 = new Mat();
        dst32.ConvertTo(dst8, MatType.CV_8UC3, 255.0);

        return EncodeForDisplay(dst8);
    }

    public byte[] AdjustBrightnessContrast(byte[] imageBytes, double contrast, double brightness)
    {
        if (OperatingSystem.IsBrowser())
        {
            var raw = GetRawOrThrow(imageBytes);
            var adjusted = Raw.RgbaImageOps.AdjustBrightnessContrast(raw, contrast, brightness);
            return ReturnToken(adjusted);
        }

        using var src = Decode(imageBytes);
        using var dst = new Mat();
        src.ConvertTo(dst, MatType.CV_8UC3, contrast, brightness);
        return EncodeForDisplay(dst);
    }

    public byte[] WarpPerspective(byte[] imageBytes, Point2f[] srcQuad, Point2f[] dstQuad, int outWidth, int outHeight)
    {
        if (srcQuad.Length != 4 || dstQuad.Length != 4)
            throw new ArgumentException("srcQuad and dstQuad must contain exactly 4 points.");

        if (OperatingSystem.IsBrowser())
        {
            var raw = GetRawOrThrow(imageBytes);
            var warped = Raw.RgbaImageOps.WarpPerspective(raw, srcQuad, dstQuad, outWidth, outHeight);
            return ReturnToken(warped);
        }

        using var src = Decode(imageBytes);
        using var transform = Cv2.GetPerspectiveTransform(srcQuad, dstQuad);

        using var dst = new Mat();
        Cv2.WarpPerspective(src, dst, transform, new Size(outWidth, outHeight));
        return EncodeForDisplay(dst);
    }

    public byte[] Rotate90(byte[] imageBytes, RotateFlags rotateFlags)
    {
        if (OperatingSystem.IsBrowser())
        {
            var raw = GetRawOrThrow(imageBytes);
            var rotated = Raw.RgbaImageOps.Rotate90(raw, rotateFlags);
            return ReturnToken(rotated);
        }

        using var src = Decode(imageBytes);
        using var dst = new Mat();
        Cv2.Rotate(src, dst, rotateFlags);
        return EncodeForDisplay(dst);
    }

    public byte[] ResizeByScale(byte[] imageBytes, double scale)
    {
        if (scale <= 0)
            throw new ArgumentOutOfRangeException(nameof(scale));

        if (OperatingSystem.IsBrowser())
        {
            var raw = GetRawOrThrow(imageBytes);
            var nw = Math.Max(1, (int)Math.Round(raw.Width * scale));
            var nh = Math.Max(1, (int)Math.Round(raw.Height * scale));
            var resized = Raw.RgbaImageOps.Resize(raw, nw, nh);
            return ReturnToken(resized);
        }

        using var src = Decode(imageBytes);
        using var dst = new Mat();
        Cv2.Resize(src, dst, new Size(), scale, scale, InterpolationFlags.Area);
        return EncodeForDisplay(dst);
    }

    public (int width, int height) GetSize(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0)
            return (0, 0);

        if (OperatingSystem.IsBrowser() && _raw is not null)
        {
            try
            {
                var sig = ImageSignature.Create(bytes);
                if (_raw.TryGet(sig, out var raw) && raw.Width > 0 && raw.Height > 0)
                    return (raw.Width, raw.Height);
            }
            catch
            {
            }
        }

        try
        {
            using var mat = Decode(bytes);
            return mat.Empty() ? (0, 0) : (mat.Width, mat.Height);
        }
        catch
        {
            return (0, 0);
        }
    }

    private Mat Decode(byte[] imageBytes)
    {
        if (imageBytes is null)
            throw new ArgumentNullException(nameof(imageBytes));

        if (imageBytes.Length < 8)
            throw new InvalidOperationException($"Failed to decode image bytes (too short: {imageBytes.Length} bytes).");

        // Raw-token path: decode directly from cached RGBA.
        if (RawToken.IsToken(imageBytes) && _raw is not null)
        {
            var tokenSig = ImageSignature.Create(imageBytes);
            if (_raw.TryGet(tokenSig, out var raw) && raw.RgbaBytes is { Length: > 0 })
            {
                return DecodeFromRgba(raw);
            }

            throw new InvalidOperationException("Raw image token not found in cache.");
        }

        // Browser: prefer cached RGBA first to avoid relying on WASM codecs.
        if (OperatingSystem.IsBrowser() && _raw is not null)
        {
            try
            {
                var sig = ImageSignature.Create(imageBytes);
                if (_raw.TryGet(sig, out var raw) && raw.RgbaBytes is { Length: > 0 })
                {
                    return DecodeFromRgba(raw);
                }
            }
            catch
            {
            }
        }

        // 1) Normal decode path (works on native platforms)
        try
        {
            var src = Cv2.ImDecode(imageBytes, ImreadModes.Color);
            if (!src.Empty())
                return src;
        }
        catch
        {
            // Some WASM builds throw native exceptions here; fall through.
        }

        // 2) WASM fallback: if OpenCV codecs can't decode even PNG, use browser-decoded RGBA pixels.
        if (_raw is not null)
        {
            try
            {
                var sig = CreateSignature(imageBytes);
                if (_raw.TryGet(sig, out var raw) && raw.RgbaBytes is { Length: > 0 })
                {
                    return DecodeFromRgba(raw);
                }
            }
            catch
            {
            }
        }

        throw new InvalidOperationException($"Failed to decode image bytes (len={imageBytes.Length}, format={GuessImageFormat(imageBytes)}).");
    }

    private static Mat DecodeFromRgba(RawRgbaImage raw)
    {
        var expected = checked(raw.Width * raw.Height * 4);
        if (raw.RgbaBytes.Length < expected)
            throw new InvalidOperationException($"RGBA buffer too short (len={raw.RgbaBytes.Length}, expected={expected}).");

        using var rgba = new Mat(raw.Height, raw.Width, MatType.CV_8UC4);
        Marshal.Copy(raw.RgbaBytes, 0, rgba.Data, expected);

        var bgr = new Mat();
        Cv2.CvtColor(rgba, bgr, ColorConversionCodes.RGBA2BGR);
        return bgr;
    }

    private static string CreateSignature(byte[] bytes) => ImageSignature.Create(bytes);

    private static string GuessImageFormat(byte[] bytes)
    {
        // Best-effort magic number checks for debugging.
        if (bytes.Length >= 12)
        {
            // PNG
            if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
                return "png";

            // JPEG
            if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
                return "jpeg";

            // GIF
            if (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46)
                return "gif";

            // BMP
            if (bytes[0] == 0x42 && bytes[1] == 0x4D)
                return "bmp";

            // WEBP: RIFF....WEBP
            if (bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 &&
                bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
                return "webp";

            // ISO BMFF (AVIF/HEIC): ....ftyp....
            if (bytes[4] == 0x66 && bytes[5] == 0x74 && bytes[6] == 0x79 && bytes[7] == 0x70)
                return "isobmff(avif/heic/...)";
        }

        return "unknown";
    }

    private byte[] EncodeForDisplay(Mat bgr)
    {
        // Browser: avoid Cv2.ImEncode entirely (it may throw a native exception that bypasses managed catch).
        if (OperatingSystem.IsBrowser())
        {
            if (_raw is IRawImageCache browserCache)
            {
                var token = RawToken.Create();
                browserCache.Set(ImageSignature.Create(token), ExtractRgba(bgr));
                return token;
            }

            throw new InvalidOperationException("Raw image cache not available on browser runtime.");
        }

        // 1) Try PNG
        try
        {
            Cv2.ImEncode(".png", bgr, out var buf);
            var png = buf.ToArray();
            CacheRawIfPossible(png, bgr);
            return png;
        }
        catch (Exception ex) when (IsMissingEncoder(ex))
        {
            // fall through
        }

        // 2) Try BMP
        try
        {
            Cv2.ImEncode(".bmp", bgr, out var bmp);
            var bytes = bmp.ToArray();
            CacheRawIfPossible(bytes, bgr);
            return bytes;
        }
        catch (Exception ex) when (IsMissingEncoder(ex))
        {
            // fall through
        }

        // 3) No encoder available: return a raw token and rely on RGBA rendering + Decode fallback.
        if (_raw is IRawImageCache cache)
        {
            var token = RawToken.Create();
            var raw = ExtractRgba(bgr);
            cache.Set(ImageSignature.Create(token), raw);
            return token;
        }

        throw new InvalidOperationException("OpenCV image encoder is unavailable in this runtime.");
    }

    private static bool IsMissingEncoder(Exception ex)
        => ex.Message?.Contains("could not find encoder", StringComparison.OrdinalIgnoreCase) == true;

    private static RawRgbaImage ExtractRgba(Mat bgr)
    {
        using var rgba = new Mat();
        Cv2.CvtColor(bgr, rgba, ColorConversionCodes.BGR2RGBA);

        var expected = checked(rgba.Width * rgba.Height * 4);
        var managed = new byte[expected];
        Marshal.Copy(rgba.Data, managed, 0, expected);
        return new RawRgbaImage(rgba.Width, rgba.Height, managed);
    }

    private static RawRgbaImage ScaleToMaxSide(in RawRgbaImage raw, int maxSide)
    {
        if (raw.Width <= 0 || raw.Height <= 0)
            return raw;

        var (tw, th) = FitToMaxSide(raw.Width, raw.Height, maxSide);
        if (tw == raw.Width && th == raw.Height)
            return raw;

        return Raw.RgbaImageOps.Resize(raw, tw, th);
    }

    private static (int w, int h) FitToMaxSide(int width, int height, int maxSide)
    {
        if (width <= 0 || height <= 0)
            return (1, 1);

        var max = Math.Max(width, height);
        if (max <= maxSide)
            return (width, height);

        var scale = (double)maxSide / max;
        var w = Math.Max(1, (int)Math.Round(width * scale));
        var h = Math.Max(1, (int)Math.Round(height * scale));
        return (w, h);
    }

    private void CacheRawIfPossible(byte[] encodedBytes, Mat bgr)
    {
        if (_raw is not IRawImageCache cache)
            return;

        try
        {
            var sig = ImageSignature.Create(encodedBytes);
            cache.Set(sig, ExtractRgba(bgr));
        }
        catch
        {
        }
    }

    private static int NormalizeOddKernel(int kernelSize)
    {
        if (kernelSize < 1)
            kernelSize = 1;
        if (kernelSize % 2 == 0)
            kernelSize += 1;
        return kernelSize;
    }

    private byte[] ApplyPipelineBrowser(byte[] imageBytes, ProcessingSettings settings)
    {
        var work = GetRawOrThrow(imageBytes);

        if (Math.Abs(settings.Contrast - 1.0) > 0.0001 || Math.Abs(settings.Brightness) > 0.0001)
            work = Raw.RgbaImageOps.AdjustBrightnessContrast(work, settings.Contrast, settings.Brightness);

        if (settings.BlurKernelSize > 1)
            work = Raw.RgbaImageOps.GaussianBlur(work, settings.BlurKernelSize);

        if (settings.Sepia)
            work = Raw.RgbaImageOps.Sepia(work);
        else if (settings.Grayscale)
            work = Raw.RgbaImageOps.Grayscale(work);

        if (settings.Canny)
            work = Raw.RgbaImageOps.CannyEdge(work, settings.CannyThreshold1, settings.CannyThreshold2);

        return ReturnToken(work);
    }

    private RawRgbaImage GetRawOrThrow(byte[] imageBytes)
    {
        if (_raw is null)
            throw new InvalidOperationException("Raw image cache not available on browser runtime.");

        var sig = ImageSignature.Create(imageBytes);
        if (_raw.TryGet(sig, out var raw) && raw.RgbaBytes is { Length: > 0 })
            return raw;

        throw new InvalidOperationException("Raw RGBA buffer not found in cache for this image.");
    }

    private byte[] ReturnToken(in RawRgbaImage raw)
    {
        if (_raw is not IRawImageCache cache)
            throw new InvalidOperationException("Raw image cache not writable on this runtime.");

        var token = RawToken.Create();
        cache.Set(ImageSignature.Create(token), raw);
        return token;
    }
}

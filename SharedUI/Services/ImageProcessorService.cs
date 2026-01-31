using OpenCvSharp;
using System.Runtime.InteropServices;
using SharedUI.Components;
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
        bool Invert,
        double Saturation,
        bool Sketch,
        bool Cartoon,
        bool Emboss,
        double SharpenAmount,
        double GlowStrength,
        ColorMapStyle ColorMap,
        double ColorMapStrength,
        int PosterizeLevels,
        int PixelizeBlockSize,
        double VignetteStrength,
        double NoiseAmount,
        bool Canny,
        double CannyThreshold1,
        double CannyThreshold2,
        double Contrast,
        double Brightness);

    public (byte[] Bytes, string ContentType) CreateBlankWhite(int width, int height)
    {
        width = Math.Clamp(width, 1, 8192);
        height = Math.Clamp(height, 1, 8192);

        if (OperatingSystem.IsBrowser())
        {
            if (_raw is not IRawImageCache cache)
                throw new InvalidOperationException("Raw image cache not available on browser runtime.");

            var token = RawToken.Create();
            var rgba = new byte[checked(width * height * 4)];
            // white RGBA
            for (var i = 0; i < rgba.Length; i += 4)
            {
                rgba[i + 0] = 255;
                rgba[i + 1] = 255;
                rgba[i + 2] = 255;
                rgba[i + 3] = 255;
            }

            cache.Set(ImageSignature.Create(token), new RawRgbaImage(width, height, rgba));
            return (token, "moge/raw");
        }

        using var mat = new Mat(height, width, MatType.CV_8UC3, new Scalar(255, 255, 255));
        Cv2.ImEncode(".png", mat, out var buf);
        return (buf.ToArray(), "image/png");
    }

    public byte[] ApplyPipeline(byte[] imageBytes, ProcessingSettings settings)
    {
        if (OperatingSystem.IsBrowser())
            return ApplyPipelineBrowser(imageBytes, settings);

        using var src = Decode(imageBytes);
        using var split = SplitBgrAndAlpha(src);
        using var work = split.Bgr.Clone();

        if (Math.Abs(settings.Contrast - 1.0) > 0.0001 || Math.Abs(settings.Brightness) > 0.0001)
        {
            using var tmp = new Mat();
            work.ConvertTo(tmp, MatType.CV_8UC3, settings.Contrast, settings.Brightness);
            tmp.CopyTo(work);
        }

        if (Math.Abs(settings.Saturation - 1.0) > 0.0001)
        {
            var sat = Math.Clamp(settings.Saturation, 0.0, 3.0);
            using var hsv = new Mat();
            Cv2.CvtColor(work, hsv, ColorConversionCodes.BGR2HSV);
            Cv2.Split(hsv, out var ch);
            using (ch[0])
            using (ch[1])
            using (ch[2])
            {
                using var s32 = new Mat();
                ch[1].ConvertTo(s32, MatType.CV_32FC1);
                Cv2.Multiply(s32, sat, s32);
                Cv2.Min(s32, Scalar.All(255), s32);
                s32.ConvertTo(ch[1], MatType.CV_8UC1);

                using var merged = new Mat();
                Cv2.Merge(ch, merged);
                using var dst = new Mat();
                Cv2.CvtColor(merged, dst, ColorConversionCodes.HSV2BGR);
                dst.CopyTo(work);
            }
        }

        if (settings.BlurKernelSize > 1)
        {
            var k = NormalizeOddKernel(settings.BlurKernelSize);
            using var tmp = new Mat();
            Cv2.GaussianBlur(work, tmp, new Size(k, k), 0);
            tmp.CopyTo(work);
        }

        if (settings.Invert)
        {
            using var tmp = new Mat();
            Cv2.BitwiseNot(work, tmp);
            tmp.CopyTo(work);
        }

        if (settings.Sketch)
        {
            // Pencil sketch: gray -> invert -> blur -> color dodge
            using var gray = new Mat();
            Cv2.CvtColor(work, gray, ColorConversionCodes.BGR2GRAY);

            using var inv = new Mat();
            Cv2.BitwiseNot(gray, inv);

            var k = NormalizeOddKernel(Math.Max(11, settings.BlurKernelSize > 1 ? settings.BlurKernelSize * 2 + 1 : 21));
            using var blur = new Mat();
            Cv2.GaussianBlur(inv, blur, new Size(k, k), 0);

            using var denom = new Mat();
            Cv2.Subtract(new Scalar(255), blur, denom);

            using var dodge = new Mat();
            Cv2.Divide(gray, denom, dodge, scale: 256.0);

            using var dst = new Mat();
            Cv2.CvtColor(dodge, dst, ColorConversionCodes.GRAY2BGR);
            dst.CopyTo(work);
        }
        else if (settings.Cartoon)
        {
            // Simplified cartoon: bilateral smoothing + edge mask + combine
            using var smooth = work.Clone();
            for (var i = 0; i < 3; i++)
            {
                using var tmp = new Mat();
                Cv2.BilateralFilter(smooth, tmp, d: 9, sigmaColor: 75, sigmaSpace: 75);
                tmp.CopyTo(smooth);
            }

            using var gray = new Mat();
            Cv2.CvtColor(smooth, gray, ColorConversionCodes.BGR2GRAY);
            using var blur = new Mat();
            Cv2.MedianBlur(gray, blur, 7);

            using var edges = new Mat();
            Cv2.AdaptiveThreshold(blur, edges, 255,
                AdaptiveThresholdTypes.MeanC, ThresholdTypes.Binary, 9, 2);

            using var edgesBgr = new Mat();
            Cv2.CvtColor(edges, edgesBgr, ColorConversionCodes.GRAY2BGR);
            using var dst = new Mat();
            Cv2.BitwiseAnd(smooth, edgesBgr, dst);
            dst.CopyTo(work);
        }
        else if (settings.Sepia)
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

        if (settings.Emboss)
        {
            using var kernel = new Mat(3, 3, MatType.CV_32FC1);
            kernel.SetArray<float>(
                -2, -1, 0,
                -1, 1, 1,
                0, 1, 2);

            using var tmp = new Mat();
            Cv2.Filter2D(work, tmp, work.Type(), kernel);
            Cv2.Add(tmp, new Scalar(128, 128, 128), tmp);
            tmp.CopyTo(work);
        }

        if (settings.SharpenAmount > 0.0001)
        {
            var a = Math.Clamp(settings.SharpenAmount, 0.0, 3.0);
            using var kernel = new Mat(3, 3, MatType.CV_32FC1);
            var c = (float)(1 + 4 * a);
            var n = (float)(-a);
            kernel.SetArray<float>(
                0, n, 0,
                n, c, n,
                0, n, 0);

            using var tmp = new Mat();
            Cv2.Filter2D(work, tmp, work.Type(), kernel);
            tmp.CopyTo(work);
        }

        if (settings.GlowStrength > 0.0001)
        {
            var strength = Math.Clamp(settings.GlowStrength, 0.0, 1.0);
            var k = NormalizeOddKernel(settings.BlurKernelSize > 1 ? settings.BlurKernelSize * 2 + 1 : 21);
            using var blur = new Mat();
            Cv2.GaussianBlur(work, blur, new Size(k, k), 0);
            using var tmp = new Mat();
            Cv2.AddWeighted(work, 1.0, blur, strength, 0.0, tmp);
            tmp.CopyTo(work);
        }

        if (settings.PosterizeLevels >= 2)
        {
            var levels = Math.Clamp(settings.PosterizeLevels, 2, 32);
            var step = 255.0 / (levels - 1);
            var lut = new Mat(1, 256, MatType.CV_8UC1);
            for (var i = 0; i < 256; i++)
            {
                var q = (int)Math.Round(i / step) * step;
                lut.Set(0, i, (byte)Math.Clamp(q, 0, 255));
            }

            Cv2.LUT(work, lut, work);
        }

        if (settings.ColorMap != ColorMapStyle.None && settings.ColorMapStrength > 0.0001)
        {
            using var gray = new Mat();
            Cv2.CvtColor(work, gray, ColorConversionCodes.BGR2GRAY);

            var cm = settings.ColorMap switch
            {
                ColorMapStyle.Autumn => ColormapTypes.Autumn,
                ColorMapStyle.Bone => ColormapTypes.Bone,
                ColorMapStyle.Ocean => ColormapTypes.Ocean,
                ColorMapStyle.Summer => ColormapTypes.Summer,
                ColorMapStyle.Hot => ColormapTypes.Hot,
                ColorMapStyle.Winter => ColormapTypes.Winter,
                ColorMapStyle.Jet => ColormapTypes.Jet,
                ColorMapStyle.Rainbow => ColormapTypes.Rainbow,
                ColorMapStyle.Pink => ColormapTypes.Pink,
                _ => ColormapTypes.Jet
            };

            using var dst = new Mat();
            Cv2.ApplyColorMap(gray, dst, cm);

            // Blend with original based on ColorMapStrength (0.0 = original, 1.0 = full color map)
            var strength = Math.Clamp(settings.ColorMapStrength, 0.0, 1.0);
            Cv2.AddWeighted(work, 1.0 - strength, dst, strength, 0.0, work);
        }

        if (settings.VignetteStrength > 0.0001)
        {
            var strength = Math.Clamp(settings.VignetteStrength, 0.0, 1.0);
            var rows = work.Rows;
            var cols = work.Cols;

            using var kernelX = Cv2.GetGaussianKernel(cols, cols / 2.0)!;
            using var kernelY = Cv2.GetGaussianKernel(rows, rows / 2.0)!;
            using var mask = (kernelY * kernelX.T()).ToMat();

            Cv2.Normalize(mask, mask, 0.0, 1.0, NormTypes.MinMax);
            // Blend mask toward 1.0 based on strength
            using var ones = new Mat(mask.Size(), mask.Type(), Scalar.All(1.0));
            using var mask2 = new Mat();
            Cv2.AddWeighted(mask, strength, ones, 1.0 - strength, 0.0, mask2);

            using var work32 = new Mat();
            work.ConvertTo(work32, MatType.CV_32FC3, 1.0 / 255.0);
            using var mask3 = new Mat();
            Cv2.Merge(new[] { mask2, mask2, mask2 }, mask3);
            Cv2.Multiply(work32, mask3, work32);
            work32.ConvertTo(work, MatType.CV_8UC3, 255.0);
        }

        if (settings.PixelizeBlockSize >= 2)
        {
            var b = Math.Clamp(settings.PixelizeBlockSize, 2, 128);
            var smallW = Math.Max(1, work.Cols / b);
            var smallH = Math.Max(1, work.Rows / b);
            using var small = new Mat();
            Cv2.Resize(work, small, new Size(smallW, smallH), 0, 0, InterpolationFlags.Linear);
            using var tmp = new Mat();
            Cv2.Resize(small, tmp, new Size(work.Cols, work.Rows), 0, 0, InterpolationFlags.Nearest);
            tmp.CopyTo(work);
        }

        if (settings.NoiseAmount > 0.0001)
        {
            var amount = Math.Clamp(settings.NoiseAmount, 0.0, 1.0);
            var sigma = amount * 50.0;
            using var noise = new Mat(work.Size(), MatType.CV_16SC3);
            Cv2.Randn(noise, Scalar.All(0), Scalar.All(sigma));
            using var work16 = new Mat();
            work.ConvertTo(work16, MatType.CV_16SC3);
            using var tmp = new Mat();
            Cv2.Add(work16, noise, tmp);
            tmp.ConvertTo(work, MatType.CV_8UC3);
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

        if (split.Alpha is not null)
        {
            using var merged = MergeBgrAndAlpha(work, split.Alpha);
            return EncodeForDisplay(merged);
        }

        return EncodeForDisplay(work);
    }

    public byte[] ApplyPipelinePreview(byte[] imageBytes, ProcessingSettings settings, int maxSide = 1200)
    {
        if (imageBytes is null || imageBytes.Length == 0)
            return imageBytes;

        maxSide = Math.Clamp(maxSide, 256, 2048);

        var (w, h) = GetSize(imageBytes);
        if (w <= 0 || h <= 0)
            return ApplyPipeline(imageBytes, settings);

        var max = Math.Max(w, h);
        if (max <= maxSide)
            return ApplyPipeline(imageBytes, settings);

        var scale = (double)maxSide / max;
        var resized = ResizeByScale(imageBytes, scale);
        return ApplyPipeline(resized, settings);
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
        using var split = SplitBgrAndAlpha(src);

        using var dst = new Mat();
        Cv2.GaussianBlur(split.Bgr, dst, new Size(kernelSize, kernelSize), 0);

        if (split.Alpha is not null)
        {
            using var merged = MergeBgrAndAlpha(dst, split.Alpha);
            return EncodeForDisplay(merged);
        }

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
        using var split = SplitBgrAndAlpha(src);

        using var gray = new Mat();
        Cv2.CvtColor(split.Bgr, gray, ColorConversionCodes.BGR2GRAY);

        using var edges = new Mat();
        Cv2.Canny(gray, edges, threshold1, threshold2);

        using var dst = new Mat();
        Cv2.CvtColor(edges, dst, ColorConversionCodes.GRAY2BGR);

        if (split.Alpha is not null)
        {
            using var merged = MergeBgrAndAlpha(dst, split.Alpha);
            return EncodeForDisplay(merged);
        }

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
        using var split = SplitBgrAndAlpha(src);

        using var gray = new Mat();
        Cv2.CvtColor(split.Bgr, gray, ColorConversionCodes.BGR2GRAY);

        using var dst = new Mat();
        Cv2.CvtColor(gray, dst, ColorConversionCodes.GRAY2BGR);

        if (split.Alpha is not null)
        {
            using var merged = MergeBgrAndAlpha(dst, split.Alpha);
            return EncodeForDisplay(merged);
        }

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
        using var split = SplitBgrAndAlpha(src);

        using var src32 = new Mat();
        split.Bgr.ConvertTo(src32, MatType.CV_32FC3, 1.0 / 255.0);

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

        if (split.Alpha is not null)
        {
            using var merged = MergeBgrAndAlpha(dst8, split.Alpha);
            return EncodeForDisplay(merged);
        }

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
        using var split = SplitBgrAndAlpha(src);

        using var dst = new Mat();
        split.Bgr.ConvertTo(dst, MatType.CV_8UC3, contrast, brightness);

        if (split.Alpha is not null)
        {
            using var merged = MergeBgrAndAlpha(dst, split.Alpha);
            return EncodeForDisplay(merged);
        }

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
        using var split = SplitBgrAndAlpha(src);
        using var transform = Cv2.GetPerspectiveTransform(srcQuad, dstQuad);

        using var dst = new Mat();
        Cv2.WarpPerspective(split.Bgr, dst, transform, new Size(outWidth, outHeight));

        if (split.Alpha is not null)
        {
            using var alpha = new Mat();
            Cv2.WarpPerspective(split.Alpha, alpha, transform, new Size(outWidth, outHeight), InterpolationFlags.Linear, BorderTypes.Constant, Scalar.All(0));
            using var merged = MergeBgrAndAlpha(dst, alpha);
            return EncodeForDisplay(merged);
        }

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
        using var split = SplitBgrAndAlpha(src);

        using var dst = new Mat();
        Cv2.Rotate(split.Bgr, dst, rotateFlags);

        if (split.Alpha is not null)
        {
            using var alpha = new Mat();
            Cv2.Rotate(split.Alpha, alpha, rotateFlags);
            using var merged = MergeBgrAndAlpha(dst, alpha);
            return EncodeForDisplay(merged);
        }

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
        using var split = SplitBgrAndAlpha(src);

        using var dst = new Mat();
        Cv2.Resize(split.Bgr, dst, new Size(), scale, scale, InterpolationFlags.Area);

        if (split.Alpha is not null)
        {
            using var alpha = new Mat();
            Cv2.Resize(split.Alpha, alpha, new Size(), scale, scale, InterpolationFlags.Area);
            using var merged = MergeBgrAndAlpha(dst, alpha);
            return EncodeForDisplay(merged);
        }

        return EncodeForDisplay(dst);
    }

    public byte[] BlurRegion(byte[] imageBytes, int x, int y, int width, int height, int kernelSize)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));

        kernelSize = NormalizeOddKernel(kernelSize);
        if (kernelSize <= 1)
            return imageBytes;

        if (OperatingSystem.IsBrowser())
        {
            var raw = GetRawOrThrow(imageBytes);

            x = Math.Clamp(x, 0, raw.Width - 1);
            y = Math.Clamp(y, 0, raw.Height - 1);
            width = Math.Clamp(width, 1, raw.Width - x);
            height = Math.Clamp(height, 1, raw.Height - y);

            var originalRegion = Raw.RgbaImageOps.Crop(raw, x, y, width, height);
            var blurred = Raw.RgbaImageOps.GaussianBlur(originalRegion, kernelSize);

            // Preserve alpha channel to avoid unexpected transparency changes.
            var fixedBytes = blurred.RgbaBytes.ToArray();
            for (var i = 3; i < fixedBytes.Length && i < originalRegion.RgbaBytes.Length; i += 4)
                fixedBytes[i] = originalRegion.RgbaBytes[i];

            var blurredFixed = new RawRgbaImage(blurred.Width, blurred.Height, fixedBytes);
            var merged = Raw.RgbaImageOps.Blit(raw, blurredFixed, x, y);
            return ReturnToken(merged);
        }

        using var src = Decode(imageBytes);
        using var split = SplitBgrAndAlpha(src);
        using var work = split.Bgr.Clone();

        x = Math.Clamp(x, 0, work.Width - 1);
        y = Math.Clamp(y, 0, work.Height - 1);
        width = Math.Clamp(width, 1, work.Width - x);
        height = Math.Clamp(height, 1, work.Height - y);

        var rect = new Rect(x, y, width, height);
        using (var roi = new Mat(work, rect))
        using (var dst = new Mat())
        {
            Cv2.GaussianBlur(roi, dst, new Size(kernelSize, kernelSize), 0);
            dst.CopyTo(roi);
        }

        if (split.Alpha is not null)
        {
            using var merged = MergeBgrAndAlpha(work, split.Alpha);
            return EncodeForDisplay(merged);
        }

        return EncodeForDisplay(work);
    }

    public byte[] SharpenRegion(byte[] imageBytes, int x, int y, int width, int height, double amount)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));

        amount = Math.Clamp(amount, 0, 3);
        if (amount <= 0.0001)
            return imageBytes;

        if (OperatingSystem.IsBrowser())
        {
            var raw = GetRawOrThrow(imageBytes);

            x = Math.Clamp(x, 0, raw.Width - 1);
            y = Math.Clamp(y, 0, raw.Height - 1);
            width = Math.Clamp(width, 1, raw.Width - x);
            height = Math.Clamp(height, 1, raw.Height - y);

            var region = Raw.RgbaImageOps.Crop(raw, x, y, width, height);
            var sharpened = Raw.RgbaImageOps.Sharpen(region, amount);
            var merged = Raw.RgbaImageOps.Blit(raw, sharpened, x, y);
            return ReturnToken(merged);
        }

        using var src = Decode(imageBytes);
        using var split = SplitBgrAndAlpha(src);
        using var work = split.Bgr.Clone();

        x = Math.Clamp(x, 0, work.Width - 1);
        y = Math.Clamp(y, 0, work.Height - 1);
        width = Math.Clamp(width, 1, work.Width - x);
        height = Math.Clamp(height, 1, work.Height - y);

        var rect = new Rect(x, y, width, height);

        using (var roi = new Mat(work, rect))
        using (var dst = new Mat())
        using (var kernel = new Mat(3, 3, MatType.CV_32FC1))
        {
            // Adjustable sharpen kernel (unsharp-ish):
            // [ 0, -a, 0
            //  -a, 1+4a, -a
            //   0, -a, 0 ]
            var a = (float)amount;
            kernel.SetArray(new float[]
            {
                0f, -a, 0f,
                -a, 1f + 4f * a, -a,
                0f, -a, 0f
            });

            Cv2.Filter2D(roi, dst, roi.Type(), kernel);
            dst.CopyTo(roi);
        }

        if (split.Alpha is not null)
        {
            using var merged = MergeBgrAndAlpha(work, split.Alpha);
            return EncodeForDisplay(merged);
        }

        return EncodeForDisplay(work);
    }

    public byte[] Crop(byte[] imageBytes, int x, int y, int width, int height)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));

        if (OperatingSystem.IsBrowser())
        {
            var raw = GetRawOrThrow(imageBytes);
            var cropped = Raw.RgbaImageOps.Crop(raw, x, y, width, height);
            return ReturnToken(cropped);
        }

        using var src = Decode(imageBytes);

        x = Math.Clamp(x, 0, src.Width - 1);
        y = Math.Clamp(y, 0, src.Height - 1);
        width = Math.Clamp(width, 1, src.Width - x);
        height = Math.Clamp(height, 1, src.Height - y);

        var rect = new Rect(x, y, width, height);
        using var roi = new Mat(src, rect);
        using var croppedMat = roi.Clone();
        return EncodeForDisplay(croppedMat);
    }

    public byte[] ApplyStroke(byte[] imageBytes, CanvasInteractionMode mode, IReadOnlyList<CanvasPoint> points, int radius, Rgba32? color = null)
    {
        if (points is null)
            throw new ArgumentNullException(nameof(points));

        radius = Math.Clamp(radius, 1, 256);
        if (points.Count < 2)
            return imageBytes;

        if (OperatingSystem.IsBrowser())
        {
            var raw = GetRawOrThrow(imageBytes);
            var next = ApplyStrokeToRgba(raw, mode, points, radius, color);
            return ReturnToken(next);
        }

        using var src = Decode(imageBytes);
        using var split = SplitBgrAndAlpha(src);

        using var work = split.Bgr.Clone();
        using var alpha = split.Alpha?.Clone() ?? new Mat(work.Rows, work.Cols, MatType.CV_8UC1, Scalar.All(255));

        var thickness = Math.Max(1, radius * 2);

        var isEraser = mode == CanvasInteractionMode.Eraser;
        var hasColor = color is not null;

        // Default behavior (back-compat): brush paints white, eraser makes transparent.
        var c = color ?? new Rgba32(255, 255, 255, 255);
        var bgr = new Scalar(c.B, c.G, c.R);
        var alphaBrush = new Scalar(255);
        var alphaErase = new Scalar(0);

        for (var i = 1; i < points.Count; i++)
        {
            var a = points[i - 1];
            var b = points[i];

            var p1 = new Point((int)Math.Round(a.X), (int)Math.Round(a.Y));
            var p2 = new Point((int)Math.Round(b.X), (int)Math.Round(b.Y));

            if (isEraser)
            {
                if (!hasColor)
                {
                    // Default eraser: make transparent.
                    Cv2.Line(alpha, p1, p2, alphaErase, thickness, LineTypes.AntiAlias);
                    // Also clear color to avoid fringes when composited elsewhere.
                    Cv2.Line(work, p1, p2, Scalar.All(0), thickness, LineTypes.AntiAlias);
                }
                else
                {
                    // GIMP-like BG erase: paint with provided color (opaque).
                    Cv2.Line(work, p1, p2, bgr, thickness, LineTypes.AntiAlias);
                    Cv2.Line(alpha, p1, p2, alphaBrush, thickness, LineTypes.AntiAlias);
                }
            }
            else
            {
                // Paint opaque with color.
                Cv2.Line(work, p1, p2, bgr, thickness, LineTypes.AntiAlias);
                Cv2.Line(alpha, p1, p2, alphaBrush, thickness, LineTypes.AntiAlias);
            }
        }

        using var merged = MergeBgrAndAlpha(work, alpha);
        return EncodeForDisplay(merged);
    }

    private static RawRgbaImage ApplyStrokeToRgba(RawRgbaImage src, CanvasInteractionMode mode, IReadOnlyList<CanvasPoint> points, int radius, Rgba32? color)
    {
        if (src.Width <= 0 || src.Height <= 0)
            return src;

        if (src.RgbaBytes is null || src.RgbaBytes.Length < src.Width * src.Height * 4)
            return src;

        var dst = src.RgbaBytes.ToArray();
        var isEraser = mode == CanvasInteractionMode.Eraser;
        var hasColor = color is not null;
        var c = color ?? new Rgba32(255, 255, 255, 255);

        for (var i = 1; i < points.Count; i++)
        {
            var a = points[i - 1];
            var b = points[i];
            DrawSegment(dst, src.Width, src.Height, a, b, radius, isEraser, hasColor, c);
        }

        return new RawRgbaImage(src.Width, src.Height, dst);
    }

    private static void DrawSegment(byte[] rgba, int width, int height, CanvasPoint a, CanvasPoint b, int radius, bool erase, bool hasColor, Rgba32 color)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;

        var steps = (int)Math.Ceiling(Math.Max(Math.Abs(dx), Math.Abs(dy)));
        steps = Math.Max(1, steps);

        for (var s = 0; s <= steps; s++)
        {
            var t = s / (double)steps;
            var x = (int)Math.Round(a.X + dx * t);
            var y = (int)Math.Round(a.Y + dy * t);
            DrawFilledCircle(rgba, width, height, x, y, radius, erase, hasColor, color);
        }
    }

    private static void DrawFilledCircle(byte[] rgba, int width, int height, int cx, int cy, int radius, bool erase, bool hasColor, Rgba32 color)
    {
        if (radius <= 0)
            return;

        var r2 = radius * radius;
        var y0 = Math.Max(0, cy - radius);
        var y1 = Math.Min(height - 1, cy + radius);

        for (var y = y0; y <= y1; y++)
        {
            var dy = y - cy;
            var dxMax = (int)Math.Floor(Math.Sqrt(r2 - dy * dy));

            var x0 = Math.Max(0, cx - dxMax);
            var x1 = Math.Min(width - 1, cx + dxMax);

            var row = y * width;
            for (var x = x0; x <= x1; x++)
            {
                var idx = (row + x) * 4;
                if (erase)
                {
                    if (!hasColor)
                    {
                        // Default eraser: make pixels transparent.
                        rgba[idx + 0] = 0;
                        rgba[idx + 1] = 0;
                        rgba[idx + 2] = 0;
                        rgba[idx + 3] = 0;
                    }
                    else
                    {
                        // GIMP-like BG erase: paint with provided color.
                        rgba[idx + 0] = color.R;
                        rgba[idx + 1] = color.G;
                        rgba[idx + 2] = color.B;
                        rgba[idx + 3] = 255;
                    }
                }
                else
                {
                    rgba[idx + 0] = color.R;
                    rgba[idx + 1] = color.G;
                    rgba[idx + 2] = color.B;
                    rgba[idx + 3] = 255;
                }
            }
        }
    }

    public byte[] CreateSimilarColorMask(byte[] imageBytes, int x, int y, int width, int height, Rgba32 target, int tolerance)
    {
        tolerance = Math.Clamp(tolerance, 0, 255);

        var (iw, ih) = GetSize(imageBytes);
        if (iw <= 0 || ih <= 0)
            throw new InvalidOperationException("Image size is unknown.");

        x = Math.Clamp(x, 0, iw - 1);
        y = Math.Clamp(y, 0, ih - 1);
        width = Math.Clamp(width, 1, iw - x);
        height = Math.Clamp(height, 1, ih - y);

        if (OperatingSystem.IsBrowser())
        {
            var raw = GetRawOrThrow(imageBytes);
            var mask = new byte[iw * ih];

            var r0 = Math.Max(0, target.R - tolerance);
            var r1 = Math.Min(255, target.R + tolerance);
            var g0 = Math.Max(0, target.G - tolerance);
            var g1 = Math.Min(255, target.G + tolerance);
            var b0 = Math.Max(0, target.B - tolerance);
            var b1 = Math.Min(255, target.B + tolerance);

            for (var yy = y; yy < y + height; yy++)
            {
                var rowBase = yy * iw;
                var pxBase = rowBase * 4;
                for (var xx = x; xx < x + width; xx++)
                {
                    var p = pxBase + (xx * 4);
                    var rr = raw.RgbaBytes[p + 0];
                    var gg = raw.RgbaBytes[p + 1];
                    var bb = raw.RgbaBytes[p + 2];

                    if (rr >= r0 && rr <= r1 && gg >= g0 && gg <= g1 && bb >= b0 && bb <= b1)
                        mask[rowBase + xx] = 255;
                }
            }

            return mask;
        }

        using var src = Decode(imageBytes);
        using var bgra = src.Channels() == 4 ? src.Clone() : new Mat();
        if (src.Channels() != 4)
            Cv2.CvtColor(src, bgra, ColorConversionCodes.BGR2BGRA);

        // Convert ROI to BGR and apply InRange.
        var rect = new Rect(x, y, width, height);
        using var roi = new Mat(bgra, rect);
        using var bgr = new Mat();
        Cv2.CvtColor(roi, bgr, ColorConversionCodes.BGRA2BGR);

        var lower = new Scalar(
            Math.Max(0, target.B - tolerance),
            Math.Max(0, target.G - tolerance),
            Math.Max(0, target.R - tolerance));
        var upper = new Scalar(
            Math.Min(255, target.B + tolerance),
            Math.Min(255, target.G + tolerance),
            Math.Min(255, target.R + tolerance));

        using var roiMask = new Mat();
        Cv2.InRange(bgr, lower, upper, roiMask);

        var full = new byte[iw * ih];
        for (var yy = 0; yy < height; yy++)
        {
            var srcRow = roiMask.Ptr(yy);
            var dstOffset = (y + yy) * iw + x;
            Marshal.Copy(srcRow, full, dstOffset, width);
        }

        return full;
    }

    public byte[] CreateConnectedSimilarColorMask(byte[] imageBytes, int startX, int startY, Rgba32 target, int tolerance)
    {
        tolerance = Math.Clamp(tolerance, 0, 255);

        var (iw, ih) = GetSize(imageBytes);
        if (iw <= 0 || ih <= 0)
            throw new InvalidOperationException("Image size is unknown.");

        startX = Math.Clamp(startX, 0, iw - 1);
        startY = Math.Clamp(startY, 0, ih - 1);

        var mask = new byte[iw * ih];

        var r0 = Math.Max(0, target.R - tolerance);
        var r1 = Math.Min(255, target.R + tolerance);
        var g0 = Math.Max(0, target.G - tolerance);
        var g1 = Math.Min(255, target.G + tolerance);
        var b0 = Math.Max(0, target.B - tolerance);
        var b1 = Math.Min(255, target.B + tolerance);

        if (OperatingSystem.IsBrowser())
        {
            var raw = GetRawOrThrow(imageBytes);
            FloodFillConnectedRgba(raw.RgbaBytes, iw, ih, startX, startY, r0, r1, g0, g1, b0, b1, mask);
            return mask;
        }

        using var src = Decode(imageBytes);
        using var bgra = src.Channels() == 4 ? src.Clone() : new Mat();
        if (src.Channels() != 4)
            Cv2.CvtColor(src, bgra, ColorConversionCodes.BGR2BGRA);

        // Copy pixels out (OpenCV Mat may be non-contiguous depending on operations).
        var pixels = new byte[iw * ih * 4];
        if (bgra.IsContinuous())
        {
            Marshal.Copy(bgra.Data, pixels, 0, pixels.Length);
        }
        else
        {
            var rowBytes = iw * 4;
            for (var y = 0; y < ih; y++)
            {
                var srcRow = bgra.Ptr(y);
                Marshal.Copy(srcRow, pixels, y * rowBytes, rowBytes);
            }
        }

        FloodFillConnectedBgra(pixels, iw, ih, startX, startY, r0, r1, g0, g1, b0, b1, mask);
        return mask;
    }

    private static void FloodFillConnectedRgba(byte[] rgba, int width, int height, int sx, int sy,
        int r0, int r1, int g0, int g1, int b0, int b1, byte[] mask)
    {
        var visited = new bool[width * height];
        var stack = new int[width * height];
        var sp = 0;
        stack[sp++] = sy * width + sx;

        while (sp > 0)
        {
            var idx = stack[--sp];
            if (visited[idx])
                continue;

            visited[idx] = true;

            var p = idx * 4;
            var rr = rgba[p + 0];
            var gg = rgba[p + 1];
            var bb = rgba[p + 2];

            if (rr < r0 || rr > r1 || gg < g0 || gg > g1 || bb < b0 || bb > b1)
                continue;

            mask[idx] = 255;

            var x = idx % width;
            var y = idx / width;

            // 8-neighborhood
            for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0)
                    continue;

                var nx = x + dx;
                var ny = y + dy;
                if ((uint)nx >= (uint)width || (uint)ny >= (uint)height)
                    continue;

                var nidx = ny * width + nx;
                if (!visited[nidx])
                    stack[sp++] = nidx;
            }
        }
    }

    private static void FloodFillConnectedBgra(byte[] bgra, int width, int height, int sx, int sy,
        int r0, int r1, int g0, int g1, int b0, int b1, byte[] mask)
    {
        var visited = new bool[width * height];
        var stack = new int[width * height];
        var sp = 0;
        stack[sp++] = sy * width + sx;

        while (sp > 0)
        {
            var idx = stack[--sp];
            if (visited[idx])
                continue;

            visited[idx] = true;

            var p = idx * 4;
            var bb = bgra[p + 0];
            var gg = bgra[p + 1];
            var rr = bgra[p + 2];

            if (rr < r0 || rr > r1 || gg < g0 || gg > g1 || bb < b0 || bb > b1)
                continue;

            mask[idx] = 255;

            var x = idx % width;
            var y = idx / width;

            for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0)
                    continue;

                var nx = x + dx;
                var ny = y + dy;
                if ((uint)nx >= (uint)width || (uint)ny >= (uint)height)
                    continue;

                var nidx = ny * width + nx;
                if (!visited[nidx])
                    stack[sp++] = nidx;
            }
        }
    }

    public Rgba32 GetPixelColor(byte[] imageBytes, int x, int y)
    {
        var (iw, ih) = GetSize(imageBytes);
        if (iw <= 0 || ih <= 0)
            throw new InvalidOperationException("Image size is unknown.");

        x = Math.Clamp(x, 0, Math.Max(0, iw - 1));
        y = Math.Clamp(y, 0, Math.Max(0, ih - 1));

        if (OperatingSystem.IsBrowser())
        {
            var raw = GetRawOrThrow(imageBytes);
            var i = (y * iw + x) * 4;
            return new Rgba32(raw.RgbaBytes[i + 0], raw.RgbaBytes[i + 1], raw.RgbaBytes[i + 2], raw.RgbaBytes[i + 3]);
        }

        using var src = Decode(imageBytes);
        using var bgra = src.Channels() == 4 ? src.Clone() : new Mat();
        if (src.Channels() != 4)
            Cv2.CvtColor(src, bgra, ColorConversionCodes.BGR2BGRA);

        var v = bgra.At<Vec4b>(y, x);
        // OpenCV BGRA -> RGBA
        return new Rgba32(v.Item2, v.Item1, v.Item0, v.Item3);
    }

    public byte[] FillByMask(byte[] imageBytes, byte[] mask, Rgba32 fillColor)
    {
        var (iw, ih) = GetSize(imageBytes);
        if (iw <= 0 || ih <= 0)
            throw new InvalidOperationException("Image size is unknown.");

        if (mask is null || mask.Length != iw * ih)
            throw new ArgumentException("Mask size mismatch.", nameof(mask));

        if (OperatingSystem.IsBrowser())
        {
            var raw = GetRawOrThrow(imageBytes);
            var dst = raw.RgbaBytes.ToArray();

            for (var i = 0; i < mask.Length; i++)
            {
                if (mask[i] == 0)
                    continue;

                var p = i * 4;
                dst[p + 0] = fillColor.R;
                dst[p + 1] = fillColor.G;
                dst[p + 2] = fillColor.B;
                dst[p + 3] = 255;
            }

            return ReturnToken(new RawRgbaImage(iw, ih, dst));
        }

        using var src = Decode(imageBytes);
        using var split = SplitBgrAndAlpha(src);
        using var work = split.Bgr.Clone();
        using var alpha = split.Alpha?.Clone() ?? new Mat(work.Rows, work.Cols, MatType.CV_8UC1, Scalar.All(255));

        using var maskMat = new Mat(ih, iw, MatType.CV_8UC1, mask);
        var bgr = new Scalar(fillColor.B, fillColor.G, fillColor.R);
        work.SetTo(bgr, maskMat);
        alpha.SetTo(Scalar.All(255), maskMat);

        using var merged = MergeBgrAndAlpha(work, alpha);
        return EncodeForDisplay(merged);
    }

    public byte[] DrawText(byte[] imageBytes, string text, int x, int y, Rgba32 color, double scale = 1.0, int thickness = 2)
    {
        if (string.IsNullOrWhiteSpace(text))
            return imageBytes;

        var (iw, ih) = GetSize(imageBytes);
        if (iw <= 0 || ih <= 0)
            throw new InvalidOperationException("Image size is unknown.");

        x = Math.Clamp(x, 0, Math.Max(0, iw - 1));
        y = Math.Clamp(y, 0, Math.Max(0, ih - 1));

        scale = Math.Clamp(scale, 0.5, 10.0);
        thickness = Math.Clamp(thickness, 1, 10);

        if (OperatingSystem.IsBrowser())
        {
            // Minimal ASCII-only bitmap font fallback.
            var raw = GetRawOrThrow(imageBytes);
            var dst = raw.RgbaBytes.ToArray();

            var intScale = Math.Clamp((int)Math.Round(scale), 1, 10);
            DrawAsciiText(dst, iw, ih, x, y, text, color, intScale, thickness);
            return ReturnToken(new RawRgbaImage(iw, ih, dst));
        }

        using var src = Decode(imageBytes);
        using var split = SplitBgrAndAlpha(src);
        using var work = split.Bgr.Clone();
        using var alpha = split.Alpha?.Clone() ?? new Mat(work.Rows, work.Cols, MatType.CV_8UC1, Scalar.All(255));

        var bgr = new Scalar(color.B, color.G, color.R);
        Cv2.PutText(work, text, new Point(x, y), HersheyFonts.HersheySimplex, scale, bgr, thickness, LineTypes.AntiAlias);
        // Approximate alpha as opaque for the text region by drawing a mask in parallel.
        using (var tmp = new Mat(work.Rows, work.Cols, MatType.CV_8UC1, Scalar.All(0)))
        {
            Cv2.PutText(tmp, text, new Point(x, y), HersheyFonts.HersheySimplex, scale, Scalar.All(255), thickness, LineTypes.AntiAlias);
            alpha.SetTo(Scalar.All(255), tmp);
        }

        using var merged = MergeBgrAndAlpha(work, alpha);
        return EncodeForDisplay(merged);
    }

    private static void DrawAsciiText(byte[] rgba, int width, int height, int x, int y, string text, Rgba32 color, int scale, int thickness)
    {
        // 5x7 font (very small); we only support basic ASCII 32..126.
        // NOTE: glyph rows are encoded MSB-left (we read bit 4..0 for columns 0..4).
        foreach (var ch in text)
        {
            if (ch == '\n')
            {
                y += 10 * Math.Max(1, scale);
                x = 0;
                continue;
            }

            if (ch < 32 || ch > 126)
            {
                x += 6 * Math.Max(1, scale);
                continue;
            }

            var glyph = Ascii5x7[ch - 32];
            for (var row = 0; row < 7; row++)
            {
                var bits = glyph[row];
                var yy0 = y + (row * scale);

                for (var col = 0; col < 5; col++)
                {
                    if (((bits >> (4 - col)) & 1) == 0)
                        continue;

                    var xx0 = x + (col * scale);

                    // Draw scaled "pixel" with simple thickness (square dilation).
                    var radius = Math.Max(0, thickness - 1);
                    for (var sy = 0; sy < scale; sy++)
                    {
                        var yy = yy0 + sy;
                        if ((uint)yy >= (uint)height)
                            continue;

                        for (var sx = 0; sx < scale; sx++)
                        {
                            var xx = xx0 + sx;
                            if ((uint)xx >= (uint)width)
                                continue;

                            for (var dy = -radius; dy <= radius; dy++)
                            {
                                var yyy = yy + dy;
                                if ((uint)yyy >= (uint)height)
                                    continue;

                                for (var dx = -radius; dx <= radius; dx++)
                                {
                                    var xxx = xx + dx;
                                    if ((uint)xxx >= (uint)width)
                                        continue;

                                    var idx = (yyy * width + xxx) * 4;
                                    rgba[idx + 0] = color.R;
                                    rgba[idx + 1] = color.G;
                                    rgba[idx + 2] = color.B;
                                    rgba[idx + 3] = 255;
                                }
                            }
                        }
                    }
                }
            }

            x += 6 * Math.Max(1, scale);
        }
    }

    private static readonly byte[][] Ascii5x7 = BuildAscii5x7();

    private static byte[][] BuildAscii5x7()
    {
        // Minimal built-in: digits + A-Z + a-z + basic punctuation; others blank.
        // NOTE: This is intentionally tiny; unsupported glyphs render as spaces.
        var table = new byte[95][];
        for (var i = 0; i < table.Length; i++)
            table[i] = new byte[7];

        void Set(char c, params byte[] rows) => table[c - 32] = rows;

        // Digits 0-9
        Set('0', 0x1E, 0x11, 0x13, 0x15, 0x19, 0x11, 0x1E);
        Set('1', 0x04, 0x0C, 0x04, 0x04, 0x04, 0x04, 0x0E);
        Set('2', 0x1E, 0x11, 0x01, 0x06, 0x08, 0x10, 0x1F);
        Set('3', 0x1E, 0x11, 0x01, 0x0E, 0x01, 0x11, 0x1E);
        Set('4', 0x02, 0x06, 0x0A, 0x12, 0x1F, 0x02, 0x02);
        Set('5', 0x1F, 0x10, 0x1E, 0x01, 0x01, 0x11, 0x1E);
        Set('6', 0x0E, 0x10, 0x1E, 0x11, 0x11, 0x11, 0x0E);
        Set('7', 0x1F, 0x01, 0x02, 0x04, 0x08, 0x08, 0x08);
        Set('8', 0x0E, 0x11, 0x11, 0x0E, 0x11, 0x11, 0x0E);
        Set('9', 0x0E, 0x11, 0x11, 0x0F, 0x01, 0x02, 0x1C);

        // Uppercase A-Z
        Set('A', 0x0E, 0x11, 0x11, 0x1F, 0x11, 0x11, 0x11);
        Set('B', 0x1E, 0x11, 0x11, 0x1E, 0x11, 0x11, 0x1E);
        Set('C', 0x0E, 0x11, 0x10, 0x10, 0x10, 0x11, 0x0E);
        Set('D', 0x1C, 0x12, 0x11, 0x11, 0x11, 0x12, 0x1C);
        Set('E', 0x1F, 0x10, 0x10, 0x1E, 0x10, 0x10, 0x1F);
        Set('F', 0x1F, 0x10, 0x10, 0x1E, 0x10, 0x10, 0x10);
        Set('G', 0x0E, 0x11, 0x10, 0x17, 0x11, 0x11, 0x0E);
        Set('H', 0x11, 0x11, 0x11, 0x1F, 0x11, 0x11, 0x11);
        Set('I', 0x0E, 0x04, 0x04, 0x04, 0x04, 0x04, 0x0E);
        Set('J', 0x07, 0x02, 0x02, 0x02, 0x02, 0x12, 0x0C);
        Set('K', 0x11, 0x12, 0x14, 0x18, 0x14, 0x12, 0x11);
        Set('L', 0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x1F);
        Set('M', 0x11, 0x1B, 0x15, 0x15, 0x11, 0x11, 0x11);
        Set('N', 0x11, 0x19, 0x15, 0x13, 0x11, 0x11, 0x11);
        Set('O', 0x0E, 0x11, 0x11, 0x11, 0x11, 0x11, 0x0E);
        Set('P', 0x1E, 0x11, 0x11, 0x1E, 0x10, 0x10, 0x10);
        Set('Q', 0x0E, 0x11, 0x11, 0x11, 0x15, 0x12, 0x0D);
        Set('R', 0x1E, 0x11, 0x11, 0x1E, 0x14, 0x12, 0x11);
        Set('S', 0x0F, 0x10, 0x10, 0x0E, 0x01, 0x01, 0x1E);
        Set('T', 0x1F, 0x04, 0x04, 0x04, 0x04, 0x04, 0x04);
        Set('U', 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x0E);
        Set('V', 0x11, 0x11, 0x11, 0x11, 0x11, 0x0A, 0x04);
        Set('W', 0x11, 0x11, 0x11, 0x15, 0x15, 0x15, 0x0A);
        Set('X', 0x11, 0x11, 0x0A, 0x04, 0x0A, 0x11, 0x11);
        Set('Y', 0x11, 0x11, 0x0A, 0x04, 0x04, 0x04, 0x04);
        Set('Z', 0x1F, 0x01, 0x02, 0x04, 0x08, 0x10, 0x1F);

        // Lowercase a-z (basic)
        Set('a', 0x00, 0x00, 0x0E, 0x01, 0x0F, 0x11, 0x0F);
        Set('b', 0x10, 0x10, 0x1E, 0x11, 0x11, 0x11, 0x1E);
        Set('c', 0x00, 0x00, 0x0E, 0x11, 0x10, 0x11, 0x0E);
        Set('d', 0x01, 0x01, 0x0F, 0x11, 0x11, 0x11, 0x0F);
        Set('e', 0x00, 0x00, 0x0E, 0x11, 0x1F, 0x10, 0x0E);
        Set('f', 0x06, 0x08, 0x1E, 0x08, 0x08, 0x08, 0x08);
        Set('g', 0x00, 0x00, 0x0F, 0x11, 0x11, 0x0F, 0x01);
        Set('h', 0x10, 0x10, 0x1E, 0x11, 0x11, 0x11, 0x11);
        Set('i', 0x04, 0x00, 0x0C, 0x04, 0x04, 0x04, 0x0E);
        Set('j', 0x02, 0x00, 0x06, 0x02, 0x02, 0x12, 0x0C);
        Set('k', 0x10, 0x10, 0x11, 0x12, 0x1C, 0x12, 0x11);
        Set('l', 0x0C, 0x04, 0x04, 0x04, 0x04, 0x04, 0x0E);
        Set('m', 0x00, 0x00, 0x1A, 0x15, 0x15, 0x11, 0x11);
        Set('n', 0x00, 0x00, 0x1E, 0x11, 0x11, 0x11, 0x11);
        Set('o', 0x00, 0x00, 0x0E, 0x11, 0x11, 0x11, 0x0E);
        Set('p', 0x00, 0x00, 0x1E, 0x11, 0x11, 0x1E, 0x10);
        Set('q', 0x00, 0x00, 0x0F, 0x11, 0x11, 0x0F, 0x01);
        Set('r', 0x00, 0x00, 0x16, 0x19, 0x10, 0x10, 0x10);
        Set('s', 0x00, 0x00, 0x0F, 0x10, 0x0E, 0x01, 0x1E);
        Set('t', 0x08, 0x08, 0x1E, 0x08, 0x08, 0x08, 0x06);
        Set('u', 0x00, 0x00, 0x11, 0x11, 0x11, 0x11, 0x0F);
        Set('v', 0x00, 0x00, 0x11, 0x11, 0x11, 0x0A, 0x04);
        Set('w', 0x00, 0x00, 0x11, 0x11, 0x15, 0x15, 0x0A);
        Set('x', 0x00, 0x00, 0x11, 0x0A, 0x04, 0x0A, 0x11);
        Set('y', 0x00, 0x00, 0x11, 0x11, 0x11, 0x0F, 0x01);
        Set('z', 0x00, 0x00, 0x1F, 0x02, 0x04, 0x08, 0x1F);

        // Punctuation
        Set(' ', 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00);
        Set('.', 0x00, 0x00, 0x00, 0x00, 0x00, 0x0C, 0x0C);
        Set('-', 0x00, 0x00, 0x00, 0x1F, 0x00, 0x00, 0x00);
        Set('_', 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x1F);
        Set(':', 0x00, 0x0C, 0x0C, 0x00, 0x0C, 0x0C, 0x00);
        Set('/', 0x01, 0x02, 0x04, 0x08, 0x10, 0x00, 0x00);

        return table;
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
            // Preserve alpha channel when present.
            var src = Cv2.ImDecode(imageBytes, ImreadModes.Unchanged);
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

        // Most native paths operate on BGR; alpha-preserving pipeline handles native PNG alpha separately.
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

    private byte[] EncodeForDisplay(Mat mat)
    {
        // Browser: avoid Cv2.ImEncode entirely (it may throw a native exception that bypasses managed catch).
        if (OperatingSystem.IsBrowser())
        {
            if (_raw is IRawImageCache browserCache)
            {
                var token = RawToken.Create();
                browserCache.Set(ImageSignature.Create(token), ExtractRgba(mat));
                return token;
            }

            throw new InvalidOperationException("Raw image cache not available on browser runtime.");
        }

        // 1) Try PNG
        try
        {
            Cv2.ImEncode(".png", mat, out var buf);
            var png = buf.ToArray();
            CacheRawIfPossible(png, mat);
            return png;
        }
        catch (Exception ex) when (IsMissingEncoder(ex))
        {
            // fall through
        }

        // If the image contains alpha, do not attempt BMP (it would drop transparency).
        if (mat.Channels() == 4)
        {
            if (_raw is IRawImageCache cacheWithAlpha)
            {
                var token = RawToken.Create();
                cacheWithAlpha.Set(ImageSignature.Create(token), ExtractRgba(mat));
                return token;
            }

            throw new InvalidOperationException("OpenCV PNG encoder is unavailable in this runtime.");
        }

        // 2) Try BMP
        try
        {
            Cv2.ImEncode(".bmp", mat, out var bmp);
            var bytes = bmp.ToArray();
            CacheRawIfPossible(bytes, mat);
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
            var raw = ExtractRgba(mat);
            cache.Set(ImageSignature.Create(token), raw);
            return token;
        }

        throw new InvalidOperationException("OpenCV image encoder is unavailable in this runtime.");
    }

    private static bool IsMissingEncoder(Exception ex)
        => ex.Message?.Contains("could not find encoder", StringComparison.OrdinalIgnoreCase) == true;

    private static RawRgbaImage ExtractRgba(Mat mat)
    {
        using var rgba = new Mat();
        if (mat.Channels() == 4)
            Cv2.CvtColor(mat, rgba, ColorConversionCodes.BGRA2RGBA);
        else
            Cv2.CvtColor(mat, rgba, ColorConversionCodes.BGR2RGBA);

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

    private void CacheRawIfPossible(byte[] encodedBytes, Mat mat)
    {
        if (_raw is not IRawImageCache cache)
            return;

        try
        {
            var sig = ImageSignature.Create(encodedBytes);
            cache.Set(sig, ExtractRgba(mat));
        }
        catch
        {
        }
    }

    private sealed class BgrAlphaSplit : IDisposable
    {
        public required Mat Bgr { get; init; }
        public Mat? Alpha { get; init; }

        public void Dispose()
        {
            Bgr.Dispose();
            Alpha?.Dispose();
        }
    }

    private static BgrAlphaSplit SplitBgrAndAlpha(Mat src)
    {
        if (src.Empty())
            throw new InvalidOperationException("Source image is empty.");

        return src.Channels() switch
        {
            3 => new BgrAlphaSplit { Bgr = src.Clone(), Alpha = null },
            4 => SplitBgra(src),
            _ => throw new InvalidOperationException($"Unsupported channel count: {src.Channels()}"),
        };

        static BgrAlphaSplit SplitBgra(Mat bgra)
        {
            var bgr = new Mat();
            Cv2.CvtColor(bgra, bgr, ColorConversionCodes.BGRA2BGR);

            var alpha = new Mat();
            Cv2.ExtractChannel(bgra, alpha, 3);

            return new BgrAlphaSplit { Bgr = bgr, Alpha = alpha };
        }
    }

    private static Mat MergeBgrAndAlpha(Mat bgr, Mat alpha)
    {
        var bgra = new Mat();
        Cv2.CvtColor(bgr, bgra, ColorConversionCodes.BGR2BGRA);
        Cv2.InsertChannel(alpha, bgra, 3);
        return bgra;
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

        if (Math.Abs(settings.Saturation - 1.0) > 0.0001)
            work = Raw.RgbaImageOps.AdjustSaturation(work, settings.Saturation);

        if (settings.BlurKernelSize > 1)
            work = Raw.RgbaImageOps.GaussianBlur(work, settings.BlurKernelSize);

        if (settings.Invert)
            work = Raw.RgbaImageOps.Invert(work);

        if (settings.Sketch)
            work = Raw.RgbaImageOps.PencilSketch(work, settings.BlurKernelSize);
        else if (settings.Cartoon)
            work = Raw.RgbaImageOps.Cartoon(work, settings.BlurKernelSize, settings.CannyThreshold1, settings.CannyThreshold2);

        if (settings.Sepia)
            work = Raw.RgbaImageOps.Sepia(work);
        else if (settings.Grayscale)
            work = Raw.RgbaImageOps.Grayscale(work);

        if (settings.Emboss)
            work = Raw.RgbaImageOps.Emboss(work);

        if (settings.SharpenAmount > 0.0001)
            work = Raw.RgbaImageOps.Sharpen(work, settings.SharpenAmount);

        if (settings.GlowStrength > 0.0001)
            work = Raw.RgbaImageOps.Glow(work, settings.BlurKernelSize, settings.GlowStrength);

        if (settings.PosterizeLevels >= 2)
            work = Raw.RgbaImageOps.Posterize(work, settings.PosterizeLevels);

        if (settings.ColorMap != ColorMapStyle.None && settings.ColorMapStrength > 0.0001)
            work = Raw.RgbaImageOps.ApplyColorMap(work, settings.ColorMap, settings.ColorMapStrength);

        if (settings.VignetteStrength > 0.0001)
            work = Raw.RgbaImageOps.Vignette(work, settings.VignetteStrength);

        if (settings.PixelizeBlockSize >= 2)
            work = Raw.RgbaImageOps.Pixelize(work, settings.PixelizeBlockSize);

        if (settings.NoiseAmount > 0.0001)
            work = Raw.RgbaImageOps.AddNoise(work, settings.NoiseAmount);

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

    public (byte[] Bytes, string ContentType) CreateTransparent(int width, int height)
    {
        width = Math.Clamp(width, 1, 8192);
        height = Math.Clamp(height, 1, 8192);

        if (OperatingSystem.IsBrowser())
        {
            if (_raw is not IRawImageCache cache)
                throw new InvalidOperationException("Raw image cache not available on browser runtime.");

            var token = RawToken.Create();
            var rgba = new byte[checked(width * height * 4)];
            cache.Set(ImageSignature.Create(token), new RawRgbaImage(width, height, rgba));
            return (token, "moge/raw");
        }

        using var mat = new Mat(height, width, MatType.CV_8UC4, Scalar.All(0));
        return (EncodeForDisplay(mat), "image/png");
    }

    public byte[] CompositeRgbaLayers(IReadOnlyList<byte[]> layers)
    {
        if (layers is null)
            throw new ArgumentNullException(nameof(layers));

        if (layers.Count == 0)
            throw new ArgumentException("Expected at least one layer.", nameof(layers));

        if (layers.Count == 1)
            return layers[0];

        if (OperatingSystem.IsBrowser())
        {
            var raws = new List<RawRgbaImage>(layers.Count);
            for (var i = 0; i < layers.Count; i++)
                raws.Add(GetRawOrThrow(layers[i]));

            var w = raws[0].Width;
            var h = raws[0].Height;
            if (raws.Any(r => r.Width != w || r.Height != h))
                throw new InvalidOperationException("All layers must share the same dimensions.");

            var dst = new byte[checked(w * h * 4)];
            foreach (var l in raws)
                AlphaBlendOverRgba(dst, l.RgbaBytes);

            return ReturnToken(new RawRgbaImage(w, h, dst));
        }

        using var baseMat = Decode(layers[0]);
        using var accBgra = EnsureBgra(baseMat);

        for (var i = 1; i < layers.Count; i++)
        {
            using var m = Decode(layers[i]);
            using var over = EnsureBgra(m);
            AlphaBlendOverBgra(accBgra, over);
        }

        return EncodeForDisplay(accBgra);

        static Mat EnsureBgra(Mat m)
        {
            if (m.Empty())
                return new Mat();

            if (m.Channels() == 4)
                return m.Clone();

            var bgra = new Mat();
            Cv2.CvtColor(m, bgra, ColorConversionCodes.BGR2BGRA);
            return bgra;
        }

        static void AlphaBlendOverBgra(Mat dstBgra, Mat srcBgra)
        {
            if (dstBgra.Empty() || srcBgra.Empty())
                return;

            if (dstBgra.Width != srcBgra.Width || dstBgra.Height != srcBgra.Height)
                throw new InvalidOperationException("All layers must share the same dimensions.");

            for (var y = 0; y < dstBgra.Rows; y++)
            {
                for (var x = 0; x < dstBgra.Cols; x++)
                {
                    var s = srcBgra.At<Vec4b>(y, x); // BGRA
                    var d = dstBgra.At<Vec4b>(y, x);

                    var sa = s.Item3 / 255.0;
                    var da = d.Item3 / 255.0;

                    var outA = sa + da * (1 - sa);
                    if (outA <= 0.000001)
                    {
                        dstBgra.Set(y, x, new Vec4b(0, 0, 0, 0));
                        continue;
                    }

                    byte Blend(byte sc, byte dc)
                    {
                        var outC = (sc * sa + dc * da * (1 - sa)) / outA;
                        return (byte)Math.Clamp((int)Math.Round(outC), 0, 255);
                    }

                    var outB = Blend(s.Item0, d.Item0);
                    var outG = Blend(s.Item1, d.Item1);
                    var outR = Blend(s.Item2, d.Item2);
                    var outAlpha = (byte)Math.Clamp((int)Math.Round(outA * 255.0), 0, 255);

                    dstBgra.Set(y, x, new Vec4b(outB, outG, outR, outAlpha));
                }
            }
        }

        static void AlphaBlendOverRgba(byte[] dstRgba, byte[] srcRgba)
        {
            if (dstRgba.Length != srcRgba.Length)
                throw new InvalidOperationException("All layers must share the same dimensions.");

            for (var i = 0; i < dstRgba.Length; i += 4)
            {
                var sr = srcRgba[i + 0] / 255.0;
                var sg = srcRgba[i + 1] / 255.0;
                var sb = srcRgba[i + 2] / 255.0;
                var sa = srcRgba[i + 3] / 255.0;

                var dr = dstRgba[i + 0] / 255.0;
                var dg = dstRgba[i + 1] / 255.0;
                var db = dstRgba[i + 2] / 255.0;
                var da = dstRgba[i + 3] / 255.0;

                var outA = sa + da * (1 - sa);
                if (outA <= 0.000001)
                {
                    dstRgba[i + 0] = 0;
                    dstRgba[i + 1] = 0;
                    dstRgba[i + 2] = 0;
                    dstRgba[i + 3] = 0;
                    continue;
                }

                double Blend(double sc, double dc) => (sc * sa + dc * da * (1 - sa)) / outA;

                dstRgba[i + 0] = (byte)Math.Clamp((int)Math.Round(Blend(sr, dr) * 255.0), 0, 255);
                dstRgba[i + 1] = (byte)Math.Clamp((int)Math.Round(Blend(sg, dg) * 255.0), 0, 255);
                dstRgba[i + 2] = (byte)Math.Clamp((int)Math.Round(Blend(sb, db) * 255.0), 0, 255);
                dstRgba[i + 3] = (byte)Math.Clamp((int)Math.Round(outA * 255.0), 0, 255);
            }
        }
    }

    /// <summary>
    /// Creates a selection mask from polygon contour points (lasso selection).
    /// Returns a byte[] mask where 255 = inside the polygon, 0 = outside.
    /// </summary>
    public byte[] CreatePolygonMask(byte[] imageBytes, IReadOnlyList<(int X, int Y)> polygonPoints)
    {
        var (iw, ih) = GetSize(imageBytes);
        if (iw <= 0 || ih <= 0)
            throw new InvalidOperationException("Image size is unknown.");

        if (polygonPoints is null || polygonPoints.Count < 3)
            return new byte[iw * ih]; // Empty mask for insufficient points

        var mask = new byte[iw * ih];

        if (OperatingSystem.IsBrowser())
        {
            // Browser path: software rasterization of polygon
            FillPolygonSoftware(mask, iw, ih, polygonPoints);
            return mask;
        }

        // Native path: use OpenCV to fill the polygon
        using var maskMat = new Mat(ih, iw, MatType.CV_8UC1, Scalar.All(0));
        var pts = polygonPoints.Select(p => new OpenCvSharp.Point(
            Math.Clamp(p.X, 0, iw - 1),
            Math.Clamp(p.Y, 0, ih - 1)
        )).ToArray();

        Cv2.FillPoly(maskMat, new[] { pts }, new Scalar(255));

        if (maskMat.IsContinuous())
        {
            Marshal.Copy(maskMat.Data, mask, 0, mask.Length);
        }
        else
        {
            for (var y = 0; y < ih; y++)
            {
                var srcRow = maskMat.Ptr(y);
                Marshal.Copy(srcRow, mask, y * iw, iw);
            }
        }

        return mask;
    }

    /// <summary>
    /// Software polygon fill using scanline algorithm (for browser WASM).
    /// </summary>
    private static void FillPolygonSoftware(byte[] mask, int width, int height, IReadOnlyList<(int X, int Y)> pts)
    {
        var n = pts.Count;
        if (n < 3)
            return;

        // Find bounding box
        var minY = int.MaxValue;
        var maxY = int.MinValue;
        foreach (var p in pts)
        {
            if (p.Y < minY) minY = p.Y;
            if (p.Y > maxY) maxY = p.Y;
        }

        minY = Math.Max(0, minY);
        maxY = Math.Min(height - 1, maxY);

        // Scanline fill
        var nodeX = new List<int>(n);

        for (var y = minY; y <= maxY; y++)
        {
            nodeX.Clear();

            var j = n - 1;
            for (var i = 0; i < n; i++)
            {
                var yi = pts[i].Y;
                var yj = pts[j].Y;
                var xi = pts[i].X;
                var xj = pts[j].X;

                if ((yi < y && yj >= y) || (yj < y && yi >= y))
                {
                    var xIntersect = xi + (y - yi) * (xj - xi) / (double)(yj - yi);
                    nodeX.Add((int)Math.Round(xIntersect));
                }

                j = i;
            }

            nodeX.Sort();

            for (var i = 0; i + 1 < nodeX.Count; i += 2)
            {
                var x0 = Math.Max(0, nodeX[i]);
                var x1 = Math.Min(width - 1, nodeX[i + 1]);

                for (var x = x0; x <= x1; x++)
                {
                    mask[y * width + x] = 255;
                }
            }
        }
    }
}

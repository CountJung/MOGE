using OpenCvSharp;

namespace SharedUI.Services.Raw;

internal static class RgbaImageOps
{
    public static RawRgbaImage AdjustBrightnessContrast(in RawRgbaImage src, double contrast, double brightness)
    {
        if (src.RgbaBytes is null || src.RgbaBytes.Length == 0)
            return src;

        var dst = new byte[src.RgbaBytes.Length];
        for (var i = 0; i < dst.Length; i += 4)
        {
            dst[i + 0] = ClampToByte(contrast * src.RgbaBytes[i + 0] + brightness);
            dst[i + 1] = ClampToByte(contrast * src.RgbaBytes[i + 1] + brightness);
            dst[i + 2] = ClampToByte(contrast * src.RgbaBytes[i + 2] + brightness);
            dst[i + 3] = src.RgbaBytes[i + 3];
        }

        return new RawRgbaImage(src.Width, src.Height, dst);
    }

    public static RawRgbaImage Grayscale(in RawRgbaImage src)
    {
        var dst = new byte[src.RgbaBytes.Length];
        for (var i = 0; i < dst.Length; i += 4)
        {
            var r = src.RgbaBytes[i + 0];
            var g = src.RgbaBytes[i + 1];
            var b = src.RgbaBytes[i + 2];

            var y = (byte)Math.Clamp((int)Math.Round(0.299 * r + 0.587 * g + 0.114 * b), 0, 255);

            dst[i + 0] = y;
            dst[i + 1] = y;
            dst[i + 2] = y;
            dst[i + 3] = src.RgbaBytes[i + 3];
        }

        return new RawRgbaImage(src.Width, src.Height, dst);
    }

    public static RawRgbaImage Sepia(in RawRgbaImage src)
    {
        var dst = new byte[src.RgbaBytes.Length];
        for (var i = 0; i < dst.Length; i += 4)
        {
            var r = src.RgbaBytes[i + 0];
            var g = src.RgbaBytes[i + 1];
            var b = src.RgbaBytes[i + 2];

            var outR = 0.393 * r + 0.769 * g + 0.189 * b;
            var outG = 0.349 * r + 0.686 * g + 0.168 * b;
            var outB = 0.272 * r + 0.534 * g + 0.131 * b;

            dst[i + 0] = ClampToByte(outR);
            dst[i + 1] = ClampToByte(outG);
            dst[i + 2] = ClampToByte(outB);
            dst[i + 3] = src.RgbaBytes[i + 3];
        }

        return new RawRgbaImage(src.Width, src.Height, dst);
    }

    public static RawRgbaImage GaussianBlur(in RawRgbaImage src, int kernelSize)
    {
        kernelSize = NormalizeOddKernel(kernelSize);
        if (kernelSize <= 1)
            return src;

        var width = src.Width;
        var height = src.Height;
        var srcBytes = src.RgbaBytes;

        var kernel = CreateGaussianKernel1D(kernelSize);

        // horizontal pass -> tmp float
        var tmp = new float[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                for (var c = 0; c < 4; c++)
                {
                    float sum = 0;
                    for (var k = 0; k < kernelSize; k++)
                    {
                        var ix = Clamp(x + k - kernelSize / 2, 0, width - 1);
                        var idx = (y * width + ix) * 4 + c;
                        sum += srcBytes[idx] * kernel[k];
                    }
                    tmp[(y * width + x) * 4 + c] = sum;
                }
            }
        }

        // vertical pass -> dst
        var dst = new byte[srcBytes.Length];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                for (var c = 0; c < 4; c++)
                {
                    float sum = 0;
                    for (var k = 0; k < kernelSize; k++)
                    {
                        var iy = Clamp(y + k - kernelSize / 2, 0, height - 1);
                        sum += tmp[(iy * width + x) * 4 + c] * kernel[k];
                    }
                    dst[(y * width + x) * 4 + c] = (byte)Math.Clamp((int)Math.Round(sum), 0, 255);
                }
            }
        }

        return new RawRgbaImage(width, height, dst);
    }

    public static RawRgbaImage CannyEdge(in RawRgbaImage src, double threshold1, double threshold2)
    {
        // Simplified Canny: grayscale -> Sobel -> NMS -> hysteresis
        var width = src.Width;
        var height = src.Height;
        var gray = ToGrayscaleBuffer(src);

        var gx = new short[width * height];
        var gy = new short[width * height];

        // Sobel 3x3
        for (var y = 1; y < height - 1; y++)
        {
            for (var x = 1; x < width - 1; x++)
            {
                var i = y * width + x;

                int p00 = gray[(y - 1) * width + (x - 1)];
                int p01 = gray[(y - 1) * width + x];
                int p02 = gray[(y - 1) * width + (x + 1)];
                int p10 = gray[y * width + (x - 1)];
                int p12 = gray[y * width + (x + 1)];
                int p20 = gray[(y + 1) * width + (x - 1)];
                int p21 = gray[(y + 1) * width + x];
                int p22 = gray[(y + 1) * width + (x + 1)];

                int sx = (-1 * p00) + (1 * p02)
                       + (-2 * p10) + (2 * p12)
                       + (-1 * p20) + (1 * p22);

                int sy = (-1 * p00) + (-2 * p01) + (-1 * p02)
                       + (1 * p20) + (2 * p21) + (1 * p22);

                gx[i] = (short)sx;
                gy[i] = (short)sy;
            }
        }

        var mag = new byte[width * height];
        var dir = new byte[width * height]; // 0,1,2,3 => 0,45,90,135

        for (var y = 1; y < height - 1; y++)
        {
            for (var x = 1; x < width - 1; x++)
            {
                var i = y * width + x;
                var sx = gx[i];
                var sy = gy[i];

                var m = Math.Sqrt((double)sx * sx + (double)sy * sy) / 4.0; // normalize ~0..255
                mag[i] = ClampToByte(m);

                var angle = Math.Atan2(sy, sx) * (180.0 / Math.PI);
                if (angle < 0) angle += 180;

                byte d;
                if (angle < 22.5 || angle >= 157.5) d = 0;
                else if (angle < 67.5) d = 1;
                else if (angle < 112.5) d = 2;
                else d = 3;

                dir[i] = d;
            }
        }

        // Non-maximum suppression
        var nms = new byte[width * height];
        for (var y = 1; y < height - 1; y++)
        {
            for (var x = 1; x < width - 1; x++)
            {
                var i = y * width + x;
                var m = mag[i];
                if (m == 0) continue;

                byte m1, m2;
                switch (dir[i])
                {
                    case 0:
                        m1 = mag[y * width + (x - 1)];
                        m2 = mag[y * width + (x + 1)];
                        break;
                    case 1:
                        m1 = mag[(y - 1) * width + (x + 1)];
                        m2 = mag[(y + 1) * width + (x - 1)];
                        break;
                    case 2:
                        m1 = mag[(y - 1) * width + x];
                        m2 = mag[(y + 1) * width + x];
                        break;
                    default:
                        m1 = mag[(y - 1) * width + (x - 1)];
                        m2 = mag[(y + 1) * width + (x + 1)];
                        break;
                }

                if (m >= m1 && m >= m2)
                    nms[i] = m;
            }
        }

        var t1 = Math.Min(threshold1, threshold2);
        var t2 = Math.Max(threshold1, threshold2);

        var strong = new bool[width * height];
        var weak = new bool[width * height];

        for (var i = 0; i < nms.Length; i++)
        {
            var m = nms[i];
            if (m >= t2) strong[i] = true;
            else if (m >= t1) weak[i] = true;
        }

        // Hysteresis: promote weak connected to strong
        var stack = new Stack<int>();
        for (var i = 0; i < strong.Length; i++)
            if (strong[i]) stack.Push(i);

        while (stack.Count > 0)
        {
            var i = stack.Pop();
            var x = i % width;
            var y = i / width;

            for (var yy = y - 1; yy <= y + 1; yy++)
            {
                for (var xx = x - 1; xx <= x + 1; xx++)
                {
                    if (xx < 0 || yy < 0 || xx >= width || yy >= height) continue;
                    var j = yy * width + xx;
                    if (!weak[j] || strong[j]) continue;
                    strong[j] = true;
                    stack.Push(j);
                }
            }
        }

        var dst = new byte[width * height * 4];
        for (var i = 0; i < strong.Length; i++)
        {
            var v = (byte)(strong[i] ? 255 : 0);
            var o = i * 4;
            dst[o + 0] = v;
            dst[o + 1] = v;
            dst[o + 2] = v;
            dst[o + 3] = 255;
        }

        return new RawRgbaImage(width, height, dst);
    }

    public static RawRgbaImage Rotate90(in RawRgbaImage src, RotateFlags flags)
    {
        var w = src.Width;
        var h = src.Height;
        var s = src.RgbaBytes;

        return flags switch
        {
            RotateFlags.Rotate90Clockwise => Rotate90Clockwise(w, h, s),
            RotateFlags.Rotate90Counterclockwise => Rotate90CounterClockwise(w, h, s),
            RotateFlags.Rotate180 => Rotate180(w, h, s),
            _ => Rotate90Clockwise(w, h, s)
        };
    }

    public static RawRgbaImage Resize(in RawRgbaImage src, int newWidth, int newHeight)
    {
        newWidth = Math.Max(1, newWidth);
        newHeight = Math.Max(1, newHeight);

        var sw = src.Width;
        var sh = src.Height;
        var s = src.RgbaBytes;

        if (newWidth == sw && newHeight == sh)
            return src;

        var dst = new byte[newWidth * newHeight * 4];

        var xScale = (double)(sw - 1) / Math.Max(1, newWidth - 1);
        var yScale = (double)(sh - 1) / Math.Max(1, newHeight - 1);

        for (var y = 0; y < newHeight; y++)
        {
            var sy = y * yScale;
            var y0 = (int)Math.Floor(sy);
            var y1 = Math.Min(y0 + 1, sh - 1);
            var fy = sy - y0;

            for (var x = 0; x < newWidth; x++)
            {
                var sx = x * xScale;
                var x0 = (int)Math.Floor(sx);
                var x1 = Math.Min(x0 + 1, sw - 1);
                var fx = sx - x0;

                var o = (y * newWidth + x) * 4;
                var i00 = (y0 * sw + x0) * 4;
                var i10 = (y0 * sw + x1) * 4;
                var i01 = (y1 * sw + x0) * 4;
                var i11 = (y1 * sw + x1) * 4;

                for (var c = 0; c < 4; c++)
                {
                    var v00 = s[i00 + c];
                    var v10 = s[i10 + c];
                    var v01 = s[i01 + c];
                    var v11 = s[i11 + c];

                    var v0 = v00 + (v10 - v00) * fx;
                    var v1 = v01 + (v11 - v01) * fx;
                    var v = v0 + (v1 - v0) * fy;

                    dst[o + c] = ClampToByte(v);
                }
            }
        }

        return new RawRgbaImage(newWidth, newHeight, dst);
    }

    public static RawRgbaImage WarpPerspective(in RawRgbaImage src, Point2f[] srcQuad, Point2f[] dstQuad, int outWidth, int outHeight)
    {
        if (srcQuad.Length != 4 || dstQuad.Length != 4)
            throw new ArgumentException("srcQuad and dstQuad must contain exactly 4 points.");

        outWidth = Math.Max(1, outWidth);
        outHeight = Math.Max(1, outHeight);

        // Compute homography that maps destination -> source
        var h = ComputeHomography(dstQuad, srcQuad);

        var sw = src.Width;
        var sh = src.Height;
        var s = src.RgbaBytes;

        var dst = new byte[outWidth * outHeight * 4];

        for (var y = 0; y < outHeight; y++)
        {
            for (var x = 0; x < outWidth; x++)
            {
                var (sx, sy) = ApplyHomography(h, x, y);
                var o = (y * outWidth + x) * 4;

                if (sx < 0 || sy < 0 || sx >= sw - 1 || sy >= sh - 1)
                {
                    dst[o + 0] = 0;
                    dst[o + 1] = 0;
                    dst[o + 2] = 0;
                    dst[o + 3] = 255;
                    continue;
                }

                // bilinear sample
                var x0 = (int)Math.Floor(sx);
                var y0 = (int)Math.Floor(sy);
                var x1 = x0 + 1;
                var y1 = y0 + 1;

                var fx = sx - x0;
                var fy = sy - y0;

                var i00 = (y0 * sw + x0) * 4;
                var i10 = (y0 * sw + x1) * 4;
                var i01 = (y1 * sw + x0) * 4;
                var i11 = (y1 * sw + x1) * 4;

                for (var c = 0; c < 4; c++)
                {
                    var v00 = s[i00 + c];
                    var v10 = s[i10 + c];
                    var v01 = s[i01 + c];
                    var v11 = s[i11 + c];

                    var v0 = v00 + (v10 - v00) * fx;
                    var v1 = v01 + (v11 - v01) * fx;
                    var v = v0 + (v1 - v0) * fy;

                    dst[o + c] = ClampToByte(v);
                }
            }
        }

        return new RawRgbaImage(outWidth, outHeight, dst);
    }

    private static byte[] ToGrayscaleBuffer(in RawRgbaImage src)
    {
        var gray = new byte[src.Width * src.Height];
        var s = src.RgbaBytes;
        for (var i = 0; i < gray.Length; i++)
        {
            var o = i * 4;
            var r = s[o + 0];
            var g = s[o + 1];
            var b = s[o + 2];
            gray[i] = (byte)Math.Clamp((int)Math.Round(0.299 * r + 0.587 * g + 0.114 * b), 0, 255);
        }
        return gray;
    }

    private static RawRgbaImage Rotate90Clockwise(int w, int h, byte[] s)
    {
        var dst = new byte[w * h * 4];
        // output dims: h x w
        dst = new byte[h * w * 4];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var srcIdx = (y * w + x) * 4;
                var dx = h - 1 - y;
                var dy = x;
                var dstIdx = (dy * h + dx) * 4;
                Buffer.BlockCopy(s, srcIdx, dst, dstIdx, 4);
            }
        }
        return new RawRgbaImage(h, w, dst);
    }

    private static RawRgbaImage Rotate90CounterClockwise(int w, int h, byte[] s)
    {
        var dst = new byte[h * w * 4];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var srcIdx = (y * w + x) * 4;
                var dx = y;
                var dy = w - 1 - x;
                var dstIdx = (dy * h + dx) * 4;
                Buffer.BlockCopy(s, srcIdx, dst, dstIdx, 4);
            }
        }
        return new RawRgbaImage(h, w, dst);
    }

    private static RawRgbaImage Rotate180(int w, int h, byte[] s)
    {
        var dst = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var srcIdx = (y * w + x) * 4;
                var dx = w - 1 - x;
                var dy = h - 1 - y;
                var dstIdx = (dy * w + dx) * 4;
                Buffer.BlockCopy(s, srcIdx, dst, dstIdx, 4);
            }
        }
        return new RawRgbaImage(w, h, dst);
    }

    private static double[] ComputeHomography(Point2f[] srcPts, Point2f[] dstPts)
    {
        // Solve for H mapping src -> dst; we use 8 unknowns with h33=1
        // Here, callers pass (dstQuad, srcQuad) to get dest->source.
        var a = new double[8, 8];
        var b = new double[8];

        for (var i = 0; i < 4; i++)
        {
            var x = srcPts[i].X;
            var y = srcPts[i].Y;
            var u = dstPts[i].X;
            var v = dstPts[i].Y;

            // row 2*i
            a[2 * i, 0] = x;
            a[2 * i, 1] = y;
            a[2 * i, 2] = 1;
            a[2 * i, 3] = 0;
            a[2 * i, 4] = 0;
            a[2 * i, 5] = 0;
            a[2 * i, 6] = -u * x;
            a[2 * i, 7] = -u * y;
            b[2 * i] = u;

            // row 2*i+1
            a[2 * i + 1, 0] = 0;
            a[2 * i + 1, 1] = 0;
            a[2 * i + 1, 2] = 0;
            a[2 * i + 1, 3] = x;
            a[2 * i + 1, 4] = y;
            a[2 * i + 1, 5] = 1;
            a[2 * i + 1, 6] = -v * x;
            a[2 * i + 1, 7] = -v * y;
            b[2 * i + 1] = v;
        }

        var xsol = SolveLinearSystem(a, b);

        // H as 3x3 in row-major, with h33=1
        return new[]
        {
            xsol[0], xsol[1], xsol[2],
            xsol[3], xsol[4], xsol[5],
            xsol[6], xsol[7], 1.0
        };
    }

    private static (double x, double y) ApplyHomography(double[] h, double x, double y)
    {
        var denom = h[6] * x + h[7] * y + h[8];
        if (Math.Abs(denom) < 1e-12)
            return (double.NaN, double.NaN);

        var nx = (h[0] * x + h[1] * y + h[2]) / denom;
        var ny = (h[3] * x + h[4] * y + h[5]) / denom;
        return (nx, ny);
    }

    private static double[] SolveLinearSystem(double[,] a, double[] b)
    {
        var n = b.Length;
        var aug = new double[n, n + 1];

        for (var r = 0; r < n; r++)
        {
            for (var c = 0; c < n; c++)
                aug[r, c] = a[r, c];
            aug[r, n] = b[r];
        }

        for (var col = 0; col < n; col++)
        {
            // pivot
            var pivotRow = col;
            var pivotVal = Math.Abs(aug[pivotRow, col]);
            for (var r = col + 1; r < n; r++)
            {
                var v = Math.Abs(aug[r, col]);
                if (v > pivotVal)
                {
                    pivotVal = v;
                    pivotRow = r;
                }
            }

            if (pivotVal < 1e-12)
                throw new InvalidOperationException("Degenerate perspective transform (singular matrix).");

            if (pivotRow != col)
            {
                for (var c = col; c <= n; c++)
                {
                    (aug[col, c], aug[pivotRow, c]) = (aug[pivotRow, c], aug[col, c]);
                }
            }

            // normalize
            var div = aug[col, col];
            for (var c = col; c <= n; c++)
                aug[col, c] /= div;

            // eliminate
            for (var r = 0; r < n; r++)
            {
                if (r == col) continue;
                var factor = aug[r, col];
                if (Math.Abs(factor) < 1e-12) continue;
                for (var c = col; c <= n; c++)
                    aug[r, c] -= factor * aug[col, c];
            }
        }

        var x = new double[n];
        for (var i = 0; i < n; i++)
            x[i] = aug[i, n];
        return x;
    }

    private static float[] CreateGaussianKernel1D(int kernelSize)
    {
        var sigma = 0.3 * ((kernelSize - 1) * 0.5 - 1) + 0.8;
        sigma = Math.Max(0.0001, sigma);

        var kernel = new float[kernelSize];
        var sum = 0.0;
        var r = kernelSize / 2;

        for (var i = 0; i < kernelSize; i++)
        {
            var x = i - r;
            var v = Math.Exp(-(x * x) / (2 * sigma * sigma));
            kernel[i] = (float)v;
            sum += v;
        }

        for (var i = 0; i < kernelSize; i++)
            kernel[i] = (float)(kernel[i] / sum);

        return kernel;
    }

    private static int NormalizeOddKernel(int kernelSize)
    {
        if (kernelSize < 1) kernelSize = 1;
        if (kernelSize % 2 == 0) kernelSize += 1;
        return kernelSize;
    }

    private static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);

    private static byte ClampToByte(double v) => (byte)Math.Clamp((int)Math.Round(v), 0, 255);
}

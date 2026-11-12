using SkiaSharp;
using MirrorStudio.Image.Models;

namespace MirrorStudio.Image.Filters;

/// <summary>Pixel-level filters that bake into a layer bitmap.</summary>
public static class FilterEngine
{
    public static void ApplyBlur(SKBitmap bmp, double radius)
    {
        if (bmp == null || radius <= 0.01) return;
        var result = ImageDocument.MakeBitmap(bmp.Width, bmp.Height);
        using var surface = SKSurface.Create(result.Info, result.GetPixels());
        var paint = new SKPaint
        {
            IsAntialias = true,
            ImageFilter = SKImageFilter.CreateBlur((float)radius, (float)radius)
        };
        surface.Canvas.Clear(SKColors.Transparent);
        surface.Canvas.DrawBitmap(bmp, 0, 0, paint);
        surface.Flush();
        CopyInto(bmp, result);
        result.Dispose();
    }

    private static void CopyInto(SKBitmap dst, SKBitmap src)
    {
        using var s = SKSurface.Create(dst.Info, dst.GetPixels());
        s.Canvas.Clear(SKColors.Transparent);
        s.Canvas.DrawBitmap(src, 0, 0);
        s.Flush();
    }

    public static void ApplySharpen(SKBitmap bmp, double amount)
    {
        // 3x3 unsharp-style kernel
        double k = Math.Max(0, amount) * 0.5;
        double[,] kernel =
        {
            { 0, -k, 0 },
            { -k, 1 + 4 * k, -k },
            { 0, -k, 0 }
        };
        Convolve(bmp, kernel);
    }

    public static void ApplyNoiseReduction(SKBitmap bmp, double strength)
    {
        // 3x3 median despeckle, strength drives pass count (1 here)
        MedianFilter(bmp, (int)Math.Round(strength));
    }

    public static void ApplyVignette(SKBitmap bmp, double amount, double size = 1.0)
    {
        if (bmp == null || amount <= 0) return;
        int w = bmp.Width, h = bmp.Height;
        IntPtr px = bmp.GetPixels();
        if (px == IntPtr.Zero) return;
        double cx = w / 2.0, cy = h / 2.0;
        double maxR = Math.Sqrt(cx * cx + cy * cy) * Math.Max(0.1, size);
        unsafe
        {
            var s = new Span<byte>(px.ToPointer(), w * h * 4);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int o = (y * w + x) * 4;
                byte a = s[o + 3];
                if (a == 0) continue;
                double dx = (x - cx) / maxR, dy = (y - cy) / maxR;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                double factor = 1 - amount * Math.Clamp(dist, 0, 1);
                s[o] = (byte)(s[o] * factor);
                s[o + 1] = (byte)(s[o + 1] * factor);
                s[o + 2] = (byte)(s[o + 2] * factor);
            }
        }
    }

    // ---------- convolution / median ----------

    private static void Convolve(SKBitmap bmp, double[,] kernel)
    {
        int w = bmp.Width, h = bmp.Height;
        var src = (byte[])bmp.Bytes.Clone();
        var dst = bmp.Bytes;
        int kh = kernel.GetLength(0), kw = kernel.GetLength(1);
        int cx = kw / 2, cy = kh / 2;
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int o = (y * w + x) * 4;
            if (src[o + 3] == 0) { dst[o] = src[o]; dst[o + 1] = src[o + 1]; dst[o + 2] = src[o + 2]; dst[o + 3] = 0; continue; }
            for (int c = 0; c < 3; c++)
            {
                double sum = 0;
                for (int ky = 0; ky < kh; ky++)
                for (int kx = 0; kx < kw; kx++)
                {
                    int sx = ClampX(x + kx - cx, w), sy = ClampY(y + ky - cy, h);
                    sum += src[(sy * w + sx) * 4 + c] * kernel[ky, kx];
                }
                dst[o + c] = (byte)Math.Max(0, Math.Min(255, sum));
            }
            dst[o + 3] = src[o + 3];
        }
    }

    private static void MedianFilter(SKBitmap bmp, int radius)
    {
        radius = Math.Clamp(radius, 1, 2);
        int w = bmp.Width, h = bmp.Height;
        var src = (byte[])bmp.Bytes.Clone();
        var dst = bmp.Bytes;
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int o = (y * w + x) * 4;
            if (src[o + 3] == 0) { dst[o] = src[o]; dst[o + 1] = src[o + 1]; dst[o + 2] = src[o + 2]; dst[o + 3] = 0; continue; }
            for (int c = 0; c < 3; c++)
            {
                var vals = new List<byte>();
                for (int dy = -radius; dy <= radius; dy++)
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int sx = ClampX(x + dx, w), sy = ClampY(y + dy, h);
                    vals.Add(src[(sy * w + sx) * 4 + c]);
                }
                vals.Sort();
                dst[o + c] = vals[vals.Count / 2];
            }
            dst[o + 3] = src[o + 3];
        }
    }

    private static int ClampX(int v, int w) => v < 0 ? 0 : v >= w ? w - 1 : v;
    private static int ClampY(int v, int h) => v < 0 ? 0 : v >= h ? h - 1 : v;
}

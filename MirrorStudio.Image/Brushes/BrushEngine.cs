using SkiaSharp;
using MirrorStudio.Image.Models;

namespace MirrorStudio.Image.Brushes;

/// <summary>
/// Rasterises brush strokes into a layer bitmap. Operates directly on
/// premultiplied RGBA pixels so it works correctly with transparency, the
/// active selection mask, and layer masks.
/// </summary>
public static class BrushEngine
{
    /// <summary>Stamp a single dab at (x,y) in image coordinates.</summary>
    public static void Stamp(SKBitmap target, Brush brush, double x, double y,
        SKColor color, bool erase, ImageMask? mask)
    {
        if (target == null) return;
        double radius = Math.Max(0.5, brush.Size / 2.0);
        int ix = (int)Math.Round(x), iy = (int)Math.Round(y);
        int r = (int)Math.Ceiling(radius);
        int w = target.Width, h = target.Height;
        IntPtr tp = target.GetPixels();
        if (tp == IntPtr.Zero) return;

        unsafe
        {
            var ts = new Span<byte>(tp.ToPointer(), w * h * 4);
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                int px = ix + dx, py = iy + dy;
                if (px < 0 || py < 0 || px >= w || py >= h) continue;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist > radius) continue;
                double a = Profile(dist / radius, brush.Hardness) * brush.Opacity * brush.Flow;
                if (mask != null) a *= mask.GetAt(px, py) / 255.0;
                if (a <= 0.001) continue;

                int o = (py * w + px) * 4;
                byte da = ts[o + 3];
                if (erase)
                {
                    double na = da / 255.0 * (1 - a);
                    double f = (1 - a);
                    ts[o] = (byte)(ts[o] * f);
                    ts[o + 1] = (byte)(ts[o + 1] * f);
                    ts[o + 2] = (byte)(ts[o + 2] * f);
                    ts[o + 3] = (byte)Math.Round(na * 255);
                }
                else
                {
                    double as_ = a;
                    double ad = da / 255.0;
                    double sr = color.Red * as_, sg = color.Green * as_, sb = color.Blue * as_;
                    ts[o] = (byte)Math.Round(sr + ts[o] * (1 - as_));
                    ts[o + 1] = (byte)Math.Round(sg + ts[o + 1] * (1 - as_));
                    ts[o + 2] = (byte)Math.Round(sb + ts[o + 2] * (1 - as_));
                    ts[o + 3] = (byte)Math.Round((as_ + ad * (1 - as_)) * 255);
                }
            }
        }
    }

    /// <summary>Paint a stroke between two points honouring brush spacing.</summary>
    public static (double lastX, double lastY) Stroke(SKBitmap target, Brush brush,
        double x0, double y0, double x1, double y1,
        SKColor color, bool erase, ImageMask? mask)
    {
        double spacing = Math.Max(0.5, brush.Size * brush.Spacing);
        double dx = x1 - x0, dy = y1 - y0;
        double dist = Math.Sqrt(dx * dx + dy * dy);
        if (dist < 1e-3) { Stamp(target, brush, x1, y1, color, erase, mask); return (x1, y1); }
        int steps = (int)Math.Floor(dist / spacing);
        for (int i = 1; i <= steps; i++)
        {
            double t = (i * spacing) / dist;
            Stamp(target, brush, x0 + dx * t, y0 + dy * t, color, erase, mask);
        }
        Stamp(target, brush, x1, y1, color, erase, mask);
        return (x1, y1);
    }

    /// <summary>Clone-stamp: copy from <paramref name="source"/> at (srcX,srcY).</summary>
    public static void StampClone(SKBitmap target, SKBitmap source, double x, double y,
        double srcX, double srcY, Brush brush, double alpha, ImageMask? mask)
    {
        if (target == null || source == null) return;
        double radius = Math.Max(0.5, brush.Size / 2.0);
        int ix = (int)Math.Round(x), iy = (int)Math.Round(y);
        int isx = (int)Math.Round(srcX), isy = (int)Math.Round(srcY);
        int r = (int)Math.Ceiling(radius);
        int w = target.Width, h = target.Height;
        IntPtr tp = target.GetPixels(), sp = source.GetPixels();
        if (tp == IntPtr.Zero || sp == IntPtr.Zero) return;
        int sw = source.Width, sh = source.Height;

        unsafe
        {
            var ts = new Span<byte>(tp.ToPointer(), w * h * 4);
            var ss = new Span<byte>(sp.ToPointer(), sw * sh * 4);
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                int px = ix + dx, py = iy + dy;
                int qx = isx + dx, qy = isy + dy;
                if (px < 0 || py < 0 || px >= w || py >= h) continue;
                if (qx < 0 || qy < 0 || qx >= sw || qy >= sh) continue;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist > radius) continue;
                double a = Profile(dist / radius, brush.Hardness) * brush.Opacity * brush.Flow * alpha;
                if (mask != null) a *= mask.GetAt(px, py) / 255.0;
                if (a <= 0.001) continue;

                int so = (qy * sw + qx) * 4;
                byte sa = ss[so + 3];
                double dr, dg, db, da;
                if (sa == 0) { dr = dg = db = da = 0; }
                else if (sa == 255) { dr = ss[so]; dg = ss[so + 1]; db = ss[so + 2]; da = 1; }
                else { double inv = 255.0 / sa; dr = ss[so] * inv; dg = ss[so + 1] * inv; db = ss[so + 2] * inv; da = sa / 255.0; }

                int o = (py * w + px) * 4;
                byte tda = ts[o + 3];
                double as_ = a * da;
                double ad = tda / 255.0;
                ts[o] = (byte)Math.Round(dr * as_ + ts[o] * (1 - as_));
                ts[o + 1] = (byte)Math.Round(dg * as_ + ts[o + 1] * (1 - as_));
                ts[o + 2] = (byte)Math.Round(db * as_ + ts[o + 2] * (1 - as_));
                ts[o + 3] = (byte)Math.Round((as_ + ad * (1 - as_)) * 255);
            }
        }
    }

    private static double Profile(double t, double hardness)
    {
        // t in 0..1 (edge distance / radius)
        if (t <= 0) return 1;
        if (t >= 1) return 0;
        double h = Math.Clamp(hardness, 0, 1);
        if (t <= h) return 1;
        return 1 - (t - h) / (1 - h);
    }
}

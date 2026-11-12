using SkiaSharp;
using System.Globalization;
using MirrorStudio.Image.Models;

namespace MirrorStudio.Image.Adjustments;

/// <summary>
/// Applies non-destructive tone/colour adjustments to an <see cref="SKBitmap"/>
/// in place. All adjustments operate in straight (un-premultiplied) alpha and
/// re-premultiply afterwards so transparent pixels stay correct.
/// </summary>
public static class AdjustmentProcessor
{
    public static void ApplyAll(SKBitmap bmp, IEnumerable<Adjustment> adjustments)
    {
        foreach (var adj in adjustments)
            Apply(bmp, adj);
    }

    public static void Apply(SKBitmap bmp, Adjustment adj)
    {
        if (bmp == null || bmp.Width == 0) return;
        int w = bmp.Width, h = bmp.Height;
        IntPtr px = bmp.GetPixels();
        if (px == IntPtr.Zero) return;

        unsafe
        {
            var span = new Span<byte>(px.ToPointer(), w * h * 4);
            switch (adj.Type)
            {
                case Adjustment.Brightness: ApplyBrightness(span, adj.Get("Amount", 0)); break;
                case Adjustment.Contrast: ApplyContrast(span, adj.Get("Amount", 0)); break;
                case Adjustment.Exposure: ApplyExposure(span, adj.Get("Stops", 0)); break;
                case Adjustment.Saturation: ApplySaturation(span, adj.Get("Amount", 0)); break;
                case Adjustment.Vibrance: ApplyVibrance(span, adj.Get("Amount", 0)); break;
                case Adjustment.Temperature: ApplyTemperature(span, adj.Get("Amount", 0)); break;
                case Adjustment.Tint: ApplyTint(span, adj.Get("Amount", 0)); break;
                case Adjustment.WhiteBalance: ApplyWhiteBalance(span, adj.Get("Temperature", 0), adj.Get("Tint", 0)); break;
                case Adjustment.Highlights: ApplyTonal(span, adj.Get("Amount", 0), true); break;
                case Adjustment.Shadows: ApplyTonal(span, adj.Get("Amount", 0), false); break;
                case Adjustment.Gamma: ApplyGamma(span, adj.Get("Value", 1)); break;
                case Adjustment.Invert: ApplyInvert(span); break;
                case Adjustment.Grayscale: ApplyGrayscale(span); break;
                case Adjustment.Sepia: ApplySepia(span); break;
                case Adjustment.Levels: ApplyLevels(span, adj); break;
                case Adjustment.Curves: ApplyCurves(span, adj); break;
                case Adjustment.HSL: ApplyHsl(span, adj); break;
                case Adjustment.ColorBalance: ApplyColorBalance(span, adj); break;
            }
        }
    }

    // ---------- individual adjustments ----------

    private static void ApplyBrightness(Span<byte> s, double amount)
    {
        double d = amount * 255;
        ForEach(s, (r, g, b) =>
        {
            r = Clamp(r + d); g = Clamp(g + d); b = Clamp(b + d);
            return (r, g, b);
        });
    }

    private static void ApplyContrast(Span<byte> s, double amount)
    {
        double c = amount * 255;
        double f = (259 * (c + 255)) / (255 * (259 - c));
        ForEach(s, (r, g, b) =>
        {
            r = Clamp((r - 128) * f + 128);
            g = Clamp((g - 128) * f + 128);
            b = Clamp((b - 128) * f + 128);
            return (r, g, b);
        });
    }

    private static void ApplyExposure(Span<byte> s, double stops)
    {
        double f = Math.Pow(2, stops);
        ForEach(s, (r, g, b) =>
        {
            r = Clamp(r * f); g = Clamp(g * f); b = Clamp(b * f);
            return (r, g, b);
        });
    }

    private static void ApplySaturation(Span<byte> s, double amount)
    {
        double sat = 1 + amount; // -1..1 -> 0..2
        ForEach(s, (r, g, b) =>
        {
            double lum = Luma(r, g, b);
            r = Clamp(lum + (r - lum) * sat);
            g = Clamp(lum + (g - lum) * sat);
            b = Clamp(lum + (b - lum) * sat);
            return (r, g, b);
        });
    }

    private static void ApplyVibrance(Span<byte> s, double amount)
    {
        ForEach(s, (r, g, b) =>
        {
            double lum = Luma(r, g, b);
            double mx = Math.Max(r, Math.Max(g, b));
            double mn = Math.Min(r, Math.Min(g, b));
            double sat = mx <= mn ? 0 : (mx - lum) / (mx - mn);
            double boost = 1 + amount * (1 - sat);
            r = Clamp(lum + (r - lum) * boost);
            g = Clamp(lum + (g - lum) * boost);
            b = Clamp(lum + (b - lum) * boost);
            return (r, g, b);
        });
    }

    private static void ApplyTemperature(Span<byte> s, double amount)
    {
        double d = amount * 30;
        ForEach(s, (r, g, b) =>
        {
            r = Clamp(r + d);
            b = Clamp(b - d);
            return (r, g, b);
        });
    }

    private static void ApplyTint(Span<byte> s, double amount)
    {
        double d = amount * 18;
        ForEach(s, (r, g, b) =>
        {
            g = Clamp(g + d);
            r = Clamp(r - d * 0.4);
            b = Clamp(b - d * 0.4);
            return (r, g, b);
        });
    }

    private static void ApplyWhiteBalance(Span<byte> s, double temp, double tint) =>
        ApplyTint(ApplyTemperatureSpan(s, temp), tint);

    private static Span<byte> ApplyTemperatureSpan(Span<byte> s, double amount)
    {
        double d = amount * 30;
        ForEach(s, (r, g, b) =>
        {
            r = Clamp(r + d); b = Clamp(b - d);
            return (r, g, b);
        });
        return s;
    }

    private static void ApplyTonal(Span<byte> s, double amount, bool highlights)
    {
        double strength = amount * 0.6 * 255;
        ForEach(s, (r, g, b) =>
        {
            double lum = Luma(r, g, b) / 255.0;
            double w = highlights ? lum * lum : (1 - lum) * (1 - lum);
            double d = strength * w;
            r = Clamp(r + d); g = Clamp(g + d); b = Clamp(b + d);
            return (r, g, b);
        });
    }

    private static void ApplyGamma(Span<byte> s, double value)
    {
        double inv = 1.0 / Math.Max(0.01, value);
        ForEach(s, (r, g, b) =>
        {
            r = Clamp(255 * Math.Pow(r / 255.0, inv));
            g = Clamp(255 * Math.Pow(g / 255.0, inv));
            b = Clamp(255 * Math.Pow(b / 255.0, inv));
            return (r, g, b);
        });
    }

    private static void ApplyInvert(Span<byte> s)
    {
        ForEach(s, (r, g, b) =>
        {
            r = (byte)(255 - r); g = (byte)(255 - g); b = (byte)(255 - b);
            return (r, g, b);
        });
    }

    private static void ApplyGrayscale(Span<byte> s)
    {
        ForEach(s, (r, g, b) =>
        {
            byte lum = (byte)Luma(r, g, b);
            return (lum, lum, lum);
        });
    }

    private static void ApplySepia(Span<byte> s)
    {
        ForEach(s, (r, g, b) =>
        {
            double nr = 0.393 * r + 0.769 * g + 0.189 * b;
            double ng = 0.349 * r + 0.686 * g + 0.168 * b;
            double nb = 0.272 * r + 0.534 * g + 0.131 * b;
            return (Clamp(nr), Clamp(ng), Clamp(nb));
        });
    }

    private static void ApplyLevels(Span<byte> s, Adjustment adj)
    {
        double black = adj.Get("BlackPoint", 0);
        double white = adj.Get("WhitePoint", 255);
        double gamma = adj.Get("Gamma", 1);
        double ob = adj.Get("OutputBlack", 0);
        double ow = adj.Get("OutputWhite", 255);
        double span = Math.Max(1, white - black);
        double inv = 1.0 / Math.Max(0.01, gamma);
        ForEach(s, (r, g, b) =>
        {
            r = Level(r, black, span, inv, ob, ow);
            g = Level(g, black, span, inv, ob, ow);
            b = Level(b, black, span, inv, ob, ow);
            return (r, g, b);
        });
    }

    private static byte Level(double v, double black, double span, double inv, double ob, double ow)
    {
        double n = (v - black) / span;
        n = Math.Pow(Clamp01(n), inv);
        return (byte)Clamp(ob + n * (ow - ob));
    }

    private static void ApplyCurves(Span<byte> s, Adjustment adj)
    {
        // four control stops (0..1) -> monotonic LUT
        double[] stops = { adj.Get("Shadows", 0), adj.Get("Darks", 0), adj.Get("Lights", 0), adj.Get("Highlights", 0) };
        byte[] lut = BuildCurveLut(stops);
        ForEach(s, (r, g, b) =>
        {
            r = lut[r]; g = lut[g]; b = lut[b];
            return (r, g, b);
        });
    }

    private static byte[] BuildCurveLut(double[] stops)
    {
        // baseline + stops as additive deltas at 0, 0.33, 0.66, 1.0
        double[] x = { 0, 0.33, 0.66, 1.0 };
        double[] y = new double[4];
        for (int i = 0; i < 4; i++) y[i] = Clamp01(x[i] + stops[i] * 0.5);
        var lut = new byte[256];
        for (int i = 0; i < 256; i++)
        {
            double t = i / 255.0;
            int k = 0;
            while (k < 3 && t > x[k + 1]) k++;
            double span = x[k + 1] - x[k];
            double f = span <= 0 ? 0 : (t - x[k]) / span;
            double val = y[k] + (y[k + 1] - y[k]) * f;
            lut[i] = (byte)Clamp(val * 255);
        }
        return lut;
    }

    private static void ApplyHsl(Span<byte> s, Adjustment adj)
    {
        double hue = adj.Get("Hue", 0) / 360.0;       // -0.5..0.5
        double sat = 1 + adj.Get("Saturation", 0);    // -1..1 -> 0..2
        double light = adj.Get("Lightness", 0);        // -1..1
        int range = (int)adj.Get("Range", 0);          // 0 all,1 shadows,2 mids,3 highlights
        ForEach(s, (r, g, b) =>
        {
            double lum = Luma(r, g, b) / 255.0;
            double w = range == 0 ? 1 : range == 1 ? (1 - lum) * (1 - lum)
                : range == 2 ? 4 * lum * (1 - lum) : lum * lum;
            (double h, double ss, double l) = RgbToHsl(r, g, b);
            h += hue * w; if (h < 0) h += 1; if (h > 1) h -= 1;
            ss = Clamp01(ss * (1 + (sat - 1) * w));
            l = Clamp01(l + light * 0.5 * w);
            (r, g, b) = HslToRgb(h, ss, l);
            return (r, g, b);
        });
    }

    private static void ApplyColorBalance(Span<byte> s, Adjustment adj)
    {
        ForEach(s, (r, g, b) =>
        {
            double lum = Luma(r, g, b) / 255.0;
            double wS = (1 - lum) * (1 - lum);
            double wM = 4 * lum * (1 - lum);
            double wH = lum * lum;
            ApplyRange(ref r, ref g, ref b, adj.Get("ShadowCyan", 0), adj.Get("ShadowMagenta", 0), adj.Get("ShadowYellow", 0), wS);
            ApplyRange(ref r, ref g, ref b, adj.Get("MidCyan", 0), adj.Get("MidMagenta", 0), adj.Get("MidYellow", 0), wM);
            ApplyRange(ref r, ref g, ref b, adj.Get("HighCyan", 0), adj.Get("HighMagenta", 0), adj.Get("HighYellow", 0), wH);
            return (r, g, b);
        });
    }

    private static void ApplyRange(ref byte r, ref byte g, ref byte b, double cyan, double magenta, double yellow, double w)
    {
        // cyan-red: +cyan lowers red; magenta-green: +magenta lowers green; yellow-blue: +yellow lowers blue
        r = Clamp(r - cyan * 30 * w);
        g = Clamp(g - magenta * 30 * w);
        b = Clamp(b - yellow * 30 * w);
    }

    // ---------- pixel loop plumbing ----------

    private delegate (byte r, byte g, byte b) PixelFn(byte r, byte g, byte b);

    private static void ForEach(Span<byte> s, PixelFn fn)
    {
        for (int i = 0; i < s.Length; i += 4)
        {
            byte a = s[i + 3];
            if (a == 0) continue;
            byte r = s[i], g = s[i + 1], b = s[i + 2];
            if (a != 255) { r = (byte)(r * 255 / a); g = (byte)(g * 255 / a); b = (byte)(b * 255 / a); }
            var res = fn(r, g, b);
            r = res.r; g = res.g; b = res.b;
            if (a != 255) { r = (byte)(r * a / 255); g = (byte)(g * a / 255); b = (byte)(b * a / 255); }
            s[i] = r; s[i + 1] = g; s[i + 2] = b;
        }
    }

    // ---------- colour helpers ----------

    private static double Luma(double r, double g, double b) =>
        0.2126 * r + 0.7152 * g + 0.0722 * b;

    private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
    private static byte Clamp(double v) => v < 0 ? (byte)0 : v > 255 ? (byte)255 : (byte)Math.Round(v);

    private static (double h, double s, double l) RgbToHsl(byte r, byte g, byte b)
    {
        double rn = r / 255.0, gn = g / 255.0, bn = b / 255.0;
        double mx = Math.Max(rn, Math.Max(gn, bn)), mn = Math.Min(rn, Math.Min(gn, bn));
        double l = (mx + mn) / 2;
        double h = 0, s = 0;
        double d = mx - mn;
        if (d > 1e-6)
        {
            s = l > 0.5 ? d / (2 - mx - mn) : d / (mx + mn);
            if (mx == rn) h = (gn - bn) / d + (gn < bn ? 6 : 0);
            else if (mx == gn) h = (bn - rn) / d + 2;
            else h = (rn - gn) / d + 4;
            h /= 6;
        }
        return (h, s, l);
    }

    private static (byte r, byte g, byte b) HslToRgb(double h, double s, double l)
    {
        if (s <= 1e-6) { byte v = (byte)Math.Round(l * 255); return (v, v, v); }
        double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        double p = 2 * l - q;
        double r = HueToRgb(p, q, h + 1.0 / 3);
        double g = HueToRgb(p, q, h);
        double b = HueToRgb(p, q, h - 1.0 / 3);
        return ((byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255));
    }

    private static double HueToRgb(double p, double q, double t)
    {
        if (t < 0) t += 1; if (t > 1) t -= 1;
        if (t < 1.0 / 6) return p + (q - p) * 6 * t;
        if (t < 1.0 / 2) return q;
        if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
        return p;
    }
}

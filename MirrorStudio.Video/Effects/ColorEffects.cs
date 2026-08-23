using System.Globalization;

namespace MirrorStudio.Video.Effects;

internal static class FN
{
    public static string N(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);
}

/// <summary>
/// Primary color panel (spec M10 §7/§8): exposure/brightness, contrast,
/// highlights/shadows (via eq's gamma_r-ish approximation), saturation,
/// temperature and tint — mapped onto ffmpeg eq + colortemperature.
/// </summary>
public class ColorCorrectionEffect : VideoEffect
{
    /// <summary>-1..1 (ffmpeg brightness range).</summary>
    public double Brightness { get; set; }

    public double Contrast { get; set; } = 1;

    public double Saturation { get; set; } = 1;

    public double Gamma { get; set; } = 1;

    /// <summary>Highlight recovery -1..1 → contrast-side gamma lift.</summary>
    public double Highlights { get; set; }

    /// <summary>Shadow lift -1..1 → low-end gamma.</summary>
    public double Shadows { get; set; }

    /// <summary>Kelvin bias -100 (cool) .. +100 (warm) mapped to 3500–8500K.</summary>
    public double Temperature { get; set; }

    /// <summary>Magenta↔green axis -100..100.</summary>
    public double Tint { get; set; }

    public ColorCorrectionEffect()
    {
        Name = "Color Correction";
    }

    public bool IsNeutral =>
        Math.Abs(Brightness) < 0.001 && Math.Abs(Contrast - 1) < 0.001 &&
        Math.Abs(Saturation - 1) < 0.001 && Math.Abs(Gamma - 1) < 0.001 &&
        Math.Abs(Highlights) < 0.001 && Math.Abs(Shadows) < 0.001 &&
        Math.Abs(Temperature) < 0.01 && Math.Abs(Tint) < 0.01;

    public override string? BuildFfmpegFilter()
    {
        if (IsNeutral) return null;

        var parts = new List<string>();

        // shadows/highlights fold into an overall gamma nudge
        double effectiveGamma = Gamma * (1.0 - Shadows * 0.35) * (1.0 + Highlights * 0.25);

        parts.Add(
            $"eq=brightness={FN.N(Brightness)}:contrast={FN.N(Math.Max(0.1, Contrast))}" +
            $":saturation={FN.N(Math.Max(0.0, Saturation))}:gamma={FN.N(effectiveGamma)}");

        if (Math.Abs(Temperature) > 0.01)
        {
            double kelvin = 6000.0 + Temperature * 25.0; // ±100 → 3500–8500 K
            parts.Add($"colortemperature=temperature={kelvin.ToString("0", CultureInfo.InvariantCulture)}");
        }

        if (Math.Abs(Tint) > 0.01)
            parts.Add($"hue=h={FN.N(Tint * 0.6)}"); // small rotation on the magenta-green axis

        return string.Join(",", parts);
    }
}

/// <summary>Gaussian blur (gblur keeps edges cleaner than boxblur at high radii).</summary>
public class BlurEffect : VideoEffect
{
    /// <summary>Blur radius in pixels.</summary>
    public double Amount { get; set; } = 5;

    public BlurEffect()
    {
        Name = "Blur";
    }

    public override string? BuildFfmpegFilter() =>
        Amount < 0.05 ? null : $"gblur=sigma={FN.N(Amount)}";
}

/// <summary>Unsharp-mask sharpening (spec M10 §12).</summary>
public class SharpenEffect : VideoEffect
{
    /// <summary>0 = off, typical range 0.2–2.</summary>
    public double Amount { get; set; } = 1;

    public SharpenEffect()
    {
        Name = "Sharpen";
    }

    public override string? BuildFfmpegFilter() =>
        Math.Abs(Amount) < 0.01 ? null : $"unsharp=5:5:{FN.N(Amount)}:5:5:0";
}

/// <summary>Soft glow bloom.</summary>
public class GlowEffect : VideoEffect
{
    public double Amount { get; set; } = 0.4;

    public GlowEffect()
    {
        Name = "Glow";
    }

    public override string? BuildFfmpegFilter()
    {
        if (Amount < 0.01) return null;

        // unique labels: several glow instances can live in one graph
        string id = FX.Next();
        return
            $"split[g{id}a][g{id}b];[g{id}b]gblur=sigma={FN.N(8 + Amount * 20)},eq=brightness={FN.N(Amount * 0.35)}[g{id}bl];" +
            $"[g{id}a][g{id}bl]blend=all_mode=screen:all_opacity={FN.N(Math.Clamp(Amount, 0, 1))}";
    }
}

/// <summary>Vignette darkening of frame edges.</summary>
public class VignetteEffect : VideoEffect
{
    /// <summary>0 = subtle … 1 = heavy.</summary>
    public double Amount { get; set; } = 0.5;

    public VignetteEffect()
    {
        Name = "Vignette";
    }

    public override string? BuildFfmpegFilter()
    {
        // ffmpeg vignette angle: PI/5 default; larger angle = stronger falloff
        double angle = Math.PI / 5.0 + Math.Clamp(Amount, 0, 1) * (Math.PI / 3.6);
        return Amount < 0.01 ? null : $"vignette=angle={FN.N(angle)}";
    }
}

/// <summary>Film-grain / noise overlay.</summary>
public class NoiseEffect : VideoEffect
{
    /// <summary>Noise strength 0..100.</summary>
    public double Amount { get; set; } = 10;

    public NoiseEffect()
    {
        Name = "Noise";
    }

    public override string? BuildFfmpegFilter() =>
        Amount < 0.5 ? null : $"noise=alls={FN.N(Amount)}:allf=t+u";
}

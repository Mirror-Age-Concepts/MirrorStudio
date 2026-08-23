using System.Globalization;

namespace MirrorStudio.Video.Effects;

internal static class FK
{
    public static string N(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);
}

/// <summary>Green-screen / color keying (spec M10 §13): key → transparent → composite.</summary>
public class ChromaKeyEffect : VideoEffect
{
    /// <summary>Key color as 0xRRGGBB (ffmpeg chromakey format).</summary>
    public string KeyColor { get; set; } = "0x00FF00";

    /// <summary>Hue sphere radius for matching (0.01..0.99).</summary>
    public double Similarity { get; set; } = 0.25;

    /// <summary>Edge softness of the key.</summary>
    public double Smoothness { get; set; } = 0.08;

    /// <summary>Green-spill suppression on remaining pixels (despill filter strength).</summary>
    public double SpillReduction { get; set; } = 0.3;

    public ChromaKeyEffect()
    {
        Name = "Chroma Key";
    }

    public override string? BuildFfmpegFilter()
    {
        var parts = new List<string>
        {
            $"chromakey={KeyColor}:{FK.N(Clamp01(Similarity))}:{FK.N(Clamp01(Smoothness))}"
        };

        if (SpillReduction > 0.01)
            parts.Add($"despill=type=green:mix={FK.N(Clamp01(SpillReduction))}");

        return string.Join(",", parts);
    }

    private static double Clamp01(double v) => Math.Clamp(v, 0.001, 0.999);
}

/// <summary>Luma-range keying (night/day mattes, sky replacement input).</summary>
public class LumaKeyEffect : VideoEffect
{
    public double Threshold { get; set; } = 0.5;

    public LumaKeyEffect()
    {
        Name = "Luma Key";
    }

    public override string? BuildFfmpegFilter() =>
        $"lumakey=threshold={FK.N(Math.Clamp(Threshold, 0, 1))}:tolerance=0.08:softness=0.05";
}

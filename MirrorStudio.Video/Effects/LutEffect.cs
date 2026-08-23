using System.Globalization;

namespace MirrorStudio.Video.Effects;

internal static class FL
{
    public static string N(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);
}

/// <summary>Applies a .cube 3D LUT via ffmpeg's lut3d (spec M10 §10).</summary>
public class LutEffect : VideoEffect
{
    private readonly Color.LutLoader _loader = new();

    public string FilePath { get; set; } = "";

    /// <summary>Mix of the graded image over the original (0 = off, 1 = full grade).</summary>
    public double Intensity { get; set; } = 1.0;

    public LutEffect()
    {
        Name = "LUT";
    }

    public override string? BuildFfmpegFilter()
    {
        if (string.IsNullOrWhiteSpace(FilePath) || !File.Exists(FilePath) || Intensity <= 0.001)
            return null;

        // validate through the loader so a broken .cube fails here, not mid-render
        if (!_loader.TryLoad(FilePath, out _))
            return null;

        string quoted = $"\"{FilePath.Replace("\\", "/")}\"";

        // intensity < 1 blends the graded result back over the original
        if (Intensity >= 0.999)
            return $"lut3d=file={quoted}";

        string id = FX.Next();
        return
            $"split[l{id}o][l{id}g];[l{id}g]lut3d=file={quoted}[l{id}d];" +
            $"[l{id}o][l{id}d]blend=all_mode=normal:all_opacity={FL.N(Math.Clamp(Intensity, 0, 1))}";
    }
}

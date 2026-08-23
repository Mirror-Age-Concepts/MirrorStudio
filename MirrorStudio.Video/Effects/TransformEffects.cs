using System.Globalization;

namespace MirrorStudio.Video.Effects;

internal static class F
{
    public static string N(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);
}

/// <summary>Position / scale / rotation (spec M10 §3). Percent-based like the inspector.</summary>
public class TransformEffect : VideoEffect
{
    /// <summary>Pixel offset from center; 0/0 = centered.</summary>
    public double PositionX { get; set; }

    public double PositionY { get; set; }

    /// <summary>1.0 = untouched.</summary>
    public double ScaleX { get; set; } = 1;

    public double ScaleY { get; set; } = 1;

    /// <summary>Degrees clockwise.</summary>
    public double Rotation { get; set; }

    public TransformEffect()
    {
        Name = "Transform";
    }

    public override string? BuildFfmpegFilter()
    {
        var parts = new List<string>();

        bool scaled = Math.Abs(ScaleX - 1) > 0.001 || Math.Abs(ScaleY - 1) > 0.001;
        if (scaled)
            parts.Add($"scale=trunc(iw*{F.N(ScaleX)}/2)*2:trunc(ih*{F.N(ScaleY)}/2)*2");

        // rotate fills exposed corners with black (c=none needs overlay compositing)
        if (Math.Abs(Rotation) > 0.05)
            parts.Add($"rotate={F.N(Rotation * Math.PI / 180.0)}:c=black");

        return parts.Count == 0 ? null : string.Join(",", parts);
    }
}

/// <summary>Crop a source rectangle (spec M10 §5).</summary>
public class CropEffect : VideoEffect
{
    public int X { get; set; }

    public int Y { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    public CropEffect()
    {
        Name = "Crop";
    }

    public override string BuildFfmpegFilter() =>
        Width <= 0 || Height <= 0
            ? null
            : $"crop={Width}:{Height}:{X}:{Y}";
}

/// <summary>Semi-transparency for overlays and dissolves (spec M10 §6).</summary>
public class OpacityEffect : VideoEffect
{
    /// <summary>1 = fully opaque, 0 = invisible.</summary>
    public double Opacity { get; set; } = 1;

    public OpacityEffect()
    {
        Name = "Opacity";
    }

    public override string? BuildFfmpegFilter() =>
        Math.Abs(Opacity - 1) < 0.001
            ? null
            : $"format=rgba,colorchannelmixer=aa={F.N(Math.Clamp(Opacity, 0, 1))}";
}

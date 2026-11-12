namespace MirrorStudio.Graphics;

using MirrorStudio.Graphics.Models;

/// <summary>Named entrance/exit presets (Milestone 11 §8).</summary>
public static class AnimationPresets
{
    public static readonly string[] Entrance =
    {
        "", "Fade In", "Slide In Left", "Slide In Right", "Slide In Up",
        "Slide In Down", "Scale In", "Pop In", "Typewriter"
    };

    public static readonly string[] Exit =
    {
        "", "Fade Out", "Slide Out Left", "Slide Out Right", "Slide Out Up",
        "Slide Out Down", "Scale Out"
    };
}

/// <summary>A single animated property sample with an easing curve.</summary>
public class MotionKeyframe
{
    /// <summary>Seconds relative to the layer start.</summary>
    public double Time { get; set; }

    /// <summary>PositionX | PositionY | ScaleX | ScaleY | Rotation | Opacity.</summary>
    public string Property { get; set; } = "Opacity";

    public double Value { get; set; }

    /// <summary>Linear | EaseIn | EaseOut | EaseInOut.</summary>
    public string Easing { get; set; } = "EaseInOut";
}

/// <summary>Snapshot of a layer's transform at a point in time — the unit both
/// the GPU preview and the FFmpeg render plan consume.</summary>
public class GraphicRenderState
{
    public double X { get; set; }
    public double Y { get; set; }
    public double ScaleX { get; set; } = 1;
    public double ScaleY { get; set; } = 1;
    public double Rotation { get; set; }
    public double Opacity { get; set; } = 1;
    /// <summary>0..1 fraction of characters revealed (Typewriter preset).</summary>
    public double RevealFraction { get; set; } = 1;

    public static GraphicRenderState From(GraphicLayer layer) => new()
    {
        X = layer.PositionX,
        Y = layer.PositionY,
        ScaleX = layer.ScaleX,
        ScaleY = layer.ScaleY,
        Rotation = layer.Rotation,
        Opacity = layer.Opacity
    };
}

public static class TextAnimator
{
    private static double Ease(string name, double t) => name switch
    {
        "Linear" => t,
        "EaseIn" => t * t,
        "EaseOut" => 1 - (1 - t) * (1 - t),
        _ => t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2   // EaseInOut
    };

    /// <summary>Keyframed value at local time, or null when no keyframes drive it.</summary>
    public static double? KeyframeValue(GraphicLayer layer, string property, double localTime)
    {
        var kfs = layer.Keyframes
            .Where(k => k.Property.Equals(property, StringComparison.OrdinalIgnoreCase))
            .OrderBy(k => k.Time).ToArray();
        if (kfs.Length == 0) return null;
        if (localTime <= kfs[0].Time) return kfs[0].Value;
        if (localTime >= kfs[^1].Time) return kfs[^1].Value;

        for (int i = 0; i < kfs.Length - 1; i++)
        {
            var a = kfs[i];
            var b = kfs[i + 1];
            if (localTime >= a.Time && localTime <= b.Time)
            {
                double span = b.Time - a.Time;
                double t = span <= 0 ? 1 : (localTime - a.Time) / span;
                return a.Value + (b.Value - a.Value) * Ease(a.Easing, t);
            }
        }
        return kfs[^1].Value;
    }

    /// <summary>Compose base values, keyframes and entrance/exit animation into a state.</summary>
    public static GraphicRenderState Evaluate(GraphicLayer layer, double timelineSeconds)
    {
        double local = timelineSeconds - layer.Start;
        var s = GraphicRenderState.From(layer);

        ApplyKeyframe(layer, "PositionX", local, v => s.X = v);
        ApplyKeyframe(layer, "PositionY", local, v => s.Y = v);
        ApplyKeyframe(layer, "ScaleX", local, v => s.ScaleX = v);
        ApplyKeyframe(layer, "ScaleY", local, v => s.ScaleY = v);
        ApplyKeyframe(layer, "Rotation", local, v => s.Rotation = v);
        ApplyKeyframe(layer, "Opacity", local, v => s.Opacity = v);

        double inDur = Math.Max(0, layer.AnimationInDuration);
        if (!string.IsNullOrEmpty(layer.AnimationIn) && inDur > 0.001 && local < inDur)
            ApplyEntrance(layer.AnimationIn, Math.Clamp(local / inDur, 0, 1), s);

        double outStart = layer.Duration - Math.Max(0, layer.AnimationOutDuration);
        if (!string.IsNullOrEmpty(layer.AnimationOut)
            && layer.AnimationOutDuration > 0.001 && local > outStart)
        {
            double prog = Math.Clamp((local - outStart) / layer.AnimationOutDuration, 0, 1);
            ApplyExit(layer.AnimationOut, prog, s);
        }

        s.Opacity = Math.Clamp(s.Opacity, 0, 1);
        return s;
    }

    private static void ApplyKeyframe(GraphicLayer layer, string prop, double local, Action<double> set)
    {
        var v = KeyframeValue(layer, prop, local);
        if (v.HasValue) set(v.Value);
    }

    private static void ApplyEntrance(string preset, double p, GraphicRenderState s)
    {
        switch (preset)
        {
            case "Fade In": s.Opacity *= p; break;
            case "Slide In Left": s.Opacity *= p; s.X -= (1 - p) * 400; break;
            case "Slide In Right": s.Opacity *= p; s.X += (1 - p) * 400; break;
            case "Slide In Up": s.Opacity *= p; s.Y += (1 - p) * 300; break;
            case "Slide In Down": s.Opacity *= p; s.Y -= (1 - p) * 300; break;
            case "Scale In": s.ScaleX *= 0.2 + 0.8 * p; s.ScaleY *= 0.2 + 0.8 * p; s.Opacity *= p; break;
            case "Pop In":
                double overshoot = p < 0.7 ? p / 0.7 * 1.12 : 1.12 - 0.12 * ((p - 0.7) / 0.3);
                s.ScaleX *= overshoot; s.ScaleY *= overshoot; s.Opacity *= Math.Min(1, p * 2); break;
            case "Typewriter": s.RevealFraction = p; break;
        }
    }

    private static void ApplyExit(string preset, double p, GraphicRenderState s)
    {
        switch (preset)
        {
            case "Fade Out": s.Opacity *= 1 - p; break;
            case "Slide Out Left": s.Opacity *= 1 - p; s.X -= p * 400; break;
            case "Slide Out Right": s.Opacity *= 1 - p; s.X += p * 400; break;
            case "Slide Out Up": s.Opacity *= 1 - p; s.Y -= p * 300; break;
            case "Slide Out Down": s.Opacity *= 1 - p; s.Y += p * 300; break;
            case "Scale Out": s.ScaleX *= 1 - 0.8 * p; s.ScaleY *= 1 - 0.8 * p; s.Opacity *= 1 - p; break;
        }
    }
}

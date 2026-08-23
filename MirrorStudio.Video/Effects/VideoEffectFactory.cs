namespace MirrorStudio.Video.Effects;

/// <summary>Unique label generator so multi-stage filters can coexist in one graph.</summary>
internal static class FX
{
    private static int _n;

    public static string Next() => "fx" + Interlocked.Increment(ref _n);

    public static string N(double v) => v.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// Creates <see cref="VideoEffect"/> instances from serialized type names
/// (spec M10 §28). <paramref name="amount"/> is already in effect units
/// (blur sigma, unsharp multiplier, 0..1 opacity/keys) — legacy 0..100
/// slider values are converted by the project mapper before they get here.
/// </summary>
public static class VideoEffectFactory
{
    public static readonly string[] Catalog =
    [
        "Transform", "Crop", "ColorCorrection", "Lut",
        "Blur", "Sharpen", "Glow", "Vignette", "Noise",
        "ChromaKey", "LumaKey", "Opacity", "Invert", "NoiseReduction",
    ];

    public static VideoEffect? Create(
        string type,
        double amount = 0,
        IReadOnlyDictionary<string, string>? settings = null)
    {
        VideoEffect? effect = type switch
        {
            "Crop" => new CropEffect(),
            "Transform" => new TransformEffect(),
            "ColorCorrection" => new ColorCorrectionEffect { Brightness = amount },
            "Blur" => new BlurEffect { Amount = amount },
            "Sharpen" => new SharpenEffect { Amount = amount },
            "Lut" => new LutEffect { Intensity = amount > 0 ? Math.Clamp(amount, 0, 1) : 1.0 },
            "ChromaKey" => new ChromaKeyEffect
            {
                Similarity = amount > 0 ? Math.Clamp(amount, 0.01, 0.99) : 0.25,
            },
            "LumaKey" => new LumaKeyEffect
            {
                Threshold = amount > 0 ? Math.Clamp(amount, 0, 1) : 0.5,
            },
            "Glow" => new GlowEffect { Amount = amount },
            "Vignette" => new VignetteEffect { Amount = amount },
            "Noise" => new NoiseEffect { Amount = amount },
            "Opacity" => new OpacityEffect
            {
                Opacity = amount > 0 ? Math.Clamp(amount, 0, 1) : 1.0,
            },
            "Invert" => new InvertEffect(),
            "NoiseReduction" => new VideoNoiseReductionEffect { Strength = amount },
            _ => null,
        };

        ApplySettings(effect, settings);
        return effect;
    }

    private static void ApplySettings(VideoEffect? effect, IReadOnlyDictionary<string, string>? s)
    {
        if (effect is null || s is null || s.Count == 0) return;

        string G(string key, string fallback) =>
            s.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : fallback;

        double D(string key, double fallback) =>
            double.TryParse(G(key, ""), System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : fallback;

        int I(string key, int fallback) =>
            int.TryParse(G(key, ""), out var i) ? i : fallback;

        switch (effect)
        {
            case CropEffect c:
                c.X = I("x", c.X);
                c.Y = I("y", c.Y);
                c.Width = I("width", c.Width);
                c.Height = I("height", c.Height);
                break;

            case TransformEffect t:
                t.PositionX = D("posX", t.PositionX);
                t.PositionY = D("posY", t.PositionY);
                t.ScaleX = D("scaleX", t.ScaleX);
                t.ScaleY = D("scaleY", t.ScaleY);
                t.Rotation = D("rotation", t.Rotation);
                break;

            case ColorCorrectionEffect cc:
                cc.Brightness = D("brightness", cc.Brightness);
                cc.Contrast = D("contrast", cc.Contrast);
                cc.Saturation = D("saturation", cc.Saturation);
                cc.Gamma = D("gamma", cc.Gamma);
                cc.Highlights = D("highlights", cc.Highlights);
                cc.Shadows = D("shadows", cc.Shadows);
                cc.Temperature = D("temperature", cc.Temperature);
                cc.Tint = D("tint", cc.Tint);
                break;

            case LutEffect lut:
                lut.FilePath = G("path", G("lutPath", G("file", lut.FilePath)));
                lut.Intensity = Math.Clamp(D("intensity", lut.Intensity), 0, 1);
                break;

            case ChromaKeyEffect ck:
                ck.KeyColor = NormalizeColor(G("keyColor", ck.KeyColor));
                ck.Similarity = Math.Clamp(D("similarity", ck.Similarity), 0.001, 0.999);
                ck.Smoothness = Math.Clamp(D("smoothness", ck.Smoothness), 0, 0.999);
                ck.SpillReduction = Math.Clamp(D("spill", ck.SpillReduction), 0, 1);
                break;

            case LumaKeyEffect lk:
                lk.Threshold = Math.Clamp(D("threshold", lk.Threshold), 0, 1);
                break;

            case OpacityEffect op:
                op.Opacity = Math.Clamp(D("opacity", op.Opacity), 0, 1);
                break;
        }
    }

    /// <summary>Accepts "#RRGGBB" or bare hex and returns ffmpeg's 0xRRGGBB form.</summary>
    public static string NormalizeColor(string color)
    {
        var hex = color.Trim().TrimStart('#');
        if (hex.Length == 6 && hex.All(Uri.IsHexDigit)) return "0x" + hex;
        return color; // assume already in ffmpeg form
    }
}

/// <summary>RGB inversion — trivial but completes the legacy effect list.</summary>
public class InvertEffect : VideoEffect
{
    public InvertEffect()
    {
        Name = "Invert";
    }

    public override string BuildFfmpegFilter() => "negate";
}

/// <summary>Spatial denoiser (hqdn3d) — video counterpart of the audio NR chain.</summary>
public class VideoNoiseReductionEffect : VideoEffect
{
    /// <summary>0 = off … 10 = aggressive.</summary>
    public double Strength { get; set; } = 4;

    public VideoNoiseReductionEffect()
    {
        Name = "Noise Reduction";
    }

    public override string? BuildFfmpegFilter()
    {
        double luma = Math.Clamp(Strength, 0, 10);
        return luma < 0.05 ? null : $"hqdn3d={FX.N(luma)}:{FX.N(luma * 1.5)}:6:6";
    }
}

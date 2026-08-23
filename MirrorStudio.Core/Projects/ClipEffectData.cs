namespace MirrorStudio.Core.Projects;

/// <summary>
/// One entry in a clip's ordered effect stack (spec M10 §28/§29). Type names map
/// to <see cref="MirrorStudio.Video.Effects.VideoEffect"/> subclasses; extra
/// parameters (LUT path, key color…) ride along as string settings.
/// </summary>
public class ClipEffectData
{
    /// <summary>Effect family, e.g. "Blur", "ColorCorrection", "Lut".</summary>
    public string Type { get; set; } = "";

    /// <summary>Primary scalar (amount/strength) — matches the legacy single-slider model.</summary>
    public double Amount { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>Named parameters beyond Amount (e.g. lutPath, keyColor, similarity).</summary>
    public Dictionary<string, string> Settings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

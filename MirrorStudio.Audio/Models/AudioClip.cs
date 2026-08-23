namespace MirrorStudio.Audio.Models;

/// <summary>
/// One placed audio clip (spec M9 §2). Timeline position and source position
/// are separate: Start/Duration describe the timeline, SourceStart/SourceDuration
/// describe the window inside the referenced media asset.
/// </summary>
public class AudioClip
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>References the media asset this clip plays back.</summary>
    public Guid MediaAssetId { get; set; }

    public string Name { get; set; } = "";

    /// <summary>Timeline start in seconds.</summary>
    public double Start { get; set; }

    /// <summary>Timeline duration in seconds.</summary>
    public double Duration { get; set; }

    /// <summary>Where playback begins inside the source, in seconds.</summary>
    public double SourceStart { get; set; }

    public double SourceDuration { get; set; }

    /// <summary>Linear gain multiplier: 0.0 = silent, 1.0 = unity.</summary>
    public double Volume { get; set; } = 1.0;

    /// <summary>Trim gain in decibels on top of <see cref="Volume"/> (spec M9 §5).</summary>
    public double GainDb { get; set; }

    /// <summary>-1 = left, 0 = center, +1 = right.</summary>
    public double Pan { get; set; }

    public bool Muted { get; set; }

    public bool Solo { get; set; }

    /// <summary>Fade-in length in seconds (afade=t=in).</summary>
    public double FadeIn { get; set; }

    /// <summary>Fade-out length in seconds (afade=t=out).</summary>
    public double FadeOut { get; set; }

    /// <summary>Optional volume automation: linear gain over clip-local time (spec M9 §23).</summary>
    public List<VolumeKeyframe> Keyframes { get; set; } = [];
}

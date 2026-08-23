namespace MirrorStudio.Core.Projects;

/// <summary>
/// A track inside the serialized project: ordered clips sharing a lane.
/// </summary>
public class TimelineTrackData
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "V1";

    /// <summary>"video" or "audio".</summary>
    public string Type { get; set; } = "video";

    public int Index { get; set; }

    public bool Muted { get; set; }

    public bool Locked { get; set; }

    public bool Solo { get; set; }

    public List<TimelineClipData> Clips { get; set; } = [];
}

/// <summary>
/// One placed clip. Timeline position and source position are kept apart
/// (spec M8 §17): Start/Duration describe the timeline, SourceStart/SourceEnd
/// describe the window inside the referenced media.
/// </summary>
public class TimelineClipData
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>References <see cref="ProjectMedia.Id"/>.</summary>
    public Guid MediaAssetId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Timeline start in seconds.</summary>
    public double Start { get; set; }

    /// <summary>Timeline duration in seconds.</summary>
    public double Duration { get; set; }

    /// <summary>Source in-point in seconds.</summary>
    public double SourceStart { get; set; }

    /// <summary>Source out-point in seconds.</summary>
    public double SourceEnd { get; set; }

    /// <summary>Playback speed factor (1.0 = normal).</summary>
    public double Speed { get; set; } = 1.0;

    public double Volume { get; set; } = 1.0;

    /// <summary>Trim gain in decibels on top of Volume (spec M9 §5).</summary>
    public double GainDb { get; set; }

    /// <summary>-1 = left, 0 = center, +1 = right (spec M9 §6).</summary>
    public double Pan { get; set; }

    /// <summary>Fade-in length in seconds (spec M9 §8).</summary>
    public double FadeIn { get; set; }

    /// <summary>Fade-out length in seconds.</summary>
    public double FadeOut { get; set; }

    /// <summary>Clip-level silence (track mute/solo live on the track).</summary>
    public bool Muted { get; set; }

    public bool Solo { get; set; }

    public double Opacity { get; set; } = 1.0;

    // ---------- effects & transitions (spec M10) ----------

    /// <summary>Ordered effect stack — order is significant (§29).</summary>
    public List<ClipEffectData> Effects { get; set; } = [];

    /// <summary>Incoming transition from the previous clip: "Cut", "CrossDissolve", "Fade", "DipToBlack", "DipToWhite", "Wipe", "Slide", "Zoom".</summary>
    public string TransitionKind { get; set; } = "Cut";

    public double TransitionDurationSec { get; set; } = 0.5;

    /// <summary>Dip color for DipToBlack/DipToWhite-style fades ("#000000").</summary>
    public string TransitionColor { get; set; } = "#000000";
}

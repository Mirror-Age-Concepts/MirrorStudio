namespace MirrorStudio.Audio.Models;

/// <summary>
/// A mixer lane holding audio clips (spec M9 §3): A1 Dialogue, A2 Music,
/// A3 Sound Effects, A4 Voice Over, A5 Ambient.
/// </summary>
public class AudioTrack
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "Audio Track";

    /// <summary>Lane index (0-based) shown as A{n+1}.</summary>
    public int Index { get; set; }

    public List<AudioClip> Clips { get; set; } = [];

    /// <summary>Linear gain multiplier applied to every clip on this lane.</summary>
    public double Volume { get; set; } = 1.0;

    /// <summary>-1 = left, 0 = center, +1 = right.</summary>
    public double Pan { get; set; }

    public bool Muted { get; set; }

    public bool Solo { get; set; }
}

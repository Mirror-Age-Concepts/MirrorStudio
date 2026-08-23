namespace MirrorStudio.Audio.Models;

/// <summary>One volume automation point, in clip-local seconds (spec M9 §23).</summary>
public class VolumeKeyframe
{
    public double Time { get; set; }

    /// <summary>Linear gain at this point (0..4).</summary>
    public double Value { get; set; } = 1.0;
}

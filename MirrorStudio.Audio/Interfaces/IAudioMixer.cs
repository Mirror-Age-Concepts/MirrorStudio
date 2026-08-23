using MirrorStudio.Audio.Models;

namespace MirrorStudio.Audio.Interfaces;

/// <summary>
/// Gain staging and FFmpeg filter generation for the mixer stage
/// (spec M9 §7/§8/§13/§21/§22).
/// </summary>
public interface IAudioMixer
{
    /// <summary>Solo/mute resolution: when any track is soloed only solo tracks pass.</summary>
    bool IsAudible(AudioTrack track, IReadOnlyList<AudioTrack> allTracks);

    /// <summary>Total linear gain of a clip: clip volume × clip dB × track volume.</summary>
    double ClipGainLinear(AudioClip clip, AudioTrack track);

    /// <summary>Trapezoid fade envelope in 0..1 at clip-local time t.</summary>
    double FadeGainAt(AudioClip clip, double clipLocalTime);

    /// <summary>Ducking envelope in 0..1 at timeline time t (spec M9 §22).</summary>
    double DuckGainAt(IReadOnlyList<(double Start, double End)> dialogueIntervals,
        double t, DuckingSettings ducking);

    /// <summary>FFmpeg filter chain segment for one audible clip (after atrim/asetpts).</summary>
    string BuildClipFilters(AudioClip clip, AudioTrack track,
        IReadOnlyList<(double Start, double End)>? dialogueIntervals = null,
        DuckingSettings? ducking = null);
}

using MirrorStudio.Audio.Models;

namespace MirrorStudio.Audio.Interfaces;

/// <summary>
/// Realtime playback engine (spec M9 §15). Driven by a shared master clock
/// so video and audio never run independent timelines (§16).
/// </summary>
public interface IAudioEngine
{
    Task PlayAsync();

    void Pause();

    void Stop();

    Task SeekAsync(double position);

    double CurrentTime { get; }

    bool IsPlaying { get; }

    /// <summary>Fired on every clock tick with the current timeline position.</summary>
    event EventHandler<double>? PositionChanged;

    /// <summary>Fired when playback reaches the end of the material.</summary>
    event EventHandler? Ended;
}

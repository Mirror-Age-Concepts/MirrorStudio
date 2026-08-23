using MirrorStudio.Rendering.Models;

namespace MirrorStudio.Rendering.Interfaces;

public interface IPlaybackEngine
{
    PlaybackState State { get; }

    double CurrentTime { get; }

    /// <summary>Project frame rate driving the playback clock.</summary>
    double FrameRate { get; set; }

    Task PlayAsync();

    void Pause();

    void Stop();

    Task SeekAsync(double position);

    Task NextFrameAsync();

    Task PreviousFrameAsync();

    event EventHandler<VideoFrame>? FrameReady;
}

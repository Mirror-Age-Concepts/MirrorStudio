using MirrorStudio.Rendering.Interfaces;
using MirrorStudio.Rendering.Models;

namespace MirrorStudio.Rendering.Services;

public class PlaybackEngine : IPlaybackEngine
{
    private readonly string _ffmpegPath;

    private CancellationTokenSource?
        _playbackCancellation;

    private double _currentTime;

    public PlaybackState State { get; private set; }
        = PlaybackState.Stopped;

    /// <summary>Project frame rate — drives the playback clock and frame stepping.</summary>
    public double FrameRate
    {
        get => _frameRate;
        set => _frameRate = Math.Max(1.0, value);
    }

    private double _frameRate = 25.0;

    public double CurrentTime =>
        _currentTime;

    public event EventHandler<VideoFrame>?
        FrameReady;

    public PlaybackEngine(
        string ffmpegPath = "ffmpeg")
    {
        _ffmpegPath = ffmpegPath;
    }

    public async Task PlayAsync()
    {
        if (State == PlaybackState.Playing)
            return;

        State = PlaybackState.Playing;

        _playbackCancellation =
            new CancellationTokenSource();

        await Task.Run(
            () => PlaybackLoop(
                _playbackCancellation.Token));
    }

    public void Pause()
    {
        if (State != PlaybackState.Playing)
            return;

        State = PlaybackState.Paused;

        _playbackCancellation?.Cancel();
    }

    public void Stop()
    {
        State = PlaybackState.Stopped;

        _playbackCancellation?.Cancel();

        _currentTime = 0;
    }

    public Task SeekAsync(double position)
    {
        _currentTime =
            Math.Max(0, position);

        return Task.CompletedTask;
    }

    public Task NextFrameAsync()
    {
        _currentTime += 1.0 / FrameRate;

        return Task.CompletedTask;
    }

    public Task PreviousFrameAsync()
    {
        _currentTime =
            Math.Max(
                0,
                _currentTime - 1.0 / FrameRate);

        return Task.CompletedTask;
    }

    private async Task PlaybackLoop(
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(1.0 / FrameRate),
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            _currentTime += 1.0 / FrameRate;
        }
    }
}

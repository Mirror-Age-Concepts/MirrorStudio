using MirrorStudio.Audio.Interfaces;

namespace MirrorStudio.Audio.Services;

/// <summary>
/// Timeline audio playback driven by the shared <see cref="MasterClock"/>
/// (spec M9 §15/§16). The clock is authoritative; the engine reports position
/// and raises ticks so the mixer/meters can follow. Device-level output is
/// routed through ffplay in preview builds and through the export pipeline
/// for final renders.
/// </summary>
public class AudioEngine : IAudioEngine
{
    private readonly MasterClock _clock;
    private System.Timers.Timer? _pumpTimer;

    public double CurrentTime => _clock.CurrentTime;

    public bool IsPlaying => _clock.IsPlaying;

    public event EventHandler<double>? PositionChanged;

    public event EventHandler? Ended;

    /// <summary>Timeline duration — playback stops and fires Ended past this.</summary>
    public double Duration { get; set; }

    public AudioEngine(MasterClock? clock = null)
    {
        _clock = clock ?? new MasterClock();
        _clock.Tick += (_, t) =>
        {
            PositionChanged?.Invoke(this, t);
            if (Duration > 0 && t >= Duration)
            {
                Pause();
                Ended?.Invoke(this, EventArgs.Empty);
            }
        };
    }

    /// <summary>The shared clock, so the video side can attach to the same timeline.</summary>
    public MasterClock Clock => _clock;

    public Task PlayAsync()
    {
        StartPump();
        _clock.Play();
        return Task.CompletedTask;
    }

    public void Pause()
    {
        _clock.Pause();
        StopPump();
    }

    public void Stop()
    {
        _clock.Stop();
        StopPump();
        PositionChanged?.Invoke(this, 0);
    }

    public Task SeekAsync(double position)
    {
        _clock.Seek(Math.Max(0, position));
        PositionChanged?.Invoke(this, _clock.CurrentTime);
        return Task.CompletedTask;
    }

    /// <summary>Playback rate forwarded to the master clock.</summary>
    public double Rate
    {
        get => _clock.Rate;
        set => _clock.Rate = value;
    }

    private void StartPump()
    {
        if (_pumpTimer != null) return;
        _pumpTimer = new System.Timers.Timer(33) { AutoReset = true }; // ~30 Hz meter/tick rate
        _pumpTimer.Elapsed += (_, _) => _clock.Pump();
        _pumpTimer.Start();
    }

    private void StopPump()
    {
        if (_pumpTimer == null) return;
        _pumpTimer.Stop();
        _pumpTimer.Dispose();
        _pumpTimer = null;
    }
}

using System.Diagnostics;

namespace MirrorStudio.Audio.Services;

/// <summary>
/// The single timeline clock shared by the video renderer and the audio engine
/// (spec M9 §16). Wall-clock driven so it never drifts with render cost:
/// consumers read <see cref="CurrentTime"/> and schedule around it.
///
/// Audio is the preferred timing reference during playback (§16) — the video
/// side decides to hold or drop frames against this clock via AvSyncCorrector.
/// </summary>
public class MasterClock
{
    private readonly Stopwatch _wall = Stopwatch.StartNew();
    private readonly object _gate = new();

    private double _position;
    private double _rate = 1.0;
    private bool _running;
    private long _lastTickWallMs;

    public bool IsPlaying { get { lock (_gate) return _running; } }

    public double CurrentTime
    {
        get
        {
            lock (_gate)
            {
                return _running
                    ? _position + (_wall.ElapsedMilliseconds - _lastTickWallMs) / 1000.0 * _rate
                    : _position;
            }
        }
    }

    /// <summary>Playback rate (1.0 = normal, 0.5 = half speed).</summary>
    public double Rate
    {
        get { lock (_gate) return _rate; }
        set { lock (_gate) { _rate = Math.Clamp(value, 0.1, 4.0); } }
    }

    public event EventHandler<double>? Tick;

    /// <summary>Starts (or resumes) the clock from the current position.</summary>
    public void Play()
    {
        lock (_gate)
        {
            if (_running) return;
            _running = true;
            _lastTickWallMs = _wall.ElapsedMilliseconds;
        }
    }

    public void Pause()
    {
        lock (_gate)
        {
            if (!_running) return;
            _position = CurrentTimeNoLock();
            _running = false;
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _position = 0;
            _running = false;
        }
    }

    public void Seek(double position)
    {
        lock (_gate)
        {
            _position = Math.Max(0, position);
            _lastTickWallMs = _wall.ElapsedMilliseconds;
        }
    }

    /// <summary>Called by a UI timer (~30–60 Hz) to raise Tick with the current time.</summary>
    public void Pump()
    {
        var t = CurrentTime;
        Tick?.Invoke(this, t);
    }

    private double CurrentTimeNoLock() =>
        _running
            ? _position + (_wall.ElapsedMilliseconds - _lastTickWallMs) / 1000.0 * _rate
            : _position;
}

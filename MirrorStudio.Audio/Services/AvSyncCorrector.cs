namespace MirrorStudio.Audio.Services;

/// <summary>
/// A/V sync policy (spec M9 §17): compares the video pipeline's timestamp
/// against the master (audio-referenced) clock and decides what video should do.
/// Never stretches audio — timing differences are absorbed by frames.
/// </summary>
public enum VideoSyncAction
{
    Normal,
    /// <summary>Video is ahead of the clock → repeat/hold the last frame.</summary>
    HoldFrame,
    /// <summary>Video is far behind → skip ahead (drop pending frames).</summary>
    SkipAhead,
}

public static class AvSyncCorrector
{
    /// <summary>Small drifts are invisible; don't react below this.</summary>
    public const double ToleranceSec = 0.04;

    /// <summary>Beyond this the video is considered lost — jump instead of chasing.</summary>
    public const double CatchUpSec = 0.25;

    public static VideoSyncAction Decide(double videoTimestamp, double masterClockTime)
    {
        double delta = videoTimestamp - masterClockTime;

        if (delta > CatchUpSec)
            return VideoSyncAction.HoldFrame;
        if (delta < -CatchUpSec)
            return VideoSyncAction.SkipAhead;
        return VideoSyncAction.Normal;
    }
}

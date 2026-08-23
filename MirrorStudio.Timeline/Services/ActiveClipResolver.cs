using MirrorStudio.Timeline.Models;

namespace MirrorStudio.Timeline.Services;

public class ActiveClipResolver
{
    public TimelineClip? FindActiveClip(
        TimelineTrack track,
        double timelinePosition)
    {
        return track.Clips.FirstOrDefault(
            clip =>
                timelinePosition >= clip.Start &&
                timelinePosition <
                clip.Start + clip.Duration);
    }

    /// <summary>Topmost active clip across tracks (higher Index renders above).</summary>
    public TimelineClip? FindActiveClip(
        IEnumerable<TimelineTrack> tracks,
        double timelinePosition,
        bool videoTracksOnly = true)
    {
        return tracks
            .Where(t => !videoTracksOnly || t.Type == Core.Enums.TrackType.Video)
            .Select(t => FindActiveClip(t, timelinePosition))
            .OfType<TimelineClip>()
            .OrderByDescending(c => c.Start)   // later start wins on overlap
            .FirstOrDefault();
    }
}

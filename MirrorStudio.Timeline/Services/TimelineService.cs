using MirrorStudio.Core.Enums;
using MirrorStudio.Timeline.Models;

namespace MirrorStudio.Timeline.Services;

public class TimelineService
{
    public TimelineModel Timeline { get; } = new();

    public TimelineService()
    {
        AddDefaultTracks();
    }

    private void AddDefaultTracks()
    {
        Timeline.Tracks.Add(
            new TimelineTrack
            {
                Name = "V1",
                Type = TrackType.Video,
                Index = 0
            });

        Timeline.Tracks.Add(
            new TimelineTrack
            {
                Name = "V2",
                Type = TrackType.Video,
                Index = 1
            });

        Timeline.Tracks.Add(
            new TimelineTrack
            {
                Name = "A1",
                Type = TrackType.Audio,
                Index = 2
            });

        Timeline.Tracks.Add(
            new TimelineTrack
            {
                Name = "A2",
                Type = TrackType.Audio,
                Index = 3
            });
    }

    public TimelineClip AddClip(
        Guid mediaAssetId,
        string name,
        double duration,
        int trackIndex)
    {
        var track =
            Timeline.Tracks[trackIndex];

        var start =
            FindNextAvailablePosition(track);

        var clip = new TimelineClip
        {
            MediaAssetId = mediaAssetId,
            Name = name,
            Start = start,
            Duration = duration,
            SourceStart = 0,
            SourceEnd = duration
        };

        track.Clips.Add(clip);

        Timeline.Duration =
            Math.Max(
                Timeline.Duration,
                start + duration);

        return clip;
    }

    private static double FindNextAvailablePosition(
        TimelineTrack track)
    {
        if (track.Clips.Count == 0)
            return 0;

        return track.Clips.Max(
            x => x.Start + x.Duration);
    }

    public void RemoveClip(
        Guid clipId)
    {
        foreach (var track in Timeline.Tracks)
        {
            var clip =
                track.Clips.FirstOrDefault(
                    x => x.Id == clipId);

            if (clip != null)
            {
                track.Clips.Remove(clip);
                RecalculateDuration();
                return;
            }
        }
    }

    public TimelineClip? FindClip(Guid clipId) =>
        Timeline.Tracks
            .SelectMany(t => t.Clips)
            .FirstOrDefault(c => c.Id == clipId);

    public IEnumerable<TimelineClip> ActiveClipsAt(double time) =>
        Timeline.Tracks
            .SelectMany(t => t.Clips)
            .Where(c => time >= c.Start && time < c.Start + c.Duration);

    public void RecalculateDuration() =>
        Timeline.Duration = Timeline.Tracks
            .SelectMany(t => t.Clips)
            .Select(c => c.Start + c.Duration)
            .DefaultIfEmpty(0)
            .Max();
}

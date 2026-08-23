using MirrorStudio.Core.Enums;

namespace MirrorStudio.Timeline.Models;

public class TimelineTrack
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public TrackType Type { get; set; }

    public int Index { get; set; }

    public bool IsLocked { get; set; }

    public bool IsMuted { get; set; }

    public bool IsSolo { get; set; }

    public List<TimelineClip> Clips { get; } = [];
}

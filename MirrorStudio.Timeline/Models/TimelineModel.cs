namespace MirrorStudio.Timeline.Models;

public class TimelineModel
{
    public double Duration { get; set; }

    public double CurrentTime { get; set; }

    public double Zoom { get; set; } = 1.0;

    public List<TimelineTrack> Tracks { get; } = [];
}

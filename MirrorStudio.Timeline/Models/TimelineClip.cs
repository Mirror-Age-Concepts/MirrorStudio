namespace MirrorStudio.Timeline.Models;

public class TimelineClip
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid MediaAssetId { get; set; }

    public string Name { get; set; } = string.Empty;

    public double Start { get; set; }

    public double Duration { get; set; }

    public double SourceStart { get; set; }

    public double SourceEnd { get; set; }

    public double Opacity { get; set; } = 1.0;

    public double Volume { get; set; } = 1.0;

    public double PositionX { get; set; }

    public double PositionY { get; set; }

    public double ScaleX { get; set; } = 1.0;

    public double ScaleY { get; set; } = 1.0;

    public double Rotation { get; set; }

    public TimeRange Range => TimeRange.FromStartAndDuration(Start, Duration);
}

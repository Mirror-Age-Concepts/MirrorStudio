namespace MirrorStudio.Timeline.Models;

public readonly record struct TimeRange
{
    public TimeRange(double start, double end)
    {
        if (end < start) (start, end) = (end, start);
        Start = start;
        End = end;
    }

    public double Start { get; }

    public double End { get; }

    public double Duration => End - Start;

    public bool Contains(double time) => time >= Start && time <= End;

    public bool Intersects(TimeRange other) => Start < other.End && other.Start < End;

    public bool Overlaps(TimeRange other) => Intersects(other);

    public static TimeRange FromStartAndDuration(double start, double duration) =>
        new(start, start + Math.Max(0, duration));
}

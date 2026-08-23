namespace MirrorStudio.Timeline.Services;

public class SnapService
{
    /// <summary>Snap points collected from the surrounding timeline.</summary>
    public static IEnumerable<double> CollectPoints(
        IEnumerable<(double Start, double End)> otherClips,
        double playhead,
        bool includeTimelineStart = true)
    {
        if (includeTimelineStart)
            yield return 0;

        foreach (var (start, end) in otherClips)
        {
            yield return start;
            yield return end;
        }

        if (playhead > 0)
            yield return playhead;
    }

    public double Snap(
        double position,
        IEnumerable<double> points,
        double threshold = 0.15)
    {
        var nearest =
            points
                .OrderBy(
                    p => Math.Abs(p - position))
                .FirstOrDefault();

        if (Math.Abs(nearest - position)
            <= threshold)
        {
            return Math.Max(0, nearest);
        }

        return position;
    }
}

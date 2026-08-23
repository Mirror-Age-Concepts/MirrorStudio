namespace MirrorStudio.Timeline.Services;

/// <summary>
/// Maps a timeline position to the position inside the clip's source media.
/// Example: clip placed at timeline 20s, first 20s of source trimmed away
/// (SourceStart = 20). At timeline 40s the source time is 20 + (40 - 20) = 40s.
/// </summary>
public static class SourceTimeCalculator
{
    public static double GetSourceTime(
        double timelineTime,
        double clipStart,
        double sourceStart)
    {
        return sourceStart +
               (timelineTime - clipStart);
    }

    public static (double Start, double End) GetSourceRange(
        Models.TimelineClip clip)
    {
        return (clip.SourceStart, clip.SourceEnd);
    }
}

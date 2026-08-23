namespace MirrorStudio.Timeline
{
    public record TimelineItem(string Id, long StartTicks, long DurationTicks, string TrackId);
}

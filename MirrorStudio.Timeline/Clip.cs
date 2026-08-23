namespace MirrorStudio.Timeline
{
    public class Clip
    {
        public string Id { get; set; } = string.Empty;
        public string MediaId { get; set; } = string.Empty;
        public long StartTicks { get; set; }
        public long DurationTicks { get; set; }
        public int TrackIndex { get; set; }

        public Clip() { }
    }
}

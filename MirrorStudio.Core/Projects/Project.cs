namespace MirrorStudio.Core.Projects;

public class Project
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "Untitled Project";

    public string FilePath { get; set; } = string.Empty;

    public ProjectSettings Settings { get; set; } = new();

    public List<ProjectMedia> Media { get; set; } = [];

    public List<TimelineTrackData> Tracks { get; set; } = [];

    public DateTime CreatedAt { get; set; } =
        DateTime.UtcNow;

    public DateTime ModifiedAt { get; set; } =
        DateTime.UtcNow;
}

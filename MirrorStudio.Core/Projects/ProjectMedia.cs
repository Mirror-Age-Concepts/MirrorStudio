namespace MirrorStudio.Core.Projects;

/// <summary>
/// Reference to source media — the original file is never copied into the
/// project (spec M8 §3). If the file moves, MediaRelinkService re-points this.
/// </summary>
public class ProjectMedia
{
    public Guid Id { get; set; } =
        Guid.NewGuid();

    public string FilePath { get; set; } = "";

    public string FileName { get; set; } = "";

    public double Duration { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    public double FrameRate { get; set; }

    public string? ProxyPath { get; set; }
}

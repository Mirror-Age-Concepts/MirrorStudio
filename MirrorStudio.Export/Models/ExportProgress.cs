namespace MirrorStudio.Export.Models;

public class ExportProgress
{
    public double Percentage { get; set; }

    public TimeSpan RenderedDuration { get; set; }

    public TimeSpan EstimatedRemaining { get; set; }

    public string Status { get; set; } = "";
}

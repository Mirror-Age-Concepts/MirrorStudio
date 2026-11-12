namespace MirrorStudio.Graphics.Subtitles;

public class Subtitle
{
    public int Index { get; set; }

    public TimeSpan Start { get; set; }

    public TimeSpan End { get; set; }

    public string Text { get; set; } = "";

    [System.Text.Json.Serialization.JsonIgnore]
    public string StartText => Start.ToString(@"hh\:mm\:ss\.fff");

    [System.Text.Json.Serialization.JsonIgnore]
    public string EndText => End.ToString(@"hh\:mm\:ss\.fff");
}

public class SubtitleTrack
{
    public string Name { get; set; } = "Subtitles";

    public List<Subtitle> Items { get; set; } = [];

    /// <summary>Optional look captured from ASS styles / caption presets.</summary>
    public Models.TextStyle? Style { get; set; }

    /// <summary>Semi-transparent band behind text (0..1).</summary>
    public double BackgroundOpacity { get; set; }

    /// <summary>Vertical anchor: Bottom | Center | Top.</summary>
    public string Position { get; set; } = "Bottom";

    public void Renumber()
    {
        for (int i = 0; i < Items.Count; i++)
            Items[i].Index = i + 1;
    }

    public Subtitle? At(TimeSpan t) =>
        Items.FirstOrDefault(s => t >= s.Start && t < s.End);
}

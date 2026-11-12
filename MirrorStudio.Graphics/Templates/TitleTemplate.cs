using MirrorStudio.Graphics.Models;

namespace MirrorStudio.Graphics.Templates;

public class TitleTemplate
{
    public string Name { get; set; } = "";
    public string Category { get; set; } = "General";
    public string Text { get; set; } = "TITLE";
    public string Subtitle { get; set; } = "";
    public TextStyle Style { get; set; } = new();
    public double Duration { get; set; } = 5;
    /// <summary>Center | LowerThird | Top.</summary>
    public string PositionPreset { get; set; } = "Center";
    public string AnimationIn { get; set; } = "Fade In";
    public string AnimationOut { get; set; } = "Fade Out";

    public TextLayer Create(int projectWidth, int projectHeight)
    {
        var (rx, ry) = PositionFraction(PositionPreset);
        return new TextLayer
        {
            Name = Name,
            Text = Text,
            Duration = Duration,
            PositionX = projectWidth * rx,
            PositionY = projectHeight * ry,
            AnimationIn = AnimationIn,
            AnimationOut = AnimationOut,
            Style = Clone(Style)
        };
    }

    private static (double, double) PositionFraction(string preset) => preset switch
    {
        "LowerThird" => (0.25, 0.82),
        "Top" => (0.5, 0.12),
        _ => (0.5, 0.5)
    };

    private static TextStyle Clone(TextStyle s) => new()
    {
        FontFamily = s.FontFamily, FontSize = s.FontSize, Bold = s.Bold, Italic = s.Italic,
        FillColor = s.FillColor, StrokeColor = s.StrokeColor, StrokeWidth = s.StrokeWidth,
        LetterSpacing = s.LetterSpacing, LineSpacing = s.LineSpacing,
        ShadowOpacity = s.ShadowOpacity, ShadowBlur = s.ShadowBlur,
        ShadowOffsetX = s.ShadowOffsetX, ShadowOffsetY = s.ShadowOffsetY,
        Alignment = s.Alignment
    };

    /// <summary>FILM TITLES starter set (Milestone 11 §11).</summary>
    public static List<TitleTemplate> Defaults() =>
    [
        new() { Name = "Classic Title", Category = "Classic", Style = new TextStyle { FontFamily = "Georgia", FontSize = 72, FillColor = "#F5F0E1", ShadowOpacity = 0.8 }, AnimationIn = "Fade In" },
        new() { Name = "Minimal Title", Category = "Minimal", Style = new TextStyle { FontSize = 54, LetterSpacing = 8, Alignment = "Center" }, AnimationIn = "Fade In" },
        new() { Name = "Cinematic Title", Category = "Cinematic", Style = new TextStyle { FontFamily = "Trajan Pro", FontSize = 84, LetterSpacing = 14, FillColor = "#E8DCC8" }, AnimationIn = "Scale In", Duration = 6 },
        new() { Name = "Modern Title", Category = "Modern", Style = new TextStyle { FontFamily = "Segoe UI", FontSize = 64, Bold = true, FillColor = "#FFFFFF" }, AnimationIn = "Slide In Left" },
        new() { Name = "Documentary Title", Category = "Documentary", Style = new TextStyle { FontFamily = "Cambria", FontSize = 56, FillColor = "#F0EDE4" }, PositionPreset = "LowerThird", AnimationIn = "Typewriter" },
        new() { Name = "News Headline", Category = "News", Style = new TextStyle { FontFamily = "Arial", FontSize = 52, Bold = true, FillColor = "#FFFFFF", StrokeColor = "#CC0000", StrokeWidth = 2 }, PositionPreset = "Top", AnimationIn = "Slide In Down" },
        new() { Name = "Corporate Title", Category = "Corporate", Style = new TextStyle { FontFamily = "Calibri", FontSize = 58, FillColor = "#1B3A6B" }, AnimationIn = "Fade In" },
        new() { Name = "Sports Score", Category = "Sports", Style = new TextStyle { FontFamily = "Arial Black", FontSize = 68, Bold = true, FillColor = "#FFD700", StrokeColor = "#000000", StrokeWidth = 3 }, AnimationIn = "Pop In" },
        new() { Name = "Wedding Title", Category = "Wedding", Style = new TextStyle { FontFamily = "Segoe Script", FontSize = 66, Italic = true, FillColor = "#FFF5F7" }, AnimationIn = "Fade In", Duration = 6 },
        new() { Name = "YouTube Title", Category = "YouTube", Style = new TextStyle { FontFamily = "Impact", FontSize = 76, Bold = true, FillColor = "#FF0000", StrokeColor = "#FFFFFF", StrokeWidth = 2 }, AnimationIn = "Pop In" },
        new() { Name = "Podcast Intro", Category = "Podcast", Style = new TextStyle { FontFamily = "Verdana", FontSize = 60, Bold = true, FillColor = "#9146FF" }, AnimationIn = "Slide In Up" },
        new() { Name = "Social Media Title", Category = "Social Media", Style = new TextStyle { FontFamily = "Trebuchet MS", FontSize = 70, Bold = true, FillColor = "#00C6FF" }, AnimationIn = "Pop In", Duration = 4 }
    ];
}

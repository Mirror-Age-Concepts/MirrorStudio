using MirrorStudio.Graphics.Models;
using MirrorStudio.Graphics.Subtitles;

namespace MirrorStudio.Graphics.Templates;

/// <summary>Burn-in caption look presets (Milestone 11 §19).</summary>
public class CaptionTemplate
{
    public string Name { get; set; } = "";
    public TextStyle Style { get; set; } = new();
    public double BackgroundOpacity { get; set; }
    public string Position { get; set; } = "Bottom";

    public void ApplyTo(SubtitleTrack track)
    {
        track.Style = new TextStyle
        {
            FontFamily = Style.FontFamily,
            FontSize = Style.FontSize,
            Bold = Style.Bold,
            Italic = Style.Italic,
            FillColor = Style.FillColor,
            StrokeColor = Style.StrokeColor,
            StrokeWidth = Style.StrokeWidth,
            ShadowOpacity = Style.ShadowOpacity,
            ShadowOffsetX = Style.ShadowOffsetX,
            ShadowOffsetY = Style.ShadowOffsetY,
            Alignment = "Center"
        };
        track.BackgroundOpacity = BackgroundOpacity;
        track.Position = Position;
    }

    public static List<CaptionTemplate> Presets() =>
    [
        new()
        {
            Name = "Classic", BackgroundOpacity = 0,
            Style = new TextStyle { FontSize = 42, FillColor = "#FFFFFF", ShadowOpacity = 0.8, ShadowOffsetY = 2 }
        },
        new()
        {
            Name = "Netflix-style", BackgroundOpacity = 0.45,
            Style = new TextStyle { FontFamily = "Arial", FontSize = 40, FillColor = "#FFFFFF" }, Position = "Bottom"
        },
        new()
        {
            Name = "YouTube", BackgroundOpacity = 0.6,
            Style = new TextStyle { FontFamily = "Roboto", FontSize = 36, FillColor = "#FFFFFF" }
        },
        new()
        {
            Name = "TikTok", BackgroundOpacity = 0,
            Style = new TextStyle { FontSize = 52, Bold = true, FillColor = "#FFFFFF", StrokeColor = "#000000", StrokeWidth = 3 },
            Position = "Center"
        },
        new()
        {
            Name = "Documentary", BackgroundOpacity = 0,
            Style = new TextStyle { FontFamily = "Georgia", FontSize = 38, Italic = true, FillColor = "#F5F0E1" }
        },
        new()
        {
            Name = "News", BackgroundOpacity = 0.75,
            Style = new TextStyle { FontFamily = "Arial", FontSize = 38, Bold = true, FillColor = "#FFFFFF" }
        },
        new()
        {
            Name = "Cinema", BackgroundOpacity = 0,
            Style = new TextStyle { FontFamily = "Cambria", FontSize = 44, LetterSpacing = 2, FillColor = "#FFF8E7", ShadowOpacity = 0.9, ShadowBlur = 6 }
        }
    ];
}

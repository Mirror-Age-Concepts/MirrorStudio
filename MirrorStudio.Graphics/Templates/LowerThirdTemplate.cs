using MirrorStudio.Graphics.Models;

namespace MirrorStudio.Graphics.Templates;

public class LowerThirdTemplate
{
    public string Name { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string AccentColor { get; set; } = "#FFFFFF";
    public double Duration { get; set; } = 5;

    /// <summary>Compose the lower third as a name text + role text + accent bar.</summary>
    public List<GraphicLayer> Create(int projectWidth, int projectHeight)
    {
        double bandY = projectHeight * 0.80;
        double bandX = projectWidth * 0.06;
        var inDur = 0.5;

        return
        [
            new ShapeLayer
            {
                Name = "LT Band",
                ShapeType = "Rectangle",
                Width = projectWidth * 0.42,
                Height = projectHeight * 0.11,
                FillColor = "#CC111111",
                PositionX = bandX + projectWidth * 0.21,
                PositionY = bandY,
                Duration = Duration,
                AnimationIn = "Slide In Left",
                AnimationOut = "Fade Out",
                AnimationInDuration = inDur,
                AnimationOutDuration = inDur
            },
            new ShapeLayer
            {
                Name = "LT Accent",
                ShapeType = "Rectangle",
                Width = projectWidth * 0.007,
                Height = projectHeight * 0.11,
                FillColor = AccentColor,
                StrokeWidth = 0,
                PositionX = bandX,
                PositionY = bandY,
                Duration = Duration,
                AnimationIn = "Slide In Up",
                AnimationOut = "Fade Out",
                AnimationInDuration = inDur,
                AnimationOutDuration = inDur
            },
            new TextLayer
            {
                Name = "LT Name",
                Text = Name,
                Style = new TextStyle
                {
                    FontSize = projectHeight * 0.045,
                    Bold = true,
                    FillColor = "#FFFFFF",
                    Alignment = "Left",
                    ShadowOpacity = 0.6,
                    ShadowOffsetX = 2, ShadowOffsetY = 2
                },
                PositionX = bandX + projectWidth * 0.02,
                PositionY = bandY - projectHeight * 0.02,
                Duration = Duration,
                AnimationIn = "Fade In",
                AnimationOut = "Fade Out",
                AnimationInDuration = inDur,
                AnimationOutDuration = inDur
            },
            new TextLayer
            {
                Name = "LT Role",
                Text = Subtitle,
                Style = new TextStyle
                {
                    FontSize = projectHeight * 0.03,
                    FillColor = "#DDDDDD",
                    Alignment = "Left",
                    ShadowOpacity = 0.6,
                    ShadowOffsetX = 1, ShadowOffsetY = 1
                },
                PositionX = bandX + projectWidth * 0.02,
                PositionY = bandY + projectHeight * 0.032,
                Duration = Duration,
                AnimationIn = "Fade In",
                AnimationOut = "Fade Out",
                AnimationInDuration = inDur,
                AnimationOutDuration = inDur
            }
        ];
    }

    public static List<LowerThirdTemplate> Defaults() =>
    [
        new() { Name = "NAME SURNAME", Subtitle = "Role / Title", AccentColor = "#FFFFFF" },
        new() { Name = "Interviewee", Subtitle = "Documentary Subject", AccentColor = "#4FC3F7" },
        new() { Name = "Breaking News", Subtitle = "Live Report", AccentColor = "#E53935" },
        new() { Name = "Guest Speaker", Subtitle = "Corporate Event", AccentColor = "#66BB6A" }
    ];
}

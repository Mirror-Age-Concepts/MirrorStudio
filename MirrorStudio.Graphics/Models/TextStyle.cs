namespace MirrorStudio.Graphics.Models;

public class TextStyle
{
    public string FontFamily { get; set; } = "Arial";
    public double FontSize { get; set; } = 48;
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public string FillColor { get; set; } = "#FFFFFF";
    public string StrokeColor { get; set; } = "#000000";
    public double StrokeWidth { get; set; }
    public double LetterSpacing { get; set; }
    public double LineSpacing { get; set; } = 1;
    public double ShadowOpacity { get; set; }
    public double ShadowBlur { get; set; }
    public double ShadowOffsetX { get; set; }
    public double ShadowOffsetY { get; set; }
    public string Alignment { get; set; } = "Center";
}

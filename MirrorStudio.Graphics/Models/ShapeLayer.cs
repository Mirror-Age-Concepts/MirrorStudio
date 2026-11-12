namespace MirrorStudio.Graphics.Models;

public class ShapeLayer : GraphicLayer
{
    /// <summary>Rectangle | RoundedRectangle | Circle | Ellipse | Line | Arrow | Polygon.</summary>
    public string ShapeType { get; set; } = "Rectangle";

    public string FillColor { get; set; } = "#FFFFFF";
    public string StrokeColor { get; set; } = "#000000";
    public double StrokeWidth { get; set; }
    public double CornerRadius { get; set; }

    /// <summary>Size in project pixels (width/height, or length for Line/Arrow).</summary>
    public double Width { get; set; } = 400;
    public double Height { get; set; } = 120;
}

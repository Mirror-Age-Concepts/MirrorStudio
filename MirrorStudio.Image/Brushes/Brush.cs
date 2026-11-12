namespace MirrorStudio.Image.Brushes;

/// <summary>Brush parameters used by the painting tools.</summary>
public sealed class Brush
{
    public double Size { get; set; } = 24;
    public double Hardness { get; set; } = 0.5;   // 0 soft .. 1 hard edge
    public double Opacity { get; set; } = 1.0;     // max alpha per stroke
    public double Flow { get; set; } = 1.0;        // per-stamp accumulation
    public double Spacing { get; set; } = 0.25;    // fraction of size between stamps
    public string Shape { get; set; } = "Round";   // Round | Square
    public double Angle { get; set; }               // for fan/leaf shapes
    public byte[]? Texture { get; set; }            // optional grayscale stamp texture
}

namespace MirrorStudio.Graphics.Models;

/// <summary>Logo / watermark / PNG-JPG-WEBP-SVG graphic overlay.</summary>
public class ImageLayer : GraphicLayer
{
    /// <summary>Project-relative or absolute path to the image file.</summary>
    public string ImagePath { get; set; } = "";

    /// <summary>Natural pixel size of the source image (scale multipliers apply on top).</summary>
    public double SourceWidth { get; set; }

    public double SourceHeight { get; set; }
}

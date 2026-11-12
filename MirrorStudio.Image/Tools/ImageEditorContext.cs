using SkiaSharp;
using MirrorStudio.Image.Brushes;
using MirrorStudio.Image.Models;

namespace MirrorStudio.Image.Tools;

/// <summary>
/// Mutable state shared between the UI and the active <see cref="IImageTool"/>.
/// Coordinates in the context are always in image (document) space.
/// </summary>
public sealed class ImageEditorContext
{
    public ImageDocument Document { get; set; }
    public ImageCanvas Canvas { get; set; }
    public Brush Brush { get; set; } = new();
    public SKColor Foreground { get; set; } = SKColors.Black;
    public SKColor Background { get; set; } = SKColors.White;
    public string ActiveTool { get; set; } = "move";

    public (double x, double y)? LastPoint;
    public (double x, double y)? CloneSource;

    public SKBitmap? ActiveBitmap()
    {
        var l = Document.ActiveLayer;
        return (l != null && (l.Kind is ImageLayerKind.Raster or ImageLayerKind.Image)) ? l.Bitmap : null;
    }

    public ImageMask? ActiveMask() =>
        Document.Selection?.BuildMask(Document.Width, Document.Height);

    /// <summary>Map an image-space point into the active raster layer's local pixels.</summary>
    public (double x, double y) ToLayerLocal(double ix, double iy)
    {
        var l = Document.ActiveLayer;
        if (l == null) return (ix, iy);
        return (ix - l.PositionX, iy - l.PositionY);
    }
}

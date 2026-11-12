namespace MirrorStudio.Image.Models;

/// <summary>
/// Editing surface: owns the <see cref="ImageDocument"/> plus the current
/// view transform (zoom / pan) used to map between image and screen pixels.
/// The pixel compositing itself lives in <see cref="Rendering.ImageCompositor"/>.
/// </summary>
public sealed class ImageCanvas
{
    public ImageDocument Document { get; set; }
    public double Zoom { get; set; } = 1.0;
    public double PanX { get; set; }
    public double PanY { get; set; }

    public ImageCanvas(ImageDocument document) => Document = document;

    public (double x, double y) ImageToScreen(double ix, double iy) =>
        (PanX + ix * Zoom, PanY + iy * Zoom);

    public (double x, double y) ScreenToImage(double sx, double sy) =>
        ((sx - PanX) / Zoom, (sy - PanY) / Zoom);

    /// <summary>Fit the document into the given viewport, preserving aspect.</summary>
    public void FitToViewport(double viewportW, double viewportH)
    {
        if (Document.Width <= 0 || Document.Height <= 0) return;
        Zoom = Math.Min(viewportW / Document.Width, viewportH / Document.Height);
        if (Zoom <= 0 || double.IsInfinity(Zoom)) Zoom = 1;
        PanX = (viewportW - Document.Width * Zoom) / 2;
        PanY = (viewportH - Document.Height * Zoom) / 2;
    }
}

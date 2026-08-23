using MirrorStudio.Rendering.Interfaces;
using MirrorStudio.Rendering.Models;
using SkiaSharp;

namespace MirrorStudio.Rendering.Services;

/// <summary>
/// SkiaSharp-backed IPreviewRenderer: converts BGRA frame bytes into an SKBitmap
/// ready for direct upload to a Skia canvas / Avalonia surface.
/// </summary>
public class SkiaPreviewRenderer : IPreviewRenderer
{
    public SKBitmap? Bitmap { get; private set; }

    public double LastTimestamp { get; private set; } = -1;

    public void Render(VideoFrame frame)
    {
        Bitmap?.Dispose();

        Bitmap = new SKBitmap(
            new SKImageInfo(
                frame.Width,
                frame.Height,
                SKColorType.Bgra8888,
                SKAlphaType.Premul));

        var destination =
            Bitmap.GetPixels();

        System.Runtime.InteropServices.Marshal
            .Copy(
                frame.Data,
                0,
                destination,
                Math.Min(frame.Data.Length, (int)Bitmap.ByteCount));

        LastTimestamp = frame.Timestamp;
        Rendered?.Invoke(this, frame);
    }

    /// <summary>Convenience: render from an existing SKBitmap without re-copying.</summary>
    public void Render(SKBitmap bitmap, double timestamp)
    {
        Bitmap?.Dispose();
        Bitmap = bitmap;
        LastTimestamp = timestamp;
        Rendered?.Invoke(this, new VideoFrame
        {
            Data = [],
            Width = bitmap.Width,
            Height = bitmap.Height,
            Stride = bitmap.RowBytes,
            Timestamp = timestamp
        });
    }

    public void Clear()
    {
        Bitmap?.Dispose();
        Bitmap = null;
        LastTimestamp = -1;
        Cleared?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler<VideoFrame>? Rendered;

    public event EventHandler? Cleared;
}

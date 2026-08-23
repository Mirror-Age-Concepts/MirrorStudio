using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using MirrorStudio.Rendering.Interfaces;
using MirrorStudio.Rendering.Models;

namespace MirrorStudio.App.Services;

/// <summary>
/// Software IPreviewRenderer for Avalonia. Raw VideoFrames are blitted into a
/// WriteableBitmap; decoded bitmaps take the fast path. A DirectX/Vulkan renderer
/// can later implement IPreviewRenderer without touching timeline or preview logic.
/// </summary>
public class AvaloniaPreviewRenderer : IPreviewRenderer
{
    private readonly Avalonia.Controls.Image _target;

    public AvaloniaPreviewRenderer(Avalonia.Controls.Image target) => _target = target;

    public void Render(VideoFrame frame)
    {
        var bmp = new WriteableBitmap(
            new Avalonia.PixelSize(frame.Width, frame.Height),
            new Avalonia.Vector(96, 96),
            Avalonia.Platform.PixelFormat.Bgra8888);

        using (var l = bmp.Lock())
        {
            int rowBytes = Math.Min(frame.Stride, l.RowBytes);
            for (int y = 0; y < frame.Height; y++)
            {
                Marshal.Copy(frame.Data, y * frame.Stride,
                    IntPtr.Add(l.Address, y * l.RowBytes), rowBytes);
            }
        }

        _target.Source = bmp;
        _target.IsVisible = true;
    }

    /// <summary>Fast path for already-decoded bitmaps (current ffmpeg extraction pipeline).</summary>
    public void Render(Bitmap? bitmap)
    {
        if (bitmap == null) { Clear(); return; }
        _target.Source = bitmap;
        _target.IsVisible = true;
    }

    public void Clear()
    {
        _target.Source = null;
        _target.IsVisible = false;
    }
}

using MirrorStudio.Rendering.Interfaces;
using MirrorStudio.Rendering.Models;

namespace MirrorStudio.Rendering.Services;

/// <summary>
/// UI-agnostic software renderer. Stores the latest frame and raises events;
/// a UI adapter (e.g. Avalonia) subscribes and blits to screen.
/// Later GPU renderers can implement IPreviewRenderer without timeline changes.
/// </summary>
public class PreviewRenderer : IPreviewRenderer
{
    public VideoFrame? LastFrame { get; private set; }

    public event EventHandler<VideoFrame>? FrameRendered;

    public event EventHandler? Cleared;

    public void Render(VideoFrame frame)
    {
        LastFrame = frame;
        FrameRendered?.Invoke(this, frame);
    }

    public void Clear()
    {
        LastFrame = null;
        Cleared?.Invoke(this, EventArgs.Empty);
    }
}

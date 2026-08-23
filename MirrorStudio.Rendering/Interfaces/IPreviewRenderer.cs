using MirrorStudio.Rendering.Models;

namespace MirrorStudio.Rendering.Interfaces;

public interface IPreviewRenderer
{
    void Render(VideoFrame frame);

    void Clear();
}

using MirrorStudio.Rendering.Models;

namespace MirrorStudio.Video.Interfaces;

public interface IVideoDecoder : IDisposable
{
    int Width { get; }

    int Height { get; }

    double FrameRate { get; }

    double Duration { get; }

    Task OpenAsync(string filePath);

    Task<VideoFrame?> DecodeFrameAsync(
        double timestamp);

    Task SeekAsync(double timestamp);

    void Close();
}

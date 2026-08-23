namespace MirrorStudio.Rendering.Models;

public sealed class VideoFrame
{
    public required byte[] Data { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }

    public required int Stride { get; init; }

    public required double Timestamp { get; init; }
}

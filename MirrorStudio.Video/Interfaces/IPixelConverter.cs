namespace MirrorStudio.Video.Interfaces;

/// <summary>
/// Converts decoded frame pixels (typically planar YUV420P) into packed BGRA
/// that Avalonia/SkiaSharp can display directly.
/// The FFmpeg-native implementation uses sws_scale(); the managed fallback is
/// Yuv420pPixelConverter.
/// </summary>
public interface IPixelConverter
{
    byte[] ConvertToBgra(
        IntPtr frame,
        int width,
        int height);
}

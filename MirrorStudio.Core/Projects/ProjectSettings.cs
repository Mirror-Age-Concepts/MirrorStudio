namespace MirrorStudio.Core.Projects;

/// <summary>
/// Project-level render settings: the whole timeline knows
/// 1920×1080 · 25 fps · 48 kHz · BT.709 (spec M8 §2).
/// </summary>
public class ProjectSettings
{
    public int Width { get; set; } = 1920;

    public int Height { get; set; } = 1080;

    public double FrameRate { get; set; } = 25;

    public string PixelFormat { get; set; } =
        "yuv420p";

    public string AudioSampleRate { get; set; } =
        "48000";

    public string ColorSpace { get; set; } =
        "bt709";
}

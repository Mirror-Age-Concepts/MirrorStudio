using MirrorStudio.Audio.Models;

namespace MirrorStudio.Export.Models;

public class ExportSettings
{
    public string OutputPath { get; set; } = "";

    public string Format { get; set; } = "mp4";

    public string VideoCodec { get; set; } =
        "libx264";

    public string AudioCodec { get; set; } =
        "aac";

    /// <summary>Video bitrate in kbps (used when Quality is bitrate-driven).</summary>
    public int VideoBitrate { get; set; } =
        12000;

    public int AudioBitrate { get; set; } =
        192;

    public int Width { get; set; } = 1920;

    public int Height { get; set; } = 1080;

    public double FrameRate { get; set; } = 25;

    // ---- Mirror Studio extensions ----

    public string PresetName { get; set; } = "Custom";

    /// <summary>x264/x265 quality: 18 = High, 22 = Medium, 28 = Low.</summary>
    public int Crf { get; set; } = 18;

    /// <summary>NVIDIA NVENC / Intel QSync / AMD AMF acceleration.</summary>
    public bool UseHardwareEncoding { get; set; }

    public bool ExportAudioOnly { get; set; }

    /// <summary>Duck music under dialogue automatically (spec M9 §22).</summary>
    public DuckingSettings Ducking { get; set; } = new();
}

/// <summary>Named export presets incl. orientation variants (spec M8 §19/§20).</summary>
public static class ExportPresets
{
    public record Preset(string Name, int Width, int Height, string VideoCodec, int Crf, int AudioBitrate);

    public static readonly Preset YouTube1080p = new("YouTube 1080p", 1920, 1080, "libx264", 18, 192);
    public static readonly Preset YouTube4K = new("YouTube 4K", 3840, 2160, "libx265", 20, 256);
    public static readonly Preset InstagramSquare = new("Instagram", 1080, 1080, "libx264", 20, 192);
    public static readonly Preset TikTokReelsPortrait = new("TikTok / Reels", 1080, 1920, "libx264", 20, 192);
    public static readonly Preset FilmMaster = new("Film Master", 3840, 2160, "libx264", 12, 512);

    public static readonly IReadOnlyList<Preset> All =
    [
        YouTube1080p, YouTube4K, InstagramSquare, TikTokReelsPortrait, FilmMaster
    ];

    public static void Apply(Preset preset, ExportSettings settings)
    {
        settings.PresetName = preset.Name;
        settings.Width = preset.Width;
        settings.Height = preset.Height;
        settings.VideoCodec = preset.VideoCodec;
        settings.Crf = preset.Crf;
        settings.AudioBitrate = preset.AudioBitrate;
    }
}

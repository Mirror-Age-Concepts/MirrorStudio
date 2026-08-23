using System.Diagnostics;
using MirrorStudio.Rendering.Models;
using MirrorStudio.Video.Interfaces;

namespace MirrorStudio.Video.Services;

/// <summary>
/// Process-based video decoder: seeks + decodes exactly one frame per call via
/// the ffmpeg CLI and returns raw BGRA pixels (no full-video decode, no native
/// bindings to manage). Implements IVideoDecoder so an FFmpeg.AutoGen / GPU
/// decoder can replace it later without touching timeline or preview code.
/// </summary>
public sealed class FfmpegVideoDecoder : IVideoDecoder
{
    private readonly string _ffmpegPath;
    private readonly string _ffprobePath;
    private readonly FfprobeService? _probe;

    private string? _filePath;

    public int Width { get; private set; }

    public int Height { get; private set; }

    public double FrameRate { get; private set; } = 25.0;

    public double Duration { get; private set; }

    /// <summary>Optional decode height override (preview quality). Null = native.</summary>
    public int? DecodeHeight { get; set; }

    public FfmpegVideoDecoder(string ffmpegPath = "ffmpeg", string ffprobePath = "ffprobe")
    {
        _ffmpegPath = ffmpegPath;
        _ffprobePath = ffprobePath;
        if (File.Exists(ffprobePath) || !Path.IsPathRooted(ffprobePath))
            _probe = new FfprobeService(ffprobePath);
    }

    public async Task OpenAsync(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException(
                "Video file not found.",
                filePath);

        _filePath = filePath;

        if (_probe != null)
        {
            var meta = await _probe.TryGetMetadataAsync(filePath);
            if (meta != null)
            {
                Width = meta.Width;
                Height = meta.Height;
                Duration = meta.Duration;
                if (meta.FrameRate > 0.01)
                    FrameRate = meta.FrameRate;
                return;
            }
        }

        // probe failed — infer dimensions from the first frame
        var f = await DecodeFrameAsync(0);
        if (f != null)
        {
            Width = f.Width;
            Height = f.Height;
        }
    }

    /// <summary>Decodes a single BGRA frame at the requested timestamp.</summary>
    public async Task<VideoFrame?> DecodeFrameAsync(double timestamp)
    {
        if (_filePath == null)
            throw new InvalidOperationException(
                "Decoder has not been opened.");

        double t = Math.Max(0, timestamp);
        string scaleArg = BuildScaleArg();

        // -ss before -i = fast keyframe seek; single frame, raw BGRA to stdout
        string args =
            $"-ss {t.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)} " +
            $"-i \"{_filePath}\" " +
            "-frames:v 1 " +
            (scaleArg.Length > 0 ? scaleArg + " " : "") +
            "-f rawvideo -pix_fmt bgra -";

        var psi = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var proc = Process.Start(psi);
            if (proc == null) return null;

            using var ms = new MemoryStream();
            await proc.StandardOutput.BaseStream.CopyToAsync(ms);
            await proc.WaitForExitAsync();
            if (proc.ExitCode != 0) return null;

            byte[] data = ms.ToArray();
            int w = EffectiveWidth(), h = EffectiveHeight();
            int expected = w * h * 4;
            if (data.Length < expected) return null;

            return new VideoFrame
            {
                Data = data,
                Width = w,
                Height = h,
                Stride = w * 4,
                Timestamp = t
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Seeking is folded into the next decode command (-ss before -i).</summary>
    public Task SeekAsync(double timestamp) => Task.CompletedTask;

    public void Close()
    {
        _filePath = null;
    }

    public void Dispose() => Close();

    private int EffectiveWidth()
    {
        int h = EffectiveHeight();
        if (Height > 0 && h != Height)
            return Math.Max(2, (int)Math.Round((double)Width * h / Height / 2) * 2);
        return Math.Max(2, Width);
    }

    private int EffectiveHeight()
    {
        if (!DecodeHeight.HasValue || Height <= 0) return Height;
        int v = DecodeHeight.Value;
        return v < 2 ? 2 : (v % 2 == 0 ? v : v + 1);
    }

    private string BuildScaleArg()
    {
        if (!DecodeHeight.HasValue || Height <= 0) return "";
        int h = EffectiveHeight();
        return $"-vf \"scale=-2:{h}\"";
    }
}

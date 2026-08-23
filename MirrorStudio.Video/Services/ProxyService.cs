using System.Diagnostics;
using System.Globalization;

namespace MirrorStudio.Video.Services;

/// <summary>
/// Creates and manages lightweight editing proxies (spec M10 §32): heavy
/// source media gets a half/full-HD stand-in under &lt;media folder&gt;\.proxies,
/// so timeline scrubbing stays smooth on long-GOP footage. Originals are
/// never modified; exports always reference the original files.
/// </summary>
public class ProxyService
{
    public const int DefaultProxyHeight = 540;

    private readonly string _ffmpegPath;
    private readonly FfprobeService _ffprobe;

    public ProxyService(string ffmpegPath, FfprobeService? ffprobe = null)
    {
        _ffmpegPath = string.IsNullOrWhiteSpace(ffmpegPath) ? "ffmpeg" : ffmpegPath;
        _ffprobe = ffprobe ?? new FfprobeService();
    }

    // ---------- path helpers ----------

    public static string ProxyFolder(string mediaPath) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(mediaPath)) ?? ".", ".proxies");

    public static string ProxyPathFor(string mediaPath, int height = DefaultProxyHeight) =>
        Path.Combine(ProxyFolder(mediaPath),
            $"{Path.GetFileNameWithoutExtension(mediaPath)}_{height}p.mp4");

    /// <summary>True when a proxy exists and is at least as new as the source.</summary>
    public static bool IsUpToDate(string mediaPath, string proxyPath)
    {
        try
        {
            if (!File.Exists(proxyPath) || !File.Exists(mediaPath)) return false;
            return File.GetLastWriteTimeUtc(proxyPath) >=
                   File.GetLastWriteTimeUtc(mediaPath).AddSeconds(-1);
        }
        catch
        {
            return false;
        }
    }

    // ---------- operations ----------

    /// <summary>
    /// Returns the proxy path, creating it when missing or stale. Returns the
    /// original path when no proxy is warranted (small source, audio-only,
    /// probe failure). Never throws for expected IO/ffmpeg failures — returns null then.
    /// </summary>
    public async Task<string?> EnsureAsync(
        string mediaPath,
        int height = DefaultProxyHeight,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(mediaPath)) return null;
            height = Math.Clamp(height, 144, 2160);

            var proxy = ProxyPathFor(mediaPath, height);
            if (IsUpToDate(mediaPath, proxy))
                return proxy;

            var meta = await _ffprobe.TryGetMetadataAsync(mediaPath);
            if (meta is not { HasVideo: true })
                return null;
            if (meta.Height <= height)
                return mediaPath; // already light enough

            Directory.CreateDirectory(ProxyFolder(mediaPath));

            var totalSeconds = Math.Max(0.1, meta.Duration);
            var exit = await RunFfmpegAsync(
                $"-y -i \"{mediaPath}\" -vf scale=-2:{height} " +
                "-c:v libx264 -preset veryfast -crf 23 -profile:v high " +
                "-c:a aac -b:a 128k -movflags +faststart " +
                $"\"{proxy}\"",
                totalSeconds,
                progress,
                cancellationToken);

            if (exit != 0)
            {
                TryDelete(proxy);
                return null;
            }

            return proxy;
        }
        catch (OperationCanceledException)
        {
            TryDelete(ProxyPathFor(mediaPath, height));
            throw;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Playback resolution: an existing fresh proxy when the source is heavier
    /// than <paramref name="thresholdHeight"/>, otherwise the original.
    /// </summary>
    public async Task<string> ResolveForPlaybackAsync(string mediaPath, int thresholdHeight = 1080)
    {
        var proxy = ProxyPathFor(mediaPath);
        if (IsUpToDate(mediaPath, proxy))
        {
            var meta = await _ffprobe.TryGetMetadataAsync(mediaPath);
            if (meta is { HasVideo: true } && meta.Height > thresholdHeight)
                return proxy;
        }

        return mediaPath;
    }

    /// <summary>Deletes every proxy derived from the given media file.</summary>
    public static void Invalidate(string mediaPath)
    {
        try
        {
            var folder = ProxyFolder(mediaPath);
            if (!Directory.Exists(folder)) return;
            var stem = Path.GetFileNameWithoutExtension(mediaPath);
            foreach (var f in Directory.EnumerateFiles(folder, stem + "_*p.mp4"))
                TryDelete(f);
        }
        catch
        {
            // best effort cleanup
        }
    }

    // ---------- internals ----------

    private async Task<int> RunFfmpegAsync(
        string arguments,
        double totalSeconds,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };

        process.Start();
        _ = process.StandardOutput.ReadToEndAsync();

        var stderr = process.StandardError.ReadToEndAsync();
        string? line;
        while ((line = await process.StandardError.ReadLineAsync(cancellationToken)) != null)
        {
            if (progress != null && TryParseSeconds(line, out var seconds))
                progress.Report(Math.Clamp(seconds / totalSeconds * 100.0, 0, 100));
        }

        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode;
    }

    internal static bool TryParseSeconds(string line, out double seconds)
    {
        seconds = 0;
        const string marker = "time=";
        int idx = line.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0) return false;

        var span = line.AsSpan(idx + marker.Length);
        var parts = span.ToString().Split(':', 3);
        if (parts.Length != 3) return false;

        if (!int.TryParse(parts[0], out var h) ||
            !int.TryParse(parts[1], out var m) ||
            !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var s))
            return false;

        seconds = h * 3600 + m * 60 + s;
        return true;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* locked or gone */ }
    }
}

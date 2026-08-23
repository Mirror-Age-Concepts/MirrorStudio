using System.Security.Cryptography;
using System.Text;
using MirrorStudio.Audio.Interfaces;
using MirrorStudio.Audio.Models;

namespace MirrorStudio.Audio.Services;

/// <summary>
/// Generates waveforms with a disk cache (spec M9 §10): the cache key derives
/// from the media file's identity (path + size + mtime) plus the requested
/// resolution, so files are analyzed once — not on every timeline repaint.
///
/// Cache layout: {cacheRoot}/waveforms/{key}.waveform
/// </summary>
public class WaveformGenerator
{
    /// <summary>Analysis sample rate: 8 kHz mono is plenty for visual peaks.</summary>
    public const int AnalysisRateHz = 8000;

    private readonly IAudioDecoder _decoder;
    private readonly string _cacheDir;
    private readonly Dictionary<string, AudioWaveform> _memory = new();

    public WaveformGenerator(IAudioDecoder? decoder = null, string? cacheRoot = null)
    {
        _decoder = decoder ?? new AudioDecoder();
        _cacheDir = Path.Combine(
            cacheRoot ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MirrorStudio", "cache"),
            "waveforms");
    }

    /// <summary>Cache key from the media file identity + analysis resolution.</summary>
    public static string CacheKey(string filePath, int groupSize)
    {
        var fi = new FileInfo(filePath);
        string identity = $"{Path.GetFullPath(filePath)}|{fi.Length}|{fi.LastWriteTimeUtc.Ticks}|{groupSize}";

        return Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(identity)))[..16].ToLowerInvariant();
    }

    /// <summary>
    /// Spec API: returns normalized peaks, one point per pixel group,
    /// decoding and caching as needed. Empty when the file has no audio.
    /// </summary>
    public async Task<float[]> GenerateAsync(string filePath, int samplesPerPixel,
        CancellationToken cancellationToken = default)
    {
        var wf = await GetWaveformAsync(filePath, samplesPerPixel, cancellationToken);
        return wf?.Peaks ?? [];
    }

    /// <summary>Full waveform (peaks + RMS), served from memory → disk → decode.</summary>
    public async Task<AudioWaveform?> GetWaveformAsync(string filePath, int groupSize,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            return null;

        groupSize = Math.Max(1, groupSize);
        string key = CacheKey(filePath, groupSize);

        if (_memory.TryGetValue(key, out var cached))
            return cached;

        var diskPath = Path.Combine(_cacheDir, key + ".waveform");
        if (File.Exists(diskPath))
        {
            try
            {
                var wf = AudioWaveform.FromBytes(await File.ReadAllBytesAsync(diskPath, cancellationToken));
                _memory[key] = wf;
                return wf;
            }
            catch { /* corrupt cache entry — regenerate */ }
        }

        short[]? pcm = await _decoder.DecodeMono16Async(filePath, AnalysisRateHz, cancellationToken);
        if (pcm == null || pcm.Length == 0)
            return null;

        var (peaks, rms) = PeakAnalyzer.Compute(pcm, groupSize);
        var waveform = new AudioWaveform
        {
            Peaks = peaks,
            Rms = rms,
            GroupSize = groupSize,
            SampleRate = AnalysisRateHz,
        };

        try
        {
            Directory.CreateDirectory(_cacheDir);
            await File.WriteAllBytesAsync(diskPath, waveform.ToBytes(), cancellationToken);
        }
        catch { /* cache write failures must never break rendering */ }

        _memory[key] = waveform;
        return waveform;
    }

    /// <summary>Deletes the cached waveform for a media file (e.g. after re-import).</summary>
    public void Invalidate(string filePath, int groupSize)
    {
        string key = CacheKey(filePath, groupSize);
        _memory.Remove(key);
        try
        {
            var p = Path.Combine(_cacheDir, key + ".waveform");
            if (File.Exists(p)) File.Delete(p);
        }
        catch { /* best effort */ }
    }
}

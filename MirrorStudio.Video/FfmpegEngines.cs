using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MirrorStudio.Core.Interfaces;
using MirrorStudio.Core.Models;
using MirrorStudio.Video.Models;
using MirrorStudio.Video.Services;

namespace MirrorStudio.Video
{
    /// <summary>FFmpeg-backed implementations of the Core engine interfaces.</summary>
    public class FfmpegVideoEngine : IVideoEngine
    {
        private readonly FfprobeService _probe;

        public FfmpegVideoEngine(FfprobeService probe) => _probe = probe;

        public async Task<MediaAsset?> ProbeAsync(string filePath)
        {
            var meta = await _probe.TryGetMetadataAsync(filePath);
            if (meta == null || !meta.HasVideo) return null;
            return ToAsset(filePath, meta);
        }

        public async Task<bool> GenerateThumbnailAsync(string filePath, double atSeconds, string outputPath, int maxWidth = 320)
        {
            var svc = new ThumbnailService();
            return await svc.TryGenerateAsync(filePath, outputPath, atSeconds) != null;
        }

        public Task<string?> ExtractFrameAsync(string filePath, double seconds, string outputPath, int width = 640)
        {
            var svc = new ThumbnailService();
            return svc.TryGenerateAsync(filePath, outputPath, seconds);
        }

        public async Task<bool> GenerateProxyAsync(string filePath, string outputPath)
        {
            var svc = new FfmpegService();
            return await svc.GenerateProxyAsync(filePath, outputPath);
        }

        internal static MediaAsset ToAsset(string filePath, VideoMetadata meta) => new()
        {
            FileName = Path.GetFileName(filePath),
            FilePath = filePath,
            Extension = Path.GetExtension(filePath),
            FileSize = File.Exists(filePath) ? new FileInfo(filePath).Length : 0,
            Duration = meta.Duration,
            Width = meta.Width,
            Height = meta.Height,
            FrameRate = meta.FrameRate,
            Codec = meta.VideoCodec,
            AudioCodec = meta.AudioCodec,
            HasAudio = meta.HasAudio,
            HasVideo = meta.HasVideo,
            Type = meta.Type
        };
    }

    public class FfmpegAudioEngine : IAudioEngine
    {
        private readonly FfprobeService _probe;

        public FfmpegAudioEngine(FfprobeService probe) => _probe = probe;

        public async Task<MediaAsset?> ProbeAsync(string filePath)
        {
            var meta = await _probe.TryGetMetadataAsync(filePath);
            if (meta == null || !meta.HasAudio) return null;
            return FfmpegVideoEngine.ToAsset(filePath, meta);
        }

        /// <summary>Produces one peak value (0..1) per line via raw PCM decode — dependency free.</summary>
        public async Task<string?> ExtractWaveformPeaksAsync(string filePath, string outputPath, int pointsPerSecond = 10)
        {
            var tmp = Path.ChangeExtension(outputPath, ".raw");
            var result = await new FfmpegService().RunAsync($"-y -i \"{filePath}\" -vn -ac 1 -ar 8000 -f s16le \"{tmp}\"");
            if (!result.Success || !File.Exists(tmp)) return null;

            var bytes = await File.ReadAllBytesAsync(tmp);
            try { File.Delete(tmp); } catch { /* ignore */ }

            int samplesPerPoint = Math.Max(1, 8000 / Math.Max(1, pointsPerSecond));
            var lines = new List<string>();
            for (int i = 0; i + 1 < bytes.Length; i += 2 * samplesPerPoint)
            {
                short peak = 0;
                for (int s = 0; s < samplesPerPoint && i + 2 * s + 1 < bytes.Length; s++)
                {
                    short v = BitConverter.ToInt16(bytes, i + 2 * s);
                    if (Math.Abs(v) > Math.Abs(peak)) peak = v;
                }
                lines.Add((peak / 32768.0).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture));
            }
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            await File.WriteAllLinesAsync(outputPath, lines);
            return outputPath;
        }

        public async Task<bool> ApplyVolumeFadeAsync(string filePath, string outputPath, double volumeDb, double fadeInSec, double fadeOutSec)
        {
            var filters = new List<string>();
            if (Math.Abs(volumeDb) > 0.01) filters.Add($"volume={volumeDb.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}dB");
            if (fadeInSec > 0.01) filters.Add($"afade=t=in:st=0:d={fadeInSec.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}");
            if (fadeOutSec > 0.01) filters.Add($"afade=t=out:st=0:d={fadeOutSec.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}");
            var vf = filters.Count > 0 ? $"-af \"{string.Join(',', filters)}\"" : "";
            var result = await new FfmpegService().RunAsync($"-y -i \"{filePath}\" {vf} \"{outputPath}\"");
            return result.Success;
        }
    }

    public class FfmpegImageEngine : IImageEngine
    {
        private readonly FfprobeService _probe;
        private readonly ThumbnailService _thumbs = new();

        public FfmpegImageEngine(FfprobeService probe) => _probe = probe;

        public async Task<MediaAsset?> ProbeAsync(string filePath)
        {
            var meta = await _probe.TryGetMetadataAsync(filePath);
            if (meta == null || !meta.HasVideo) return null;
            return FfmpegVideoEngine.ToAsset(filePath, meta);
        }

        public async Task<string?> GenerateThumbnailAsync(string filePath, string outputPath, int maxWidth = 320) =>
            await _thumbs.TryGenerateAsync(filePath, outputPath, 0);

        public async Task<bool> ResizeAsync(string filePath, string outputPath, int width, int height)
        {
            var result = await new FfmpegService().RunAsync($"-y -i \"{filePath}\" -vf \"scale={width}:{height}\" \"{outputPath}\"");
            return result.Success;
        }

        public async Task<bool> ToVideoSegmentAsync(string filePath, string outputPath, double durationSeconds, double fps)
        {
            var d = Math.Max(0.1, durationSeconds).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            var f = Math.Max(1, fps).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            var result = await new FfmpegService().RunAsync($"-y -loop 1 -i \"{filePath}\" -t {d} -r {f} -pix_fmt yuv420p -c:v libx264 \"{outputPath}\"");
            return result.Success;
        }
    }
}

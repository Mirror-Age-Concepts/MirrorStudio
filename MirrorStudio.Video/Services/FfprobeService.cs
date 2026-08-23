using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using MirrorStudio.Core.Enums;
using MirrorStudio.Video.Models;

namespace MirrorStudio.Video.Services
{
    public class FfprobeService
    {
        private readonly string _ffprobePath;

        public FfprobeService(string ffprobePath = "ffprobe")
        {
            _ffprobePath = ffprobePath;
        }

        public virtual async Task<VideoMetadata> GetMetadataAsync(string filePath)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _ffprobePath,
                Arguments =
                    $"-v quiet " +
                    $"-print_format json " +
                    $"-show_format " +
                    $"-show_streams " +
                    $"\"{filePath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };

            process.Start();

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
                throw new Exception($"FFprobe failed: {error}");

            return ParseMetadata(filePath, output);
        }

        /// <summary>Non-throwing variant for UI import flows. Returns null when probing fails.</summary>
        public async Task<VideoMetadata?> TryGetMetadataAsync(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return null;
                var meta = await GetMetadataAsync(filePath);
                if (!meta.HasVideo && !meta.HasAudio) return null;
                meta.Type = ResolveType(filePath, meta);
                return meta;
            }
            catch
            {
                return null;
            }
        }

        private static MediaType ResolveType(string filePath, VideoMetadata meta)
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();

            if (ext is ".png" or ".svg" or ".webp" or ".ico") return MediaType.Graphic;
            if (ext is ".ttf" or ".otf" or ".woff" or ".woff2") return MediaType.Font;

            if (meta.HasVideo && !IsStillCodec(meta.VideoCodec)) return MediaType.Video;
            if (meta.HasAudio && !meta.HasVideo) return MediaType.Audio;
            if (meta.HasVideo) return MediaType.Image;

            return MediaType.Unknown;
        }

        public static bool IsStillCodec(string codec) =>
            codec is "mjpeg" or "png" or "bmp" or "gif" or "webp" or "tiff";

        private static VideoMetadata ParseMetadata(string filePath, string json)
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            var result = new VideoMetadata
            {
                FilePath = filePath,
                FileName = Path.GetFileName(filePath),
                FileSize = new FileInfo(filePath).Length
            };

            if (root.TryGetProperty("format", out var format))
            {
                if (format.TryGetProperty("duration", out var duration))
                    result.Duration = ParseDouble(duration.GetString());
            }

            if (root.TryGetProperty("streams", out var streams))
            {
                foreach (var stream in streams.EnumerateArray())
                {
                    if (!stream.TryGetProperty("codec_type", out var typeEl))
                        continue;

                    var codecType = typeEl.GetString();

                    if (codecType == "video" && !result.HasVideo)
                    {
                        result.HasVideo = true;

                        if (stream.TryGetProperty("width", out var width))
                            result.Width = width.GetInt32();

                        if (stream.TryGetProperty("height", out var height))
                            result.Height = height.GetInt32();

                        if (stream.TryGetProperty("codec_name", out var codec))
                            result.VideoCodec = codec.GetString() ?? "";

                        if (stream.TryGetProperty("r_frame_rate", out var fps))
                            result.FrameRate = ParseFrameRate(fps.GetString());

                        // still images report no format.duration
                        if (result.Duration <= 0 && IsStillCodec(result.VideoCodec))
                            result.Duration = 5.0;
                    }
                    else if (codecType == "audio")
                    {
                        result.HasAudio = true;

                        if (stream.TryGetProperty("codec_name", out var codec) &&
                            string.IsNullOrEmpty(result.AudioCodec))
                            result.AudioCodec = codec.GetString() ?? "";
                    }
                }
            }

            return result;
        }

        private static double ParseDouble(string? value) =>
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;

        private static double ParseFrameRate(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return 0;

            var parts = value.Split('/');
            if (parts.Length != 2)
                return ParseDouble(value);

            var numerator = ParseDouble(parts[0]);
            var denominator = ParseDouble(parts[1]);

            return denominator == 0 ? 0 : numerator / denominator;
        }
    }
}

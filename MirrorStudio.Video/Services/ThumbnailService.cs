using System.Diagnostics;

namespace MirrorStudio.Video.Services
{
    public class ThumbnailService
    {
        private readonly string _ffmpegPath;

        public ThumbnailService(string ffmpegPath = "ffmpeg")
        {
            _ffmpegPath = ffmpegPath;
        }

        /// <summary>Grabs a single frame ~1s in (or at <paramref name="atSeconds"/> for short clips).</summary>
        public virtual async Task<string> GenerateAsync(string videoPath, string outputDirectory, double atSeconds = 1.0)
        {
            Directory.CreateDirectory(outputDirectory);

            var fileName = $"{Guid.NewGuid():N}.jpg";
            var outputPath = Path.Combine(outputDirectory, fileName);

            bool ok = await TryGenerateAsync(videoPath, outputPath, atSeconds) != null;
            if (!ok)
                throw new Exception("Unable to generate thumbnail.");

            return outputPath;
        }

        /// <summary>Non-throwing variant. Returns the output path on success, null on failure.</summary>
        public async Task<string?> TryGenerateAsync(string videoPath, string outputPath, double atSeconds = 1.0)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

                var seek = Math.Max(0, atSeconds)
                    .ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

                var arguments =
                    $"-y " +
                    $"-ss {seek} " +
                    $"-i \"{videoPath}\" " +
                    $"-frames:v 1 " +
                    $"-q:v 2 " +
                    $"-vf \"scale='min(320,iw)':-2\" " +
                    $"\"{outputPath}\"";

                var startInfo = new ProcessStartInfo
                {
                    FileName = _ffmpegPath,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = startInfo };
                process.Start();
                await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode != 0 || !File.Exists(outputPath))
                {
                    try { File.Delete(outputPath); } catch { /* ignore */ }
                    return null;
                }

                return outputPath;
            }
            catch
            {
                return null;
            }
        }
    }
}

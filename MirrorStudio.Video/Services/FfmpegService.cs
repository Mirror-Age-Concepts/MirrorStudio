using System.Diagnostics;
using System.Threading.Tasks;

namespace MirrorStudio.Video.Services
{
    /// <summary>Runs ffmpeg commands (proxies, renders). Path is configurable — never hard-coded.</summary>
    public class FfmpegService
    {
        public string FFmpegPath { get; set; }

        public FfmpegService(string ffmpegPath = "ffmpeg")
        {
            FFmpegPath = ffmpegPath;
        }

        public bool IsAvailable()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = FFmpegPath,
                    Arguments = "-version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc == null) return false;
                _ = proc.StandardOutput.ReadToEndAsync();
                proc.WaitForExit(5000);
                return proc.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> GenerateProxyAsync(string sourcePath, string outputPath)
        {
            var result = await RunAsync($"-y -i \"{sourcePath}\" -c:v libx264 -preset veryfast -crf 28 -c:a aac -b:a 128k -vf \"scale=1280:-2\" \"{outputPath}\"");
            return result.Success;
        }

        public async Task<FFmpegResult> RunAsync(string arguments)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = FFmpegPath,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc == null)
                    return new FFmpegResult { Success = false, Output = "Failed to start ffmpeg." };

                var errTask = proc.StandardError.ReadToEndAsync();
                await proc.WaitForExitAsync();
                var output = await errTask;

                return new FFmpegResult { Success = proc.ExitCode == 0, Output = output };
            }
            catch (System.Exception ex)
            {
                return new FFmpegResult { Success = false, Output = ex.Message };
            }
        }
    }

    public class FFmpegResult
    {
        public bool Success { get; set; }
        public string Output { get; set; } = string.Empty;
    }
}

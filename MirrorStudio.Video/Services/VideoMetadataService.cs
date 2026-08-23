using System.Diagnostics;
using MirrorStudio.Video.Models;

namespace MirrorStudio.Video.Services
{
    /// <summary>High-level metadata facade: probe + file facts + media-type resolution.</summary>
    public class VideoMetadataService
    {
        private readonly FfprobeService _ffprobe;

        public VideoMetadataService(string ffprobePath = "ffprobe")
        {
            _ffprobe = new FfprobeService(ffprobePath);
        }

        public async Task<VideoMetadata?> GetMetadataAsync(string filePath)
        {
            var meta = await _ffprobe.TryGetMetadataAsync(filePath);
            if (meta == null) return null;

            if (meta.FileSize == 0 && File.Exists(filePath))
                meta.FileSize = new FileInfo(filePath).Length;

            return meta;
        }
    }

    /// <summary>
    /// Resolves the ffmpeg/ffprobe executables without hard-coding install paths:
    /// 1. the configured setting is used as-is when it works,
    /// 2. then PATH lookup,
    /// 3. then a scan of common install locations.
    /// </summary>
    public static class MediaEngineLocator
    {
        private static readonly string[] KnownDirs =
        {
            @"C:\ffmpeg\bin",
            @"C:\ffmpeg",
            @"C:\Program Files\ffmpeg\bin",
            @"C:\Program Files (x86)\ffmpeg\bin",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Links"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "scoop", "shims"),
            @"C:\ProgramData\chocolatey\bin"
        };

        public static string ResolveFfmpeg(string configured)
        {
            if (Works(configured, "-version")) return configured;
            return Find("ffmpeg.exe") ?? configured;
        }

        public static string ResolveFfprobe(string configured)
        {
            if (Works(configured, "-version")) return configured;
            var nextToFfmpeg = Find("ffmpeg.exe");
            if (nextToFfmpeg != null)
            {
                var probe = Path.Combine(Path.GetDirectoryName(nextToFfmpeg)!, "ffprobe.exe");
                if (File.Exists(probe)) return probe;
            }
            return Find("ffprobe.exe") ?? configured;
        }

        private static readonly Dictionary<string, string?> _findCache = new();

        private static string? Find(string exe)
        {
            if (_findCache.TryGetValue(exe, out var cached)) return cached;

            var result = FindSlow(exe);
            _findCache[exe] = result;
            return result;
        }

        private static string? FindSlow(string exe)
        {
            // 1. bundled runtime layout: MirrorStudio.exe + Runtime/FFmpeg/*.exe
            try
            {
                var runtimeDir = Path.Combine(AppContext.BaseDirectory, "Runtime", "FFmpeg");
                var bundled = Path.Combine(runtimeDir, exe);
                if (File.Exists(bundled)) return bundled;
            }
            catch { /* invalid base dir */ }

            foreach (var dir in KnownDirs)
            {
                var candidate = Path.Combine(dir, exe);
                if (File.Exists(candidate)) return candidate;
            }

            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                try
                {
                    var candidate = Path.Combine(dir, exe);
                    if (File.Exists(candidate)) return candidate;
                }
                catch { /* invalid path entry */ }
            }

            // last resort: shallow scan of Program Files trees (covers bundled installs)
            foreach (var root in new[] { @"C:\Program Files", @"C:\Program Files (x86)" })
            {
                if (!Directory.Exists(root)) continue;
                try
                {
                    foreach (var sub in Directory.EnumerateDirectories(root))
                    {
                        try
                        {
                            // check the folder itself and one bin level below it
                            var direct = Path.Combine(sub, exe);
                            if (File.Exists(direct)) return direct;
                            var binDir = Path.Combine(sub, "bin");
                            if (Directory.Exists(binDir))
                            {
                                var inBin = Path.Combine(binDir, exe);
                                if (File.Exists(inBin)) return inBin;
                            }
                        }
                        catch { /* unreadable dir */ }
                    }
                }
                catch { /* unreadable root */ }
            }
            return null;
        }

        private static bool Works(string exe, string args)
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                if (p == null) return false;
                _ = p.StandardOutput.ReadToEndAsync();
                p.WaitForExit(4000);
                return p.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }
    }
}

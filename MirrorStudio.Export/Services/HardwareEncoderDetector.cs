using MirrorStudio.Core.Projects;
using MirrorStudio.Export.Interfaces;
using MirrorStudio.Export.Models;

using System.Diagnostics;

namespace MirrorStudio.Export.Services;

/// <summary>
/// Detects hardware encoders once and caches the result (spec M8 §22):
/// NVIDIA NVENC, Intel Quick Sync, AMD AMF.
/// </summary>
public class HardwareEncoderDetector
{
    private readonly string _executable;
    private HashSet<string>? _encoders;

    public HardwareEncoderDetector(string executable = "ffmpeg")
    {
        _executable = string.IsNullOrWhiteSpace(executable) ? "ffmpeg" : executable;
    }

    /// <summary>True when the ffmpeg build contains the given encoder (e.g. h264_nvenc).</summary>
    public async Task<bool> SupportsAsync(string encoder)
    {
        try
        {
            _encoders ??= await ProbeAsync();
            return _encoders.Contains(encoder);
        }
        catch
        {
            return false;
        }
    }

    private async Task<HashSet<string>> ProbeAsync()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var process = new Process
        {
            StartInfo =
            {
                FileName = _executable,
                Arguments = "-hide_banner -encoders",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        };
        process.Start();

        while (await process.StandardOutput.ReadLineAsync() is { } line)
        {
            // format: " A.... description" — encoder id is the 3rd token
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3 && parts[0].Length > 1 && parts[0][0] != '-')
                set.Add(parts[2]);
        }
        process.WaitForExit(5000);
        return set;
    }
}

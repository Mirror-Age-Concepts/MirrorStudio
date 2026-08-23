using System.Diagnostics;
using System.Text.RegularExpressions;

namespace MirrorStudio.Audio.Services;

/// <summary>
/// Voice-over recording via ffmpeg capture devices (spec M9 §18/§19).
/// Windows uses dshow; the caller drives the countdown workflow.
/// </summary>
public partial class VoiceRecorder
{
    private readonly string _ffmpegPath;

    public VoiceRecorder(string ffmpegPath = "ffmpeg")
    {
        _ffmpegPath = string.IsNullOrWhiteSpace(ffmpegPath) ? "ffmpeg" : ffmpegPath;
    }

    public class AudioDeviceInfo
    {
        public string Name { get; init; } = "";
        public override string ToString() => Name;
    }

    /// <summary>Lists capture devices by asking ffmpeg's dshow probe.</summary>
    public async Task<IReadOnlyList<AudioDeviceInfo>> ListInputDevicesAsync(
        CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            Arguments = "-hide_banner -list_devices true -f dshow -i dummy",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi)!;
        string stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return DeviceLineRegex().Matches(stderr)
            .Select(m => new AudioDeviceInfo { Name = m.Groups["name"].Value.Replace("(audio)", "").Trim() })
            .Where(d => !string.IsNullOrEmpty(d.Name))
            .DistinctBy(d => d.Name)
            .ToList();
    }

    /// <summary>Records until cancelled; the resulting WAV lands on A4 as a new clip.</summary>
    public async Task<bool> RecordAsync(string deviceName, string outputWavPath,
        int sampleRateHz = 48000, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputWavPath)!);

        var psi = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            Arguments =
                $"-y -v error -f dshow -i audio=\"{deviceName}\" " +
                $"-ac 1 -ar {sampleRateHz} -c:a pcm_s16le \"{outputWavPath}\"",
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        await using var reg = cancellationToken.Register(() =>
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
        });

        try
        {
            await process.WaitForExitAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            // expected when stopping via cancellation
        }

        // graceful stop kills ffmpeg → nonzero exit is normal here; a valid WAV is what counts
        return File.Exists(outputWavPath) && new FileInfo(outputWavPath).Length > 44;
    }

    [GeneratedRegex(@"\""(?<name>[^""]+)\""(?:\s*\((?:audio|video)\))?")]
    private static partial Regex DeviceLineRegex();
}

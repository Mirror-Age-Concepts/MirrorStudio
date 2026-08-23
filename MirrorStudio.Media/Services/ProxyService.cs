using System.Diagnostics;

namespace MirrorStudio.Media.Services;

public class ProxyService
{
    public enum ProxyQuality
    {
        p540,
        p720,
        p1080
    }

    private readonly string _ffmpegPath;
    private readonly string _proxyDirectory;

    public ProxyService(string ffmpegPath, string proxyDirectory)
    {
        _ffmpegPath = ffmpegPath;
        _proxyDirectory = proxyDirectory;
    }

    public static int HeightFor(ProxyQuality quality) => quality switch
    {
        ProxyQuality.p540 => 540,
        ProxyQuality.p720 => 720,
        ProxyQuality.p1080 => 1080,
        _ => 720
    };

    /// <summary>
    /// Generates a scaled H.264 proxy (audio passthrough to AAC).
    /// Returns the proxy file path, or null on failure.
    /// </summary>
    public async Task<string?> TryGenerateAsync(
        string sourceFile, ProxyQuality quality, CancellationToken ct = default)
    {
        if (!File.Exists(sourceFile)) return null;

        Directory.CreateDirectory(_proxyDirectory);

        var name = $"{Path.GetFileNameWithoutExtension(sourceFile)}__proxy{(int)HeightFor(quality)}.mp4";
        var output = Path.Combine(_proxyDirectory, name);
        if (File.Exists(output)) return output;   // already proxied

        var args =
            $"-y -i \"{sourceFile}\" " +
            $"-vf \"scale=-2:{HeightFor(quality)}\" " +
            "-c:v libx264 -preset veryfast -crf 23 " +
            "-c:a aac -b:a 128k " +
            $"\"{output}\"";

        var psi = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var proc = Process.Start(psi);
            if (proc == null) return null;
            await proc.WaitForExitAsync(ct);
            return proc.ExitCode == 0 && File.Exists(output) ? output : null;
        }
        catch
        {
            return null;
        }
    }
}

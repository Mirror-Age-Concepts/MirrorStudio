using System.Diagnostics;
using MirrorStudio.Audio.Interfaces;

namespace MirrorStudio.Audio.Services;

/// <summary>
/// Decodes audio to mono 16-bit PCM via ffmpeg piping to stdout —
/// the same pattern the video pipeline uses for frame extraction.
/// </summary>
public class AudioDecoder : IAudioDecoder
{
    private readonly string _ffmpegPath;

    public AudioDecoder(string ffmpegPath = "ffmpeg")
    {
        _ffmpegPath = string.IsNullOrWhiteSpace(ffmpegPath) ? "ffmpeg" : ffmpegPath;
    }

    public async Task<short[]?> DecodeMono16Async(string filePath, int sampleRateHz,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return null;

        var psi = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            Arguments =
                $"-v error -i \"{filePath}\" -vn -map 0:a:0? -ac 1 -ar {sampleRateHz} -f s16le pipe:1",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        using var ms = new MemoryStream();
        await process.StandardOutput.BaseStream.CopyToAsync(ms, cancellationToken);

        // drain stderr on another thread so a full pipe can never deadlock us
        _ = process.StandardError.ReadToEndAsync(CancellationToken.None);

        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0 || ms.Length < 2)
            return null;

        var bytes = ms.ToArray();
        var samples = new short[bytes.Length / 2];
        for (int i = 0; i < samples.Length; i++)
            samples[i] = BitConverter.ToInt16(bytes, i * 2);

        return samples;
    }
}

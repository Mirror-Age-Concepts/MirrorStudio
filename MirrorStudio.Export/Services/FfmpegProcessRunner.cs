using System.Diagnostics;

namespace MirrorStudio.Export.Services;

/// <summary>
/// Runs ffmpeg and streams its stderr lines (ffmpeg writes progress to
/// stderr, not stdout — spec M8 §13).
/// </summary>
public class FfmpegProcessRunner
{
    private readonly string _executable;

    public FfmpegProcessRunner(string executable = "ffmpeg")
    {
        _executable = string.IsNullOrWhiteSpace(executable) ? "ffmpeg" : executable;
    }

    public async Task<int> RunAsync(
        string arguments,
        IProgress<string>? output = null,
        CancellationToken cancellationToken = default)
    {
        using var process =
            new Process
            {
                StartInfo =
                {
                    FileName = _executable,
                    Arguments = arguments,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

        process.Start();

        // cancellation kills the encode mid-flight
        await using var _ = cancellationToken.Register(() =>
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
        });

        while (!process.StandardError.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var line =
                await process.StandardError.ReadLineAsync(cancellationToken);

            if (!string.IsNullOrWhiteSpace(line))
                output?.Report(line);
        }

        await process.WaitForExitAsync(
            cancellationToken);

        return process.ExitCode;
    }
}

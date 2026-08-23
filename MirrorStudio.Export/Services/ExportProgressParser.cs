using System.Diagnostics;
using System.Text.RegularExpressions;
using MirrorStudio.Export.Models;

using System.Globalization;

namespace MirrorStudio.Export.Services;

/// <summary>
/// Parses FFmpeg stderr for progress: <c>time=00:01:23.45</c> lines
/// drive percentage + ETA against the plan's total duration.
/// </summary>
public static partial class ExportProgressParser
{
    [GeneratedRegex(@"time=(\d+):(\d{2}):(\d{2}(?:\.\d+)?)")]
    private static partial Regex TimeRegex();

    /// <summary>Extracts the encoded media time from an ffmpeg stderr line.</summary>
    public static bool TryParseTime(string line, out TimeSpan time)
    {
        time = default;
        if (string.IsNullOrEmpty(line)) return false;

        var match = TimeRegex().Match(line);
        if (!match.Success) return false;

        time = TimeSpan.FromHours(int.Parse(match.Groups[1].Value))
             + TimeSpan.FromMinutes(int.Parse(match.Groups[2].Value))
             + TimeSpan.FromSeconds(double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture));
        return true;
    }

    /// <summary>
    /// Builds an <see cref="ExportProgress"/> from a raw stderr line.
    /// Returns null when the line carries no timing information.
    /// </summary>
    public static ExportProgress? ToProgress(string line, double totalSeconds, Stopwatch elapsed)
    {
        if (!TryParseTime(line, out var rendered) || totalSeconds <= 0)
            return null;

        var pct = Math.Clamp(rendered.TotalSeconds / totalSeconds * 100.0, 0, 100);
        TimeSpan remaining = default;
        if (pct > 0.5 && elapsed.ElapsedMilliseconds > 250)
        {
            var rate = elapsed.Elapsed.TotalSeconds / (pct / 100.0);
            remaining = TimeSpan.FromSeconds(Math.Max(0, rate * (1 - pct / 100.0)));
        }

        return new ExportProgress
        {
            Percentage = pct,
            RenderedDuration = rendered,
            EstimatedRemaining = remaining,
            Status = line.Trim(),
        };
    }
}

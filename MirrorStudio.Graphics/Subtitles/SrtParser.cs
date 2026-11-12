using System.Globalization;
using System.Text;

namespace MirrorStudio.Graphics.Subtitles;

/// <summary>SubRip (.srt) — blank-line separated blocks, comma milliseconds.</summary>
public static class SrtParser
{
    public static SubtitleTrack Parse(string content)
    {
        var track = new SubtitleTrack();
        if (string.IsNullOrWhiteSpace(content)) return track;

        foreach (var block in SplitBlocks(content))
        {
            var lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (lines.Length == 0) continue;

            int first = 0;
            // optional leading index line
            if (lines[0].All(char.IsDigit)) first = 1;
            if (lines.Length <= first || !TryTimes(lines[first], out var start, out var end))
                continue;

            var text = string.Join("\n", lines.Skip(first + 1));
            if (text.Length == 0) continue;

            track.Items.Add(new Subtitle
            {
                Index = track.Items.Count + 1,
                Start = start,
                End = end,
                Text = text
            });
        }
        return track;
    }

    public static string Write(SubtitleTrack track)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < track.Items.Count; i++)
        {
            var s = track.Items[i];
            sb.AppendLine((i + 1).ToString(CultureInfo.InvariantCulture));
            sb.AppendLine($"{Fmt(s.Start)} --> {Fmt(s.End)}");
            sb.AppendLine(s.Text);
            sb.AppendLine();
        }
        return sb.ToString();

        static string Fmt(TimeSpan t) => $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00},{t.Milliseconds:000}";
    }

    internal static IEnumerable<string> SplitBlocks(string content)
    {
        // normalize line endings, then split on blank lines
        var normalized = content.Replace("\r\n", "\n").Replace("\r", "\n");
        return normalized.Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
                         .Select(b => b.Trim());
    }

    internal static bool TryTimes(string line, out TimeSpan start, out TimeSpan end)
    {
        start = end = default;
        var sep = line.IndexOf("-->", StringComparison.Ordinal);
        if (sep < 0) return false;
        return TryTime(line[..sep].Trim(), out start)
            && TryTime(line[(sep + 3)..].Trim(), out end);
    }

    internal static bool TryTime(string s, out TimeSpan t)
    {
        t = default;
        s = s.Replace(',', '.');
        // HH:MM:SS.mmm  or MM:SS.mmm
        var parts = s.Split(':');
        if (parts.Length is not (2 or 3)) return false;
        double seconds;
        if (!double.TryParse(parts[^1], NumberStyles.Float, CultureInfo.InvariantCulture, out seconds))
            return false;
        if (!int.TryParse(parts[^2], out int minutes)) return false;
        int hours = parts.Length == 3 && int.TryParse(parts[0], out int h) ? h : 0;
        if (parts.Length != 3) hours = 0;
        else if (!int.TryParse(parts[0], out hours)) return false;

        t = new TimeSpan(hours, minutes, 0) + TimeSpan.FromSeconds(seconds);
        return true;
    }
}

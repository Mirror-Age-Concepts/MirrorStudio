namespace MirrorStudio.Graphics.Subtitles;

/// <summary>WebVTT (.vtt) — WEBVTT header, dot milliseconds, optional cue settings and tags.</summary>
public static class VttParser
{
    public static SubtitleTrack Parse(string content)
    {
        var track = new SubtitleTrack { Name = "WebVTT" };
        if (string.IsNullOrWhiteSpace(content)) return track;

        foreach (var block in SrtParser.SplitBlocks(content))
        {
            var lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (lines.Length == 0) continue;
            if (lines[0].StartsWith("WEBVTT", StringComparison.Ordinal)) continue;
            if (lines[0].StartsWith("NOTE", StringComparison.Ordinal)) continue;
            if (lines[0].StartsWith("STYLE", StringComparison.Ordinal)) continue;

            int first = 0;
            // optional cue identifier line before the timing line
            if (!lines[0].Contains("-->"))
            {
                first = 1;
                if (lines.Length <= first || !lines[first].Contains("-->")) continue;
            }
            if (!SrtParser.TryTimes(lines[first], out var start, out var end))
                continue;

            var text = string.Join("\n", lines.Skip(first + 1));
            text = StripTags(text);
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

    /// <summary>Remove VTT markup: &lt;c.cls&gt;, &lt;00:00:01.000&gt;, &lt;/i&gt;…</summary>
    internal static string StripTags(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        int depth = 0;
        foreach (var ch in text)
        {
            if (ch == '<') { depth++; continue; }
            if (ch == '>') { depth--; continue; }
            if (depth == 0) sb.Append(ch);
        }
        return sb.ToString();
    }

    public static string Write(SubtitleTrack track)
    {
        var sb = new System.Text.StringBuilder("WEBVTT\n\n");
        for (int i = 0; i < track.Items.Count; i++)
        {
            var s = track.Items[i];
            sb.AppendLine((i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
            sb.AppendLine($"{Fmt(s.Start)} --> {Fmt(s.End)}");
            sb.AppendLine(s.Text);
            sb.AppendLine();
        }
        return sb.ToString();

        static string Fmt(TimeSpan t) =>
            $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}.{t.Milliseconds:000}";
    }
}

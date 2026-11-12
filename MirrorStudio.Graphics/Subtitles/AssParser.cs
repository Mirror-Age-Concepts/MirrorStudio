using System.Globalization;

namespace MirrorStudio.Graphics.Subtitles;

/// <summary>
/// Advanced SubStation Alpha (.ass) — [Events]/Dialogue lines with Format-driven
/// field order, H:MM:SS.cc centisecond timing, override tags in {...} and \N breaks.
/// Also captures the first [V4+ Styles] entry into the track's default style.
/// </summary>
public static class AssParser
{
    public static SubtitleTrack Parse(string content)
    {
        var track = new SubtitleTrack { Name = "ASS" };
        if (string.IsNullOrWhiteSpace(content)) return track;

        string[]? eventFormat = null;
        bool inEvents = false;
        bool inStyles = false;
        string[]? styleFormat = null;

        foreach (var rawLine in content.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;

            if (line.Length > 0 && line[0] == '[')
            {
                var section = line.Trim('[', ']').ToLowerInvariant();
                inEvents = section is "events" or "v4+ events";
                inStyles = section.StartsWith("v4");
                continue;
            }

            int colon = line.IndexOf(':');
            if (colon <= 0) continue;
            var key = line[..colon].Trim();
            var value = line[(colon + 1)..];

            if (inEvents && key.Equals("Format", StringComparison.OrdinalIgnoreCase))
            {
                eventFormat = value.Split(',').Select(f => f.Trim()).ToArray();
            }
            else if (inEvents && key.Equals("Dialogue", StringComparison.OrdinalIgnoreCase))
            {
                var sub = ParseDialogue(value, eventFormat);
                if (sub != null) track.Items.Add(sub);
            }
            else if (inStyles && key.Equals("Format", StringComparison.OrdinalIgnoreCase))
            {
                styleFormat = value.Split(',').Select(f => f.Trim()).ToArray();
            }
            else if (inStyles && key.Equals("Style", StringComparison.OrdinalIgnoreCase))
            {
                ApplyStyle(track, value, styleFormat);
            }
        }

        for (int i = 0; i < track.Items.Count; i++)
            track.Items[i].Index = i + 1;
        return track;
    }

    private static Subtitle? ParseDialogue(string value, string[]? format)
    {
        // Text is the LAST field and may contain commas — split with a cap.
        var fields = format ?? DefaultEventFormat;
        var parts = SplitDialogue(value, fields.Length);
        if (parts.Length < fields.Length) return null;

        int iStart = ArrayIndex(fields, "Start");
        int iEnd = ArrayIndex(fields, "End");
        int iText = ArrayIndex(fields, "Text");
        if (iStart < 0 || iEnd < 0 || iText < 0) return null;

        if (!TryAssTime(parts[iStart], out var start) || !TryAssTime(parts[iEnd], out var end))
            return null;

        var text = CleanText(parts[iText]);
        if (text.Length == 0) return null;

        return new Subtitle { Start = start, End = end, Text = text };
    }

    internal static readonly string[] DefaultEventFormat =
        { "Layer", "Start", "End", "Style", "Name", "MarginL", "MarginR", "MarginV", "Effect", "Text" };

    /// <summary>Split into exactly n fields; the last field keeps embedded commas.</summary>
    internal static string[] SplitDialogue(string value, int n)
    {
        var result = new List<string>(n);
        int idx = 0;
        while (result.Count < n - 1)
        {
            int comma = value.IndexOf(',', idx);
            if (comma < 0) break;
            result.Add(value[idx..comma]);
            idx = comma + 1;
        }
        result.Add(value[Math.Min(idx, value.Length)..]);
        return result.ToArray();
    }

    private static int ArrayIndex(string[] format, string name) =>
        Array.FindIndex(format, f => f.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>H:MM:SS.cc (centiseconds).</summary>
    internal static bool TryAssTime(string s, out TimeSpan t)
    {
        t = default;
        var parts = s.Trim().Split(':');
        if (parts.Length != 3) return false;
        if (!int.TryParse(parts[0], out int h)) return false;
        if (!int.TryParse(parts[1], out int m)) return false;
        var secParts = parts[2].Split('.');
        if (secParts.Length == 0 || !int.TryParse(secParts[0], out int sec)) return false;
        int cs = secParts.Length > 1 && int.TryParse(secParts[1].PadRight(2, '0'), out int c) ? c : 0;
        t = new TimeSpan(0, h, m, sec, cs * 10);
        return true;
    }

    /// <summary>Strip {...} override blocks; \N and \n become line breaks.</summary>
    internal static string CleanText(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        int depth = 0;
        foreach (var ch in text)
        {
            if (ch == '{') { depth++; continue; }
            if (ch == '}') { depth--; continue; }
            if (depth > 0) continue;
            sb.Append(ch);
        }

        var cleaned = sb.ToString()
            .Replace("\\N", "\n")
            .Replace("\\n", "\n")
            .Trim();
        return cleaned;
    }

    private static void ApplyStyle(SubtitleTrack track, string styleValue, string[]? format)
    {
        var fields = format ?? DefaultStyleFormat;
        var parts = styleValue.Split(',');
        if (parts.Length < fields.Length) return;

        string Get(string name) => parts[ArrayIndex(fields, name)].Trim();

        try
        {
            var style = new Models.TextStyle
            {
                FontFamily = Get("Fontname"),
                FontSize = double.Parse(Get("Fontsize"), CultureInfo.InvariantCulture),
                Bold = Get("Bold") != "0",
                FillColor = AssColor(Get("PrimaryColour")),
                StrokeColor = AssColor(Get("OutlineColour"))
            };
            if (!string.IsNullOrEmpty(style.FontFamily))
                track.Style ??= style;
        }
        catch
        {
            // malformed style line — subtitles still load unstyled
        }
    }

    internal static readonly string[] DefaultStyleFormat =
        { "Name", "Fontname", "Fontsize", "PrimaryColour", "SecondaryColour", "OutlineColour",
          "BackColour", "Bold", "Italic", "Underline", "StrikeOut", "ScaleX", "ScaleY",
          "Spacing", "Angle", "BorderStyle", "Outline", "Shadow", "Alignment",
          "MarginL", "MarginR", "MarginV", "Encoding" };

    /// <summary>&HAABBGGRR → #RRGGBB.</summary>
    internal static string AssColor(string assColor)
    {
        var hex = assColor.Trim().Replace("&H", "").Replace("&", "");
        if (hex.Length >= 6 && hex[^6..].All(Uri.IsHexDigit))
        {
            var bb = hex[^6..^4];
            var gg = hex[^4..^2];
            var rr = hex[^2..];
            return $"#{rr}{gg}{bb}".ToUpperInvariant();
        }
        return "#FFFFFF";
    }

    public static string Write(SubtitleTrack track)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[Script Info]");
        sb.AppendLine("ScriptType: v4.00+");
        sb.AppendLine();
        sb.AppendLine("[V4+ Styles]");
        sb.AppendLine("Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding");
        var st = track.Style;
        sb.AppendLine(
            $"Style: Default,{st?.FontFamily ?? "Arial"},{N(st?.FontSize ?? 48)},&H00FFFFFF,&H000000FF,{AssOut(st)},&H00000000,-1,0,0,0,100,100,0,0,1,2,1,2,10,10,30,1");
        sb.AppendLine();
        sb.AppendLine("[Events]");
        sb.AppendLine("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text");

        foreach (var s in track.Items)
            sb.AppendLine($"Dialogue: 0,{Fmt(s.Start)},{Fmt(s.End)},Default,,0,0,0,,{Escape(s.Text)}");
        return sb.ToString();

        static string N(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);

        static string AssOut(Models.TextStyle? st) =>
            "&H00" + (st?.StrokeColor ?? "#000000").TrimStart('#').ToLowerInvariant() + "00";

        static string Fmt(TimeSpan t) =>
            $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}.{t.Milliseconds / 10:00}";

        static string Escape(string text) =>
            text.Replace("\r\n", "\\N").Replace("\n", "\\N");
    }
}

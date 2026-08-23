using System.Globalization;
using System.Text;

namespace MirrorStudio.Video.Color;

/// <summary>
/// A parsed .cube 3D LUT (spec M10 §10). We parse and validate the table here;
/// at render time the file path is handed to ffmpeg's lut3d filter.
/// </summary>
public class LutFile
{
    public string Title { get; set; } = "";

    /// <summary>LUT_3D_SIZE — the lattice dimension (e.g. 33 → 33³ entries).</summary>
    public int Size { get; private set; } = 0;

    /// <summary>Table rows in .cube order: R fastest-varying, then G, then B, values 0..1.</summary>
    public List<(float R, float G, float B)> Table { get; } = [];

    public bool IsValid => Size > 1 && Table.Count == Size * Size * Size;

    /// <summary>Parses .cube text; throws FormatException with a helpful line number on bad data.</summary>
    public static LutFile Parse(string content)
    {
        var lut = new LutFile();
        int lineNumber = 0;
        float max = 0f;

        foreach (var rawLine in (content ?? "").Split('\n'))
        {
            lineNumber++;
            var line = rawLine.Trim();

            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (line.StartsWith("TITLE", StringComparison.OrdinalIgnoreCase))
            {
                lut.Title = line.Length > 5 ? line[5..].Trim('"', ' ') : "";
                continue;
            }
            if (line.StartsWith("LUT_3D_SIZE", StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[^1], out int size) || size < 2 || size > 256)
                    throw new FormatException($"Line {lineNumber}: invalid LUT_3D_SIZE.");
                lut.Size = size;
                continue;
            }
            if (line.StartsWith("DOMAIN_", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("LUT_1D_SIZE", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("LUT_1D_INPUT_RANGE", StringComparison.OrdinalIgnoreCase))
            {
                if (line.Contains("1D", StringComparison.OrdinalIgnoreCase))
                    throw new FormatException("Line {lineNumber}: 1D LUTs are not supported.");
                continue; // DOMAIN_MIN/MAX accepted as-is (default 0..1)
            }

            var parts = line.Split(' ', '\t', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3)
                throw new FormatException($"Line {lineNumber}: expected three floats.");

            if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var r) ||
                !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var g) ||
                !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var b))
                throw new FormatException($"Line {lineNumber}: unparseable RGB values.");

            lut.Table.Add((r, g, b));
            max = Math.Max(max, Math.Max(r, Math.Max(g, b)));
        }

        if (lut.Size == 0)
            throw new FormatException("Missing LUT_3D_SIZE header.");
        if (!lut.IsValid)
            throw new FormatException(
                $"Expected {lut.Size * lut.Size * lut.Size} entries but found {lut.Table.Count}.");

        // domain sanity: standard LUTs stay within 0..1
        if (max > 10f)
            throw new FormatException("LUT values exceed a plausible 0..1 range.");

        return lut;
    }

    /// <summary>Serializes back to .cube text (used when normalizing imported files).</summary>
    public string ToCubeText()
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.IsNullOrEmpty(Title) ? "TITLE \"MirrorStudio LUT\"" : $"TITLE \"{Title}\"");
        sb.AppendLine($"LUT_3D_SIZE {Size}");
        sb.AppendLine("DOMAIN_MIN 0.0 0.0 0.0");
        sb.AppendLine("DOMAIN_MAX 1.0 1.0 1.0");
        foreach (var (r, g, b) in Table)
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"{r.ToString("0.000000")} {g.ToString("0.000000")} {b.ToString("0.000000")}"));
        return sb.ToString();
    }
}

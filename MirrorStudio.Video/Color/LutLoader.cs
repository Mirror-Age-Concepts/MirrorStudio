namespace MirrorStudio.Video.Color;

/// <summary>
/// Loads and caches .cube LUTs, validating structure before a file is allowed
/// anywhere near the render pipeline (spec M10 §10).
/// </summary>
public class LutLoader
{
    private readonly Dictionary<string, LutFile> _cache = new(StringComparer.OrdinalIgnoreCase);

    public bool TryLoad(string filePath, out LutFile? lut)
    {
        lut = null;
        try
        {
            if (!File.Exists(filePath))
                return false;

            string full = Path.GetFullPath(filePath);
            if (_cache.TryGetValue(full, out var cached) &&
                new FileInfo(full).LastWriteTimeUtc == _cacheTimestamps[full])
            {
                lut = cached;
                return true;
            }

            var parsed = LutFile.Parse(File.ReadAllText(full));
            if (!parsed.IsValid)
                return false;

            _cache[full] = parsed;
            _cacheTimestamps[full] = new FileInfo(full).LastWriteTimeUtc;
            lut = parsed;
            return true;
        }
        catch
        {
            // parse errors / IO problems → caller treats the LUT as absent
            return false;
        }
    }

    private readonly Dictionary<string, DateTime> _cacheTimestamps = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Common import locations surfaced in the LUT browser.</summary>
    public static IReadOnlyList<string> FindAvailableLuts(params string[] extraFolders)
    {
        var results = new List<string>();
        var folders = new List<string>(extraFolders)
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MirrorStudio", "LUTs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MirrorStudio", "LUTs"),
        };

        foreach (var folder in folders.Where(Directory.Exists))
        {
            try
            {
                results.AddRange(Directory.EnumerateFiles(folder, "*.cube", SearchOption.AllDirectories));
            }
            catch { /* unreadable subtree — skip */ }
        }

        return results.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}

namespace MirrorStudio.Audio.Models;

/// <summary>
/// Duck music under dialogue automatically (spec M9 §22): when a key track
/// (e.g. A1 Dialogue) is audible, ducked tracks (e.g. A2 Music) drop by AmountDb,
/// with the given attack/release shaping.
/// </summary>
public class DuckingSettings
{
    public bool Enabled { get; set; }

    /// <summary>How much quieter ducked tracks become while dialogue plays.</summary>
    public double AmountDb { get; set; } = -12.0;

    public double AttackSec { get; set; } = 0.15;

    public double ReleaseSec { get; set; } = 0.50;

    /// <summary>Track indexes whose clips trigger ducking (e.g. [0] = A1).</summary>
    public List<int> KeyTrackIndexes { get; set; } = [];
}

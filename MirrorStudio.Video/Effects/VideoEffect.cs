namespace MirrorStudio.Video.Effects;

/// <summary>
/// Base for every visual effect (spec M10 §2). Effects serialize to FFmpeg
/// filters for export while the preview renderer approximates them on the
/// GPU-composited visual layer (§30/§31).
/// </summary>
public abstract class VideoEffect
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "";

    public bool Enabled { get; set; } = true;

    /// <summary>Optional effect-local timing (seconds, clip-relative).</summary>
    public double StartTime { get; set; }

    public double Duration { get; set; }

    /// <summary>FFmpeg filter fragment. Null when the effect currently no-ops.</summary>
    public abstract string? BuildFfmpegFilter();

    /// <summary>
    /// Canonical processing order when the user hasn't reordered the stack:
    /// source → crop → transform → color → blur → sharpen → LUT/keying → stylize → opacity.
    /// </summary>
    public static readonly string[] CanonicalOrder =
    [
        "Crop", "Transform", "ColorCorrection", "Blur", "Sharpen",
        "Lut", "ChromaKey", "Glow", "Vignette", "Noise", "Opacity",
    ];

    internal static int OrderOf(string typeName) =>
        Array.IndexOf(CanonicalOrder, typeName) is >= 0 and var i ? i : CanonicalOrder.Length;

    /// <summary>Order key derived from the concrete class ("BlurEffect" → "Blur").</summary>
    internal static int OrderOf(VideoEffect effect) =>
        OrderOf(effect.GetType().Name.Replace("Effect", string.Empty));
}

/// <summary>
/// An ordered effect stack for one clip (§28/§29). Order matters: cropping before
/// or after a color pass yields different frames, so the list is authoritative.
/// </summary>
public class EffectStack
{
    private readonly List<VideoEffect> _effects = [];

    public IReadOnlyList<VideoEffect> Items => _effects;

    public void Add(VideoEffect effect)
    {
        if (effect.Enabled)
            effect.Enabled = true;
        _effects.Add(effect);
        SortCanonical();
    }

    public void Insert(int index, VideoEffect effect) =>
        _effects.Insert(Math.Clamp(index, 0, _effects.Count), effect);

    public bool Remove(VideoEffect effect) => _effects.Remove(effect);

    public void MoveUp(VideoEffect effect)
    {
        int i = _effects.IndexOf(effect);
        if (i > 0) (_effects[i - 1], _effects[i]) = (_effects[i], _effects[i - 1]);
    }

    public void MoveDown(VideoEffect effect)
    {
        int i = _effects.IndexOf(effect);
        if (i >= 0 && i < _effects.Count - 1) (_effects[i + 1], _effects[i]) = (_effects[i], _effects[i + 1]);
    }

    /// <summary>Re-orders entries into the canonical pipeline order (stable).</summary>
    public void SortCanonical() =>
        StableSort(_effects, VideoEffect.OrderOf);

    private static void StableSort<T>(List<T> list, Func<T, int> key)
    {
        for (int i = 1; i < list.Count; i++)
        {
            var item = list[i];
            int k = key(item);
            int j = i - 1;
            while (j >= 0 && key(list[j]) > k)
            {
                list[j + 1] = list[j];
                j--;
            }
            list[j + 1] = item;
        }
    }

    /// <summary>All enabled effects joined with commas — one chain segment.</summary>
    public string BuildFilterChain()
    {
        var parts = _effects
            .Where(e => e.Enabled)
            .Select(e => e.BuildFfmpegFilter())
            .Where(f => !string.IsNullOrEmpty(f))
            .ToList();

        return parts.Count == 0 ? "" : string.Join(",", parts);
    }
}

namespace MirrorStudio.Video.Keyframes;

/// <summary>A single animated property sample (spec M10 §15).</summary>
public class Keyframe<T>
{
    public double Time { get; set; }

    public T Value { get; set; } = default!;

    /// <summary>"Linear" or "Smooth" (smoothstep easing).</summary>
    public string Easing { get; set; } = "Linear";

    public Keyframe() { }

    public Keyframe(double time, T value, string easing = "Linear")
    {
        Time = time;
        Value = value;
        Easing = easing;
    }
}

/// <summary>
/// An animatable property: ordered samples with interpolation (spec M10 §15–§17).
/// Used for animated position, scale (Ken Burns), opacity and any effect amount.
/// </summary>
public class KeyframeTrack<T>
{
    public List<Keyframe<T>> Keyframes { get; set; } = [];

    public void Add(double time, T value, string easing = "Linear") =>
        Keyframes.Add(new Keyframe<T>(time, value, easing));

    public bool IsEmpty => Keyframes.Count == 0;

    /// <summary>
    /// Value at a given time: clamped before the first / after the last key,
    /// linearly (or smoothstep) interpolated between surrounding keys.
    /// </summary>
    public T GetValue(double time)
    {
        if (Keyframes.Count == 0)
            return default!;

        var keys = Keyframes.OrderBy(k => k.Time).ToList();

        if (time <= keys[0].Time) return keys[0].Value;
        if (time >= keys[^1].Time) return keys[^1].Value;

        for (int i = 0; i < keys.Count - 1; i++)
        {
            var a = keys[i];
            var b = keys[i + 1];
            if (time >= a.Time && time <= b.Time)
            {
                if (EqualityComparer<T>.Default.Equals(a.Value, b.Value))
                    return a.Value;

                double f = b.Time > a.Time ? (time - a.Time) / (b.Time - a.Time) : 0;
                f = a.Easing == "Smooth" ? SmoothStep(f) : f;
                return Lerp(a.Value, b.Value, f);
            }
        }

        return keys[^1].Value;
    }

    private static double SmoothStep(double t) => t * t * (3.0 - 2.0 * t);

    private static T Lerp(T a, T b, double f)
    {
        if (a is double d) return (T)(object)(d + ((double)(object)b! - d) * f);
        if (a is float s) return (T)(object)(s + ((float)(object)b! - s) * (float)f);
        throw new NotSupportedException($"Keyframe interpolation is not supported for {typeof(T)}.");
    }
}

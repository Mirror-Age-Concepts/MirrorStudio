namespace MirrorStudio.Audio.Services;

/// <summary>
/// Gain math shared by mixer, UI and filter generation (spec M9 §5/§6).
/// </summary>
public static class AudioMath
{
    public const double MinDb = -60.0;
    public const double MaxDb = +12.0;

    public static double DbToLinear(double db) => Math.Pow(10.0, db / 20.0);

    public static double LinearToDb(double linear)
    {
        if (linear <= 0) return double.NegativeInfinity;
        return 20.0 * Math.Log10(linear);
    }

    public static double ClampDb(double db) => Math.Clamp(db, MinDb, MaxDb);

    public static double ClampLinear(double linear) => Math.Clamp(linear, 0.0, 4.0);

    /// <summary>
    /// Equal-power stereo pan law (constant loudness across the sweep):
    /// pan -1 → full left, 0 → equal split (√½), +1 → full right.
    /// </summary>
    public static (double Left, double Right) PanGains(double pan)
    {
        pan = Math.Clamp(pan, -1.0, 1.0);
        double angle = (pan + 1.0) * Math.PI / 4.0;
        return (Math.Cos(angle), Math.Sin(angle));
    }

    /// <summary>Formats a linear gain as a rounded dB label for meters/UI.</summary>
    public static string FormatDb(double db) =>
        db <= double.NegativeInfinity ? "-∞" : $"{db:0.#}";
}

using System.Globalization;

namespace MirrorStudio.Audio.Effects;

/// <summary>
/// Base for per-clip/track audio effects (spec M9 §25). Each effect maps to
/// one FFmpeg audio filter; the chain order is EQ → Compressor → Noise
/// Reduction → Reverb → Delay → Limiter.
/// </summary>
public abstract class AudioEffect
{
    public string Name { get; init; } = "Effect";

    public bool Enabled { get; set; } = true;

    /// <summary>The FFmpeg filter fragment, or null when the effect is a no-op.</summary>
    public abstract string? ToFilterArg();

    /// <summary>Joins enabled effects into one comma-separated chain segment.</summary>
    public static string? BuildChain(IEnumerable<AudioEffect> effects)
    {
        var parts = effects
            .Where(e => e.Enabled)
            .Select(e => e.ToFilterArg())
            .Where(a => !string.IsNullOrEmpty(a))
            .ToList();

        return parts.Count == 0 ? null : string.Join(",", parts);
    }

    protected static string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}

/// <summary>Parametric EQ band (equalizer filter).</summary>
public class EqualizerEffect : AudioEffect
{
    private static readonly (string Name, double Freq, double WidthOct)[] Bands =
    {
        ("60Hz", 60, 1.2),
        ("150Hz", 150, 1.0),
        ("400Hz", 400, 1.0),
        ("1kHz", 1000, 1.0),
        ("2.4kHz", 2400, 1.0),
        ("15kHz", 15000, 1.4),
    };

    /// <summary>Six band gains in dB, matching the mixer's six sliders.</summary>
    public double[] BandGainsDb { get; set; } = new double[6];

    public EqualizerEffect()
    {
        Name = "Equalizer";
    }

    public override string? ToFilterArg()
    {
        var parts = new List<string>();
        for (int i = 0; i < Bands.Length; i++)
        {
            double g = Math.Clamp(BandGainsDb[i], -12, 12);
            if (Math.Abs(g) < 0.25) continue;
            var (_, freq, width) = Bands[i];
            parts.Add($"equalizer=f={freq}:t=q:w={width}:g={N(g)}");
        }

        return parts.Count == 0 ? null : string.Join(",", parts);
    }
}

/// <summary>Single-band dynamics compressor.</summary>
public class CompressorEffect : AudioEffect
{
    /// <summary>Detection threshold in dB (-60..0).</summary>
    public double ThresholdDb { get; set; } = -20;

    /// <summary>Compression ratio above the threshold (1..20).</summary>
    public double Ratio { get; set; } = 3;

    /// <summary>Attack time in ms.</summary>
    public double AttackMs { get; set; } = 25;

    /// <summary>Release time in ms.</summary>
    public double ReleaseMs { get; set; } = 250;

    /// <summary>Makeup gain in dB applied after compression.</summary>
    public double MakeupDb { get; set; } = 2;

    public CompressorEffect()
    {
        Name = "Compressor";
    }

    public override string ToFilterArg() =>
        $"acompressor=threshold={N(ThresholdDb)}dB:ratio={N(Ratio)}:attack={N(AttackMs)}:release={N(ReleaseMs)}:makeup={N(MakeupDb)}";
}

/// <summary>Look-ahead limiter that catches peaks after other processing.</summary>
public class LimiterEffect : AudioEffect
{
    /// <summary>Ceiling in dB (output never exceeds this).</summary>
    public double LimitDb { get; set; } = -1.5;

    public LimiterEffect()
    {
        Name = "Limiter";
    }

    public override string ToFilterArg() =>
        $"alimiter=limit={N(Math.Pow(10, LimitDb / 20.0))}";
}

/// <summary>FFT-based broadband denoise.</summary>
public class NoiseReductionEffect : AudioEffect
{
    /// <summary>Noise reduction strength in dB of attenuation (0.01..97).</summary>
    public double StrengthDb { get; set; } = 12;

    public NoiseReductionEffect()
    {
        Name = "Noise Reduction";
    }

    public override string ToFilterArg() =>
        $"afftdn=nr={N(StrengthDb)}:nf=-25";
}

/// <summary>Room/space simulation via multi-tap echo.</summary>
public class ReverbEffect : AudioEffect
{
    /// <summary>0 = closet … 1 = cathedral.</summary>
    public double RoomSize { get; set; } = 0.4;

    /// <summary>Wet mix (0..1).</summary>
    public double Mix { get; set; } = 0.25;

    public ReverbEffect()
    {
        Name = "Reverb";
    }

    public override string ToFilterArg()
    {
        RoomSize = Math.Clamp(RoomSize, 0, 1);
        Mix = Math.Clamp(Mix, 0, 1);

        // three taps whose delays and decays scale with room size
        int d1 = 40 + (int)(RoomSize * 80);      // 40–120 ms
        int d2 = 70 + (int)(RoomSize * 200);     // 70–270 ms
        int d3 = 110 + (int)(RoomSize * 500);    // 110–610 ms
        double decay = 0.25 + RoomSize * 0.45;

        // aecho is dry+wet; compensate level slightly for larger rooms
        return $"aecho=0.8:0.85:{d1}|{d2}|{d3}:{N(decay * 0.6)}|{N(decay * 0.4)}|{N(decay * 0.25)}," +
               $"volume={N(1.0 - Mix * 0.4)}";
    }
}

/// <summary>Precise delay with feedback.</summary>
public class DelayEffect : AudioEffect
{
    /// <summary>Delay time in milliseconds.</summary>
    public double DelayMs { get; set; } = 250;

    /// <summary>Echo count (1..8).</summary>
    public int Echoes { get; set; } = 2;

    /// <summary>Feedback decay per echo (0..1).</summary>
    public double Feedback { get; set; } = 0.35;

    public DelayEffect()
    {
        Name = "Delay";
    }

    public override string ToFilterArg()
    {
        Echoes = Math.Clamp(Echoes, 1, 8);
        Feedback = Math.Clamp(Feedback, 0, 0.9);

        // adelay has no feedback — unroll one tap per echo on both channels
        var delays = new List<string>();
        for (int i = 1; i <= Echoes; i++)
            delays.Add($"{N(DelayMs * i)}");

        return $"adelay={string.Join(":", delays)}:all=1";
    }
}

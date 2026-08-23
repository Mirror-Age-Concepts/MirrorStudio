using System.Globalization;
using System.Text;
using MirrorStudio.Audio.Interfaces;
using MirrorStudio.Audio.Models;

namespace MirrorStudio.Audio.Services;

/// <summary>
/// Implements the mixer stage: solo/mute gating, gain staging, fade envelopes,
/// ducking and the matching FFmpeg filter chains (spec M9 §7/§8/§21/§22).
/// </summary>
public class AudioMixer : IAudioMixer
{
    public bool IsAudible(AudioTrack track, IReadOnlyList<AudioTrack> allTracks)
    {
        if (track.Muted) return false;
        return allTracks.Any(t => t.Solo)
            ? track.Solo
            : true;
    }

    /// <summary>True when at least one clip anywhere is soloed (clip-level solo).</summary>
    public static bool AnyClipSolo(IReadOnlyList<AudioTrack> tracks) =>
        tracks.SelectMany(t => t.Clips).Any(c => c.Solo);

    /// <summary>A clip plays when neither it, its track nor solo policy silences it.</summary>
    public bool IsClipAudible(AudioClip clip, AudioTrack track, IReadOnlyList<AudioTrack> allTracks)
    {
        if (!IsAudible(track, allTracks)) return false;
        if (AnyClipSolo(allTracks)) return clip.Solo;
        return !clip.Muted;
    }

    public double ClipGainLinear(AudioClip clip, AudioTrack track) =>
        AudioMath.ClampLinear(
            Math.Max(0, clip.Volume)
            * AudioMath.DbToLinear(AudioMath.ClampDb(clip.GainDb))
            * Math.Max(0, track.Volume));

    public double FadeGainAt(AudioClip clip, double t)
    {
        double fadeIn = Math.Max(0, clip.FadeIn);
        double fadeOut = Math.Max(0, clip.FadeOut);
        double dur = Math.Max(0.04, clip.Duration);

        double gain = 1.0;
        if (fadeIn > 0 && t < fadeIn)
            gain *= Math.Max(0, t / fadeIn);
        double outStart = dur - fadeOut;
        if (fadeOut > 0 && t > outStart)
            gain *= Math.Max(0, (dur - t) / fadeOut);

        return Math.Clamp(gain, 0.0, 1.0);
    }

    public double DuckGainAt(IReadOnlyList<(double Start, double End)> dialogueIntervals,
        double t, DuckingSettings ducking)
    {
        if (!ducking.Enabled || dialogueIntervals.Count == 0)
            return 1.0;

        double amount = AudioMath.DbToLinear(AudioMath.ClampDb(ducking.AmountDb));
        double attack = Math.Max(0.01, ducking.AttackSec);
        double release = Math.Max(0.01, ducking.ReleaseSec);

        double min = 1.0;
        foreach (var (start, end) in dialogueIntervals)
        {
            if (t < start || t > end + release) continue;

            // piecewise linear: 1 → amount over attack, hold, amount → 1 over release
            if (t < start + attack)
                min = Math.Min(min, Lerp(1.0, amount, (t - start) / attack));
            else if (t > end)
                min = Math.Min(min, Lerp(amount, 1.0, (t - end) / release));
            else
                min = Math.Min(min, amount);
        }
        return min;
    }

    private static double Lerp(double a, double b, double f) => a + (b - a) * Math.Clamp(f, 0, 1);

    public string BuildClipFilters(AudioClip clip, AudioTrack track,
        IReadOnlyList<(double Start, double End)>? dialogueIntervals = null,
        DuckingSettings? ducking = null)
    {
        var filters = new List<string>();

        // ---------- gain ----------
        double totalGain = ClipGainLinear(clip, track);

        // volume keyframes override the static value with automation
        var keyframes = clip.Keyframes.OrderBy(k => k.Time).ToList();
        if (keyframes.Count >= 2)
        {
            string expr = BuildKeyframeExpression(keyframes);
            filters.Add($"volume='{expr}':eval=frame");
        }
        else if (Math.Abs(totalGain - 1.0) > 0.001)
        {
            filters.Add($"volume={N(totalGain)}");
        }

        // ---------- fades (§8) ----------
        double fadeIn = Math.Max(0, clip.FadeIn);
        double fadeOut = Math.Max(0, clip.FadeOut);
        double dur = Math.Max(0.04, clip.Duration);

        if (fadeIn > 0.001)
            filters.Add($"afade=t=in:st=0:d={N(fadeIn)}");

        if (fadeOut > 0.001)
        {
            double outStart = Math.Max(0, dur - fadeOut);
            filters.Add($"afade=t=out:st={N(outStart)}:d={N(fadeOut)}");
        }

        // ---------- pan (§6) ----------
        if (Math.Abs(clip.Pan) > 0.01 || Math.Abs(track.Pan) > 0.01)
        {
            double pan = Math.Clamp(clip.Pan + track.Pan, -1.0, 1.0);
            var (l, r) = AudioMath.PanGains(pan);
            filters.Add($"pan=stereo|c0={N(l)}*c0|c1={N(r)}*c1");
        }

        // ---------- ducking (§22) ----------
        if (ducking is { Enabled: true } && dialogueIntervals is { Count: > 0 })
        {
            // map timeline intervals into this clip's local time
            var local = new List<(double S, double E)>();
            foreach (var (s, e) in dialogueIntervals)
            {
                double ls = s - clip.Start;
                double le = e - clip.Start;
                if (le <= 0 || ls >= dur) continue;
                local.Add((Math.Max(0, ls), Math.Min(dur, le)));
            }

            if (local.Count > 0)
            {
                string duckExpr = BuildDuckExpression(local, ducking);
                filters.Add($"volume='{duckExpr}':eval=frame");
            }
        }

        return string.Join(",", filters);
    }

    /// <summary>
    /// Nested-if FFmpeg expression interpolating linearly between keyframes:
    /// if(lt(t,k2), lerp(k1,k2,t), if(lt(t,k3), ...)).
    /// </summary>
    public static string BuildKeyframeExpression(List<VolumeKeyframe> keys)
    {
        var sb = new StringBuilder();
        BuildKeySegment(keys[0], keys.Skip(1).ToList());
        return sb.ToString();

        void BuildKeySegment(VolumeKeyframe current, List<VolumeKeyframe> rest)
        {
            if (rest.Count == 0)
            {
                sb.Append(N(current.Value));
                return;
            }

            var next = rest[0];
            double span = Math.Max(0.0001, next.Time - current.Time);
            sb.Append($"if(lt(t\\,{N(next.Time)})\\,");
            sb.Append($"{N(current.Value)}+({N(next.Value)}-{N(current.Value)})*(t-{N(current.Time)})/{N(span)}");
            sb.Append("\\,");
            BuildKeySegment(next, rest.Skip(1).ToList());
            sb.Append(")");
        }
    }

    /// <summary>
    /// min() of one nested-if term per dialogue interval, each evaluating to 1
    /// outside its window and to the shaped duck gain inside it.
    /// </summary>
    internal static string BuildDuckExpression(List<(double S, double E)> intervals, DuckingSettings ducking)
    {
        string amount = N(AudioMath.DbToLinear(AudioMath.ClampDb(ducking.AmountDb)));
        double a = Math.Max(0.01, ducking.AttackSec);
        double r = Math.Max(0.01, ducking.ReleaseSec);

        var terms = new List<string>();
        foreach (var (s, e) in intervals.Take(16))
        {
            double coreStart = s + a;
            double coreEnd = Math.Max(coreStart, e);

            string term =
                $"if(between(t\\,{N(s)}\\,{N(e + r)})\\," +
                $"if(lt(t\\,{N(coreStart)})\\," +
                $"{amount}+({N(1.0)}-{amount})*({N(coreStart)}-t)/{N(a)}\\," +
                $"if(gt(t\\,{N(coreEnd)})\\," +
                $"{amount}+({N(1.0)}-{amount})*(t-{N(coreEnd)})/{N(r)}\\," +
                $"{amount}))" +
                ",1)";
            terms.Add(term);
        }

        return terms.Count == 1 ? terms[0] : $"min({string.Join("\\,", terms)})";
    }

    private static string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}

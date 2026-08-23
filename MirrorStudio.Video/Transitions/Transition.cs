using System.Globalization;

namespace MirrorStudio.Video.Transitions;

internal static class TN
{
    public static string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}

/// <summary>
/// Base for A/B transitions (spec M10 §19). Most map to ffmpeg's xfade filter;
/// fade-through-color maps to chained fades instead.
/// </summary>
public abstract class Transition
{
    public string Name { get; set; } = "";

    /// <summary>Overlap duration in seconds.</summary>
    public double Duration { get; set; } = 1;

    /// <summary>
    /// The xfade parameters for this transition, e.g. "transition=fade:duration=0.5:offset=4.5",
    /// or null when the transition isn't an xfade variant (see FadeThroughColorTransition).
    /// </summary>
    public abstract string? BuildXfadeArgs();

    public string BuildFfmpegFilter(string labelA, string labelB, double offset, string outputLabel) =>
        $"[{labelA}][{labelB}]xfade={BuildXfadeArgs()}:offset={TN.N(Math.Max(0, offset))}[{outputLabel}]";
}

/// <summary>The workhorse: opacity cross-dissolve (spec M10 §20).</summary>
public class CrossDissolveTransition : Transition
{
    public CrossDissolveTransition(double duration = 1) { Name = "Cross Dissolve"; Duration = duration; }

    public override string BuildXfadeArgs() => $"transition=fade:duration={TN.N(Duration)}";
}

/// <summary>Directional wipe (spec M10 §22): Left/Right/Top/Bottom/Diagonal.</summary>
public class WipeTransition : Transition
{
    private readonly string _variant;

    public WipeTransition(string direction = "left", double duration = 1)
    {
        Name = "Wipe";
        Duration = duration;
        _variant = direction.ToLowerInvariant() switch
        {
            "right" => "wiperight",
            "up" => "wipeup",
            "down" => "wipedown",
            "diagonal" => "diagtl",
            _ => "wipeleft",
        };
    }

    public override string BuildXfadeArgs() => $"transition={_variant}:duration={TN.N(Duration)}";
}

/// <summary>Push/slide (spec M10 §23).</summary>
public class SlideTransition : Transition
{
    private readonly string _variant;

    public SlideTransition(string direction = "left", double duration = 1)
    {
        Name = "Slide";
        Duration = duration;
        _variant = direction.ToLowerInvariant() switch
        {
            "right" => "slideright",
            "up" => "slideup",
            "down" => "slidedown",
            _ => "slideleft",
        };
    }

    public override string BuildXfadeArgs() => $"transition={_variant}:duration={TN.N(Duration)}";
}

/// <summary>Zoom-out-then-in punch (spec M10 §24) — great for social edits.</summary>
public class ZoomTransition : Transition
{
    public ZoomTransition(double duration = 1) { Name = "Zoom"; Duration = duration; }

    public override string BuildXfadeArgs() => $"transition=zoomin:duration={TN.N(Duration)}";
}

/// <summary>Fade through black/white/color (spec M10 §21): A fades out, B fades in.</summary>
public class FadeThroughColorTransition : Transition
{
    /// <summary>Dip color as hex ("#000000", "#FFFFFF", …).</summary>
    public string Color { get; set; } = "#000000";

    public FadeThroughColorTransition(string color = "#000000", double duration = 1)
    {
        Name = "Fade";
        Color = color;
        Duration = duration;
    }

    /// <summary>Not expressible as a single xfade — the builder emits two fades.</summary>
    public override string? BuildXfadeArgs() => null;

    /// <summary>Half-duration fade out on A, half-duration fade in on B (sequential, no overlap).</summary>
    public (string FadeOutFilter, string FadeInFilter) BuildFadeFilters(double totalDurationOfA)
    {
        string rgb = Color.StartsWith('#') ? "0x" + Color[1..] : Color;
        string half = TN.N(Duration / 2.0);
        return (
            $"fade=t=out:st={TN.N(Math.Max(0, totalDurationOfA - Duration / 2.0))}:d={half}:c={rgb}",
            $"fade=t=in:st=0:d={half}:c={rgb}");
    }
}

/// <summary>Maps the timeline's transition kind strings to concrete transitions.</summary>
public static class TransitionFactory
{
    public static readonly string[] KnownKinds =
    [
        "Cut", "CrossDissolve", "Fade", "DipToBlack", "DipToWhite", "Wipe", "Slide", "Zoom",
    ];

    /// <summary>Returns null for Cut/unknown → plain concat.</summary>
    public static Transition? Create(string kind, double durationSec, string color = "#000000") =>
        kind switch
        {
            "CrossDissolve" => new CrossDissolveTransition(durationSec),
            "Wipe" => new WipeTransition("left", durationSec),
            "Slide" => new SlideTransition("left", durationSec),
            "Zoom" => new ZoomTransition(durationSec),
            "DipToWhite" => new FadeThroughColorTransition("#FFFFFF", durationSec),
            "DipToBlack" => new FadeThroughColorTransition(string.IsNullOrWhiteSpace(color) || color == "#000000" ? "#000000" : color, durationSec),
            "Fade" => new FadeThroughColorTransition("#000000", durationSec),
            _ => null,
        };
}

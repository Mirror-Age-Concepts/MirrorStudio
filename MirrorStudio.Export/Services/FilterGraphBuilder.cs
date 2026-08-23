using MirrorStudio.Audio.Models;
using MirrorStudio.Audio.Services;
using MirrorStudio.Core.Projects;
using MirrorStudio.Export.Models;
using MirrorStudio.Video.Effects;

using System.Globalization;

namespace MirrorStudio.Export.Services;

/// <summary>
/// One decoded input for the ffmpeg command line.
/// <see cref="PreInputArgs"/> carries options that must appear before -i
/// (e.g. "-loop 1 -t 3" for stills or "-f lavfi -t 3" for generated fallbacks).
/// </summary>
public class PlanInput
{
    public string Path { get; set; } = "";
    public string PreInputArgs { get; set; } = "";
}

/// <summary>The complete render plan produced by <see cref="FilterGraphBuilder"/>.</summary>
public class RenderPlan
{
    public List<PlanInput> Inputs { get; } = [];

    /// <summary>Full filter_complex string ([outv]/[outa] labels included).</summary>
    public string FilterGraph { get; set; } = "";

    public bool HasVideo { get; set; }

    public bool HasAudio { get; set; }

    /// <summary>Total timeline duration of the export in seconds.</summary>
    public double TotalDuration { get; set; }

    /// <summary>Media files that could not be found and were replaced by placeholders.</summary>
    public List<string> OfflineMedia { get; } = [];

    public int VideoClipCount { get; set; }

    public int AudioClipCount { get; set; }
}

/// <summary>
/// Builds the FFmpeg filter_complex for a timeline (spec M8 §14–§17):
///
///   [i:v]trim=start=A:end=B,setpts=PTS-STARTPTS,scale,pad,fps[v0]; ...
///   [i:a]atrim=start=A:end=B,asetpts=PTS-STARTPTS[a0]; ...
///   [v0][v1][v2]concat=n=3:v=1:a=0[outv]
///   [a0][a1][a2]concat=n=3:v=0:a=1[outa]
///
/// Timeline position vs source position stay separate: SourceStart/SourceEnd
/// are source seconds, Start/Duration are timeline seconds.
/// </summary>
public class FilterGraphBuilder
{
    private readonly Func<string, bool>? _fileExists;

    private static readonly string[] ImageExtensions =
        [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".tif", ".tiff"];

    public FilterGraphBuilder(Func<string, bool>? fileExists = null)
    {
        _fileExists = fileExists;
    }

    public RenderPlan Build(Project project, ExportSettings settings)
    {
        var plan = new RenderPlan();
        var w = Math.Max(16, settings.Width);
        var h = Math.Max(16, settings.Height);
        var fps = N(Math.Max(1, settings.FrameRate));

        var mediaById = project.Media.ToDictionary(m => m.Id);

        var videoTracks = project.Tracks.Where(t => t.Type == "video").OrderBy(t => t.Index).ToList();
        var audioTracks = project.Tracks.Where(t => t.Type == "audio").OrderBy(t => t.Index).ToList();

        var clips = project.Tracks.SelectMany(t => t.Clips).ToList();
        plan.TotalDuration = clips.Count == 0 ? 0 : clips.Max(c => c.Start + c.Duration);

        bool audioOnly = settings.ExportAudioOnly;
        var videoChains = new List<string>();
        var audioChains = new List<string>();

        // ---------- video segments ----------
        if (!audioOnly)
        {
            var segments = videoTracks.SelectMany(t => t.Clips).OrderBy(c => c.Start).ToList();
            plan.VideoClipCount = segments.Count;
            plan.HasVideo = segments.Count > 0;

            // Accumulated output stream label + its effective length. Transitions
            // are length-preserving: xfades hold A's last frame via tpad while B
            // dissolves in, dips use half-fades on both sides (spec M10 §19–§24).
            string? current = null;
            double currentLen = 0;

            for (int i = 0; i < segments.Count; i++)
            {
                var clip = segments[i];
                mediaById.TryGetValue(clip.MediaAssetId, out var media);
                var srcPath = media?.FilePath ?? "";
                var exists = !string.IsNullOrEmpty(srcPath) && (_fileExists?.Invoke(srcPath) ?? File.Exists(srcPath));
                bool isImage = exists && IsImage(srcPath);

                int inputIndex = plan.Inputs.Count;
                string chainHead;

                if (!exists)
                {
                    // offline media → black placeholder so the render still completes
                    if (!string.IsNullOrEmpty(srcPath)) plan.OfflineMedia.Add(srcPath);
                    plan.Inputs.Add(new PlanInput
                    {
                        Path = $"color=c=black:s={w}x{h}:r={fps}",
                        PreInputArgs = $"-f lavfi -t {N(Math.Max(0.04, clip.Duration))}",
                    });
                    chainHead = null;   // nothing to trim from a generated source
                }
                else if (isImage)
                {
                    plan.Inputs.Add(new PlanInput
                    {
                        Path = Quote(srcPath),
                        PreInputArgs = $"-loop 1 -t {N(Math.Max(0.04, clip.Duration))}",
                    });
                    chainHead = null;
                }
                else
                {
                    plan.Inputs.Add(new PlanInput { Path = Quote(srcPath) });
                    chainHead = $"trim=start={N(SourceIn(clip))}:end={N(SourceOut(clip))},setpts=PTS-STARTPTS";
                }

                var filters = new List<string>();
                if (chainHead != null) filters.Add(chainHead);

                // ordered effect stack on source-native frames (spec M10 §28/§29);
                // multi-stage effects carry ';' separators and become their own
                // filter_complex statements feeding the continuing chain
                foreach (var fx in clip.Effects.Where(e => e.Enabled))
                {
                    var built = VideoEffectFactory.Create(fx.Type, fx.Amount, fx.Settings)?.BuildFfmpegFilter();
                    if (string.IsNullOrEmpty(built)) continue;
                    filters.AddRange(built.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
                }

                if (clip.Opacity < 0.999)
                    filters.Add($"format=rgba,colorchannelmixer=aa={N(Math.Clamp(clip.Opacity, 0, 1))}");

                filters.Add($"scale={w}:{h}:force_original_aspect_ratio=decrease");
                filters.Add($"pad={w}:{h}:(ow-iw)/2:(oh-ih)/2");
                filters.Add("setsar=1");
                filters.Add($"fps={fps}");

                string segLabel = $"[v{i}]";
                videoChains.Add($"[{inputIndex}:v]{string.Join(",", filters)}{segLabel}");
                double segLen = Math.Max(0.04, clip.Duration);

                if (current == null)
                {
                    current = segLabel;
                    currentLen = segLen;
                    continue;
                }

                double dur = Math.Clamp(clip.TransitionDurationSec, 0.0, Math.Min(currentLen, segLen));
                var transition = dur > 0.05
                    ? MirrorStudio.Video.Transitions.TransitionFactory.Create(clip.TransitionKind, dur, clip.TransitionColor)
                    : null;
                string merged = $"[vx{i}]";

                if (transition == null)
                {
                    // hard cut
                    videoChains.Add($"{current}{segLabel}concat=n=2:v=1:a=0{merged}");
                }
                else if (transition is MirrorStudio.Video.Transitions.FadeThroughColorTransition dip)
                {
                    string color = dip.Color.StartsWith('#') ? "0x" + dip.Color[1..] : dip.Color;
                    string half = N(dur / 2.0);
                    string outA = $"[vd{i}]";
                    string inB = $"[ve{i}]";
                    videoChains.Add($"{current}fade=t=out:st={N(Math.Max(0, currentLen - dur / 2.0))}:d={half}:c={color}{outA}");
                    videoChains.Add($"{segLabel}fade=t=in:st=0:d={half}:c={color}{inB}");
                    videoChains.Add($"{outA}{inB}concat=n=2:v=1:a=0{merged}");
                }
                else
                {
                    string held = $"[vh{i}]";
                    videoChains.Add($"{current}tpad=stop_mode=clone:stop_duration={N(dur)}{held}");
                    videoChains.Add($"{held}{segLabel}xfade={transition!.BuildXfadeArgs()}:offset={N(currentLen)}{merged}");
                }

                current = merged;
                currentLen += segLen;
            }

            if (current != null)
                videoChains.Add($"{current}null[outv]");
        }

        // ---------- audio segments ----------
        {
            var mixer = new AudioMixer();
            bool anySoloTrack = audioTracks.Any(t => t.Solo);
            bool anySoloClip = audioTracks.SelectMany(t => t.Clips).Any(c => c.Solo);

            // audible clips only, in timeline order
            var segments = audioTracks
                .Where(t => !(anySoloTrack ? !t.Solo : t.Muted))
                .SelectMany(t => t.Clips.Select(c => (Clip: c, Track: t)))
                .Where(x => !(x.Clip.Muted || (anySoloClip && !x.Clip.Solo && !x.Track.Solo)))
                .OrderBy(x => x.Clip.Start)
                .ToList();

            plan.AudioClipCount = segments.Count;
            plan.HasAudio = segments.Count > 0;

            // dialogue intervals for ducking: audible clips on key tracks
            List<(double Start, double End)>? duckIntervals = null;
            DuckingSettings? ducking = null;
            if (settings.Ducking is { Enabled: true } d && d.KeyTrackIndexes.Count > 0)
            {
                ducking = d;
                duckIntervals = audioTracks
                    .Where(t => d.KeyTrackIndexes.Contains(t.Index))
                    .SelectMany(t => t.Clips)
                    .OrderBy(c => c.Start)
                    .Select(c => (c.Start, Math.Min(plan.TotalDuration, c.Start + c.Duration)))
                    .ToList();
            }

            int i = 0;
            foreach (var (clip, track) in segments)
            {
                mediaById.TryGetValue(clip.MediaAssetId, out var media);
                var srcPath = media?.FilePath ?? "";
                var exists = !string.IsNullOrEmpty(srcPath) && (_fileExists?.Invoke(srcPath) ?? File.Exists(srcPath));

                int inputIndex = plan.Inputs.Count;
                string streamType = ":a";
                List<string> filters;

                if (!exists)
                {
                    if (!string.IsNullOrEmpty(srcPath)) plan.OfflineMedia.Add(srcPath);
                    plan.Inputs.Add(new PlanInput
                    {
                        Path = "anullsrc=r=48000:cl=stereo",
                        PreInputArgs = $"-f lavfi -t {N(Math.Max(0.04, clip.Duration))}",
                    });
                    filters =
                    [
                        "asetpts=PTS-STARTPTS",
                        mixer.BuildClipFilters(ToAudioClip(clip), ToAudioTrack(track), duckIntervals, ducking),
                    ];
                }
                else
                {
                    plan.Inputs.Add(new PlanInput { Path = Quote(srcPath) });
                    var clipFilters = mixer.BuildClipFilters(ToAudioClip(clip), ToAudioTrack(track), duckIntervals, ducking);

                    filters =
                    [
                        $"atrim=start={N(SourceIn(clip))}:end={N(SourceOut(clip))}",
                        "asetpts=PTS-STARTPTS",
                        clipFilters,
                    ];
                }

                filters.RemoveAll(string.IsNullOrEmpty);
                audioChains.Add($"[{inputIndex}{streamType}]{string.Join(",", filters)}[a{i}]");
                i++;
            }

            if (audioChains.Count > 0)
            {
                var labels = string.Join("", Enumerable.Range(0, audioChains.Count).Select(j => $"[a{j}]"));
                audioChains.Add($"{labels}concat=n={audioChains.Count}:v=0:a=1[outa]");
            }
        }

        var all = new List<string>();
        all.AddRange(videoChains);
        all.AddRange(audioChains);
        plan.FilterGraph = string.Join(";", all);

        return plan;
    }

    /// <summary>Source in-point (spec M8 §16: trim=start=10:end=45 for a 10→45 trim).</summary>
    internal static double SourceIn(TimelineClipData clip) => Math.Max(0, clip.SourceStart);

    /// <summary>Source out-point.</summary>
    internal static double SourceOut(TimelineClipData clip) =>
        Math.Max(SourceIn(clip) + 0.04, clip.SourceEnd);

    private static AudioClip ToAudioClip(TimelineClipData c) => new()
    {
        Name = c.Name,
        Start = c.Start,
        Duration = c.Duration,
        SourceStart = SourceIn(c),
        Volume = c.Volume,
        GainDb = c.GainDb,
        Pan = c.Pan,
        FadeIn = c.FadeIn,
        FadeOut = c.FadeOut,
        Muted = c.Muted,
        Solo = c.Solo,
    };

    private static AudioTrack ToAudioTrack(TimelineTrackData t) => new()
    {
        Name = t.Name,
        Index = t.Index,
        Volume = 1.0,
        Pan = 0,
        Muted = t.Muted,
        Solo = t.Solo,
    };

    private static bool IsImage(string path) =>
        ImageExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    private static string Quote(string p) => $"\"{p}\"";

    private static string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}

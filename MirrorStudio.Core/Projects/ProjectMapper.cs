using System.Text.Json;
using MirrorStudio.Core.Models;

namespace MirrorStudio.Core.Projects;

/// <summary>
/// Adapts the live editor session model (<see cref="ProjectModel"/>) to the
/// serialized project model (<see cref="Project"/>) consumed by the render
/// engine and the .msp package service.
/// </summary>
public static class ProjectMapper
{
    /// <param name="resolvePath">
    /// Resolves a clip/media relative path to an absolute path on disk.
    /// </param>
    public static Project ToProject(
        this ProjectModel source,
        Func<string, string?>? resolvePath = null)
    {
        var project = new Project
        {
            Name = source.Name,
            Settings =
            {
                Width = source.Width,
                Height = source.Height,
                FrameRate = source.FrameRate <= 0 ? 25 : source.FrameRate,
                AudioSampleRate = source.AudioSampleRateHz.ToString(),
            },
            CreatedAt = source.CreatedAt,
            ModifiedAt = source.UpdatedAt,
        };

        // ---------- graphics + subtitles (Milestone 11) ----------
        project.GraphicsJson = JsonSerializer.Serialize(source.Graphics);
        project.SubtitlesJson = JsonSerializer.Serialize(source.SubtitleTracks);

        // ---------- media references ----------
        var mediaIdByPath = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in source.Media)
        {
            var absolute = resolvePath?.Invoke(entry.RelativePath)
                ?? Path2(entry.RelativePath);
            var media = new ProjectMedia
            {
                FileName = entry.FileName,
                FilePath = absolute,
                Duration = entry.DurationSec,
                Width = entry.Width,
                Height = entry.Height,
                FrameRate = entry.FrameRate,
            };
            project.Media.Add(media);
            if (!string.IsNullOrEmpty(entry.RelativePath))
                mediaIdByPath[entry.RelativePath] = media.Id;
            if (!string.IsNullOrEmpty(absolute))
                mediaIdByPath[absolute] = media.Id;
        }

        // ---------- tracks ----------
        var videoTrackIds = new Dictionary<int, TimelineTrackData>();
        var audioTrackIds = new Dictionary<int, TimelineTrackData>();

        foreach (var clip in source.Timeline.Clips.OrderBy(c => c.StartTime))
        {
            bool isAudio = clip.TrackKind == TrackKind.Audio || clip.MediaType == "Audio";
            var table = isAudio ? audioTrackIds : videoTrackIds;
            if (!table.TryGetValue(clip.TrackIndex, out var track))
            {
                track = new TimelineTrackData
                {
                    Name = (isAudio ? "A" : "V") + (clip.TrackIndex + 1),
                    Type = isAudio ? "audio" : "video",
                    Index = clip.TrackIndex,
                };
                table[clip.TrackIndex] = track;
                project.Tracks.Add(track);
            }

            Guid mediaId = Guid.Empty;
            string key = !string.IsNullOrEmpty(clip.SourcePath) ? clip.SourcePath : clip.Name;
            if (!mediaIdByPath.TryGetValue(key, out mediaId))
                mediaId = mediaIdByPath.TryGetValue(clip.SourcePath, out var byName) ? byName : Guid.Empty;

            double srcStart = Math.Max(0, clip.InPoint);
            double srcEnd = Math.Max(srcStart + 0.04, clip.OutPoint > clip.InPoint ? clip.OutPoint : clip.InPoint + clip.Duration);

            track.Clips.Add(new TimelineClipData
            {
                MediaAssetId = mediaId,
                Name = clip.DisplayName,
                Start = clip.StartTime,
                Duration = Math.Max(0.04, clip.Duration),
                SourceStart = srcStart,
                SourceEnd = srcEnd,
                Speed = 1.0,
                Volume = Math.Clamp(clip.Volume / 100.0, 0.0, 4.0),
                GainDb = clip.GainDb,
                Pan = Math.Clamp(clip.Pan / 100.0, -1.0, 1.0),
                FadeIn = clip.FadeIn,
                FadeOut = clip.FadeOut,
                Muted = clip.IsMuted,
                Solo = clip.IsSolo,
                Opacity = Math.Clamp(clip.Opacity / 100.0, 0.0, 1.0),
                Effects = clip.Effects.Select(ToCanonicalEffect).ToList(),
                TransitionKind = ToTransitionName(clip.TransitionIn),
                TransitionDurationSec = clip.TransitionIn?.DurationSeconds ?? 0.5,
                TransitionColor = "#000000",
            });
        }

        return project;
    }

    private static string Path2(string rel) =>
        System.IO.Path.IsPathRooted(rel) ? rel : System.IO.Path.GetFullPath(rel);

    // ---------- effects & transitions (spec M10 §28/§29) ----------

    /// <summary>
    /// Legacy 0..100 effect sliders → canonical effect units (blur sigma,
    /// unsharp multiplier, ffmpeg brightness range …).
    /// </summary>
    private static ClipEffectData ToCanonicalEffect(EffectModel m)
    {
        var data = new ClipEffectData { Type = m.Type, Amount = m.Amount };
        foreach (var kv in m.Settings)
            data.Settings[kv.Key] = kv.Value;

        switch (m.Type)
        {
            case "Blur":
                data.Amount = m.Amount / 100.0 * 15.0;
                break;
            case "Sharpen":
                data.Amount = m.Amount / 100.0 * 1.5;
                break;
            case "Brightness":
                data.Type = "ColorCorrection";
                data.Amount = m.Amount / 100.0 * 0.3;
                break;
            case "Contrast":
                data.Type = "ColorCorrection";
                data.Settings["contrast"] = (1 + m.Amount / 100.0).ToString(System.Globalization.CultureInfo.InvariantCulture);
                break;
            case "Saturation":
                data.Type = "ColorCorrection";
                data.Settings["saturation"] = (1 + m.Amount / 100.0).ToString(System.Globalization.CultureInfo.InvariantCulture);
                break;
            case "Grayscale":
                data.Type = "ColorCorrection";
                data.Settings["saturation"] = "0";
                break;
            case "Sepia":
                data.Type = "ColorCorrection";
                data.Settings["temperature"] = "45";
                data.Settings["saturation"] = "0.85";
                break;
            case "Hue":
                data.Type = "ColorCorrection";
                data.Settings["tint"] = (m.Amount - 50).ToString(System.Globalization.CultureInfo.InvariantCulture);
                break;
            case "Vignette":
                data.Type = "Vignette";
                data.Amount = m.Amount / 100.0;
                break;
            case "Lut":
                data.Settings["intensity"] = Math.Clamp(m.Amount / 100.0, 0, 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                break;
            case "ChromaKey":
                data.Amount = m.Amount / 100.0;
                break;
            case "Glow":
            case "Noise":
                data.Amount = m.Amount / 100.0;
                break;
            case "Noise Reduction":
                data.Type = "NoiseReduction";
                data.Amount = m.Amount / 100.0 * 4.0;
                break;
            case "Invert":
                data.Amount = 0;
                break;
        }

        return data;
    }

    /// <summary>Canonical effects back to legacy sliders; canonical-only families are dropped.</summary>
    private static IEnumerable<EffectModel> FromCanonicalEffects(IEnumerable<ClipEffectData> effects)
    {
        foreach (var e in effects)
        {
            switch (e.Type)
            {
                case "Blur":
                    yield return new EffectModel { Type = "Blur", Amount = Math.Clamp(e.Amount / 15.0 * 100, 0, 100) };
                    break;
                case "Sharpen":
                    yield return new EffectModel { Type = "Sharpen", Amount = Math.Clamp(e.Amount / 1.5 * 100, 0, 100) };
                    break;
                case "ColorCorrection":
                    double brightness = e.Settings.TryGetValue("brightness", out var bs) && double.TryParse(bs, System.Globalization.CultureInfo.InvariantCulture, out var b) ? b : e.Amount;
                    double contrast = ParseSetting(e, "contrast", 1);
                    double saturation = ParseSetting(e, "saturation", 1);
                    double tint = ParseSetting(e, "tint", 0);
                    if (Math.Abs(brightness) > 0.001)
                        yield return new EffectModel { Type = "Brightness", Amount = Math.Clamp(brightness / 0.3 * 100, -100, 100) };
                    else if (Math.Abs(contrast - 1) > 0.001)
                        yield return new EffectModel { Type = "Contrast", Amount = Math.Clamp((contrast - 1) * 100, 0, 100) };
                    else if (Math.Abs(saturation - 1) > 0.001)
                        yield return new EffectModel { Type = "Saturation", Amount = Math.Clamp((saturation - 1) * 100, 0, 100) };
                    else if (Math.Abs(tint) > 0.001)
                        yield return new EffectModel { Type = "Hue", Amount = Math.Clamp(tint + 50, 0, 100) };
                    break;
                case "Vignette":
                    yield return new EffectModel { Type = "Vignette", Amount = Math.Clamp(e.Amount * 100, 0, 100) };
                    break;
                case "NoiseReduction":
                    yield return new EffectModel { Type = "Noise Reduction", Amount = Math.Clamp(e.Amount / 4.0 * 100, 0, 100) };
                    break;
                case "Invert":
                    yield return new EffectModel { Type = "Invert", Amount = 0 };
                    break;
            }
        }
    }

    private static double ParseSetting(ClipEffectData e, string key, double fallback) =>
        e.Settings.TryGetValue(key, out var raw) && double.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, out var v)
            ? v
            : fallback;

    private static string ToTransitionName(ClipTransition? t)
    {
        if (t is null) return "Cut";
        return t.Kind switch
        {
            TransitionKind.Fade => "Fade",
            TransitionKind.CrossDissolve => "CrossDissolve",
            TransitionKind.DipToBlack => "DipToBlack",
            TransitionKind.DipToWhite => "DipToWhite",
            TransitionKind.Wipe => "Wipe",
            TransitionKind.Slide => "Slide",
            TransitionKind.Zoom => "Zoom",
            _ => "Cut",
        };
    }

    private static ClipTransition? FromTransitionName(string kind, double durationSec)
    {
        var k = kind switch
        {
            "Fade" => TransitionKind.Fade,
            "CrossDissolve" => TransitionKind.CrossDissolve,
            "DipToBlack" => TransitionKind.DipToBlack,
            "DipToWhite" => TransitionKind.DipToWhite,
            "Wipe" => TransitionKind.Wipe,
            "Slide" => TransitionKind.Slide,
            "Zoom" => TransitionKind.Zoom,
            _ => TransitionKind.Cut,
        };

        // normalize to a Cut instance rather than null so downstream
        // consumers can rely on a non-null transition (see RenderPreviewFrame)
        return new ClipTransition { Kind = k, DurationSeconds = Math.Max(0.05, durationSec) };
    }

    public static TimeSpan TotalDuration(Project project) =>
        TimeSpan.FromSeconds(project.Tracks
            .SelectMany(t => t.Clips)
            .Select(c => c.Start + c.Duration)
            .DefaultIfEmpty(0)
            .Max());

    /// <summary>Reverse mapping: canonical project back to the live session model.</summary>
    public static ProjectModel ToProjectModel(Project source)
    {
        var model = new ProjectModel
        {
            Name = source.Name,
            Width = source.Settings.Width,
            Height = source.Settings.Height,
            FrameRate = source.Settings.FrameRate,
            AudioSampleRateHz = int.TryParse(source.Settings.AudioSampleRate, out var sr) ? sr : 48000,
            CreatedAt = source.CreatedAt,
            UpdatedAt = source.ModifiedAt,
        };

        var mediaById = source.Media.ToDictionary(m => m.Id);
        var usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var track in source.Tracks.OrderBy(t => t.Type).ThenBy(t => t.Index))
        {
            bool isAudio = track.Type == "audio";
            foreach (var clip in track.Clips)
            {
                mediaById.TryGetValue(clip.MediaAssetId, out var media);
                var relative = media?.FilePath ?? "";
                usedPaths.Add(relative);

                model.Timeline.Clips.Add(new ClipModel
                {
                    Name = clip.Name,
                    SourcePath = relative,
                    MediaType = isAudio ? "Audio" : "Video",
                    TrackKind = isAudio ? TrackKind.Audio : TrackKind.Video,
                    TrackIndex = track.Index,
                    StartTime = clip.Start,
                    Duration = clip.Duration,
                    InPoint = clip.SourceStart,
                    OutPoint = clip.SourceEnd,
                    Volume = clip.Volume * 100.0,
                    GainDb = clip.GainDb,
                    Pan = clip.Pan * 100.0,
                    FadeIn = clip.FadeIn,
                    FadeOut = clip.FadeOut,
                    IsMuted = clip.Muted,
                    IsSolo = clip.Solo,
                    Opacity = clip.Opacity * 100.0,
                    TransitionIn = FromTransitionName(clip.TransitionKind, clip.TransitionDurationSec),
                    Effects = FromCanonicalEffects(clip.Effects).ToList(),
                });
            }
        }

        foreach (var media in source.Media)
        {
            if (!usedPaths.Contains(media.FilePath)) continue;
            model.Media.Add(new MediaEntry
            {
                FileName = media.FileName,
                RelativePath = media.FilePath,
                MediaType = "video",
                DurationSec = media.Duration,
                Width = media.Width,
                Height = media.Height,
                FrameRate = media.FrameRate,
                Probed = media.Duration > 0,
            });
        }

        // ---------- graphics + subtitles (Milestone 11) ----------
        try
        {
            model.Graphics.AddRange(
                JsonSerializer.Deserialize<List<MirrorStudio.Graphics.Models.GraphicLayer>>(source.GraphicsJson)
                    ?? []);
            model.SubtitleTracks.AddRange(
                JsonSerializer.Deserialize<List<MirrorStudio.Graphics.Subtitles.SubtitleTrack>>(source.SubtitlesJson)
                    ?? []);
        }
        catch (JsonException)
        {
            // corrupt graphics payload must never block project load
        }

        return model;
    }
}

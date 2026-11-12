using MirrorStudio.Core.Models;
using MirrorStudio.Graphics.Models;
using MirrorStudio.Graphics.Subtitles;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace MirrorStudio.Video
{
    public static class SequenceExporter
    {
        private static string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        public static bool TryBuild(
            ProjectModel project,
            TimelineData timeline,
            Func<string, string?> resolve,
            string tag,
            int crf,
            bool includeAudio,
            string output,
            out string args,
            out string error)
        {
            args = string.Empty;
            error = string.Empty;

            var audioOnly = tag == "wav" || tag == "mp3";
            var videoClips = timeline.Clips
                .Where(c => c.TrackKind == TrackKind.Video && c.MediaType != "Audio")
                .OrderBy(c => c.StartTime)
                .ToList();
            var audioClips = timeline.Clips.Where(c => c.TrackKind == TrackKind.Audio).OrderBy(c => c.StartTime).ToList();

            if (!audioOnly && videoClips.Count == 0)
            {
                error = "Timeline has no video clips.";
                return false;
            }
            if (audioOnly && audioClips.Count == 0)
            {
                error = "Timeline has no audio clips.";
                return false;
            }

            int w = project.Width, h = project.Height;
            double fps = project.FrameRate <= 0 ? 25 : project.FrameRate;

            var inputs = new List<string>();
            var chains = new List<string>();
            var sb = new StringBuilder();

            // ---------- video ----------
            for (int i = 0; i < videoClips.Count; i++)
            {
                var c = videoClips[i];
                string? src = resolve(c.SourcePath);
                bool isImage = c.MediaType == "Images" || c.MediaType == "Graphics";
                bool isTitle = !isImage && (string.IsNullOrEmpty(c.SourcePath) || src == null || !File.Exists(src));

                if (isImage && src != null && File.Exists(src))
                    sb.Append($"-loop 1 -t {N(c.Duration)} -i \"{src}\" ");
                else if (isTitle)
                    sb.Append($"-f lavfi -t {N(c.Duration)} -i color=c=black:s={w}x{h}:r={N(fps)} ");
                else if (src != null && File.Exists(src))
                    sb.Append($"-ss {N(Math.Max(0, c.InPoint))} -t {N(c.Duration)} -i \"{src}\" ");
                else
                    sb.Append($"-f lavfi -t {N(c.Duration)} -i color=c=black:s={w}x{h}:r={N(fps)} ");

                var f = new List<string>
                {
                    $"scale={w}:{h}:force_original_aspect_ratio=decrease",
                    $"pad={w}:{h}:(ow-iw)/2:(oh-ih)/2",
                    "setsar=1",
                    $"fps={N(fps)}"
                };

                var fx = FilterBuilder.BuildClipFilters(c.Effects);
                if (fx.Length > 0) f.Add(fx);

                double tw = Math.Min(0.6, c.Duration / 2);
                switch (c.TransitionIn?.Kind)   // null = Cut (legacy data)
                {
                    case TransitionKind.Fade:
                    case TransitionKind.CrossDissolve:
                    case TransitionKind.Wipe:
                        f.Add($"fade=t=in:st=0:d={N(tw)}");
                        break;
                    case TransitionKind.DipToBlack:
                        f.Add($"fade=t=in:st=0:d={N(tw)}");
                        break;
                    case TransitionKind.DipToWhite:
                        f.Add($"fade=t=in:st=0:d={N(tw)}:color=white");
                        break;
                }

                if (c.Title != null && !string.IsNullOrWhiteSpace(c.Title.Text))
                {
                    var dt = DrawTextFilter(c.Title, w, h);
                    if (dt != null) f.Add(dt);
                }

                chains.Add($"[{i}:v]" + string.Join(",", f) + $"[v{i}]");
            }

            if (!audioOnly && videoClips.Count > 0 && project.Graphics.Count == 0
                && !project.SubtitleTracks.Any(t => t.Items.Count > 0))
            {
                // fast path: no overlays — concat straight to output
                var labels = string.Join("", videoClips.Select((_, i) => $"[v{i}]"));
                chains.Add($"{labels}concat=n={videoClips.Count}:v=1:a=0[vcat]");
                chains.Add("[vcat]format=yuv420p[vout]");
            }
            // ---------- audio ----------
            bool anySolo = audioClips.Any(a => a.IsSolo);
            int ai = audioOnly ? 0 : videoClips.Count;
            int appendedAudio = 0;
            var aLabels = new List<string>();

            foreach (var c in audioClips)
            {
                string? src = resolve(c.SourcePath);
                if (src == null || !File.Exists(src))
                {
                    ai++;
                    continue;
                }

                sb.Append($"-ss {N(Math.Max(0, c.InPoint))} -t {N(c.Duration)} -i \"{src}\" ");
                appendedAudio++;
                ai++;
                var f = new List<string>();
                double vol = c.Volume / 100.0 * Math.Pow(10, c.GainDb / 20.0);
                if (c.IsMuted || (anySolo && !c.IsSolo)) vol = 0;
                f.Add($"volume={N(vol)}");

                double p = Math.Clamp(c.Pan / 100.0, -1, 1);
                if (Math.Abs(p) > 0.01)
                {
                    double l = 1 - Math.Max(0, p);
                    double r = 1 + Math.Min(0, p);
                    f.Add($"pan=stereo|c0=c0*{N(l)}|c1=c1*{N(r)}");
                }
                if (c.FadeIn > 0.01) f.Add($"afade=t=in:st=0:d={N(c.FadeIn)}");
                if (c.FadeOut > 0.01) f.Add($"afade=t=out:st={N(Math.Max(0, c.Duration - c.FadeOut))}:d={N(c.FadeOut)}");

                int ms = Math.Max(0, (int)Math.Round(c.StartTime * 1000));
                f.Add($"adelay={ms}|{ms}");

                chains.Add($"[{ai - 1}:a]" + string.Join(",", f) + $"[a{ai - 1}]");
                aLabels.Add($"[a{ai - 1}]");
            }

            if (includeAudio && aLabels.Count == 1) chains.Add($"{aLabels[0]}anull[aout]");
            else if (includeAudio && aLabels.Count > 1)
                chains.Add(string.Join("", aLabels) + $"amix=inputs={aLabels.Count}:normalize=0[aout]");

            bool hasAudioMix = includeAudio && aLabels.Count > 0;

            // ---------- graphics overlays + subtitles (Milestone 11) ----------
            // runs after audio so image-layer input indices land after all -i flags
            bool hasOverlays = project.Graphics.Count > 0
                || project.SubtitleTracks.Any(t => t.Items.Count > 0);
            if (!audioOnly && videoClips.Count > 0 && hasOverlays)
            {
                var labels = string.Join("", videoClips.Select((_, i) => $"[v{i}]"));
                chains.Add($"{labels}concat=n={videoClips.Count}:v=1:a=0[vcat]");
                string src = AppendGraphicsAndSubtitles(project, chains, ref sb,
                    "vcat", w, h, videoClips.Count + appendedAudio, resolve);
                chains.Add($"[{src}]format=yuv420p[vout]");
            }

            // ---------- encoder ----------
            string enc;
            string audioMapArgs = hasAudioMix ? " -map \"[aout]\"" : "";
            string audioEnc = hasAudioMix ? AudioEncoderFor(tag) : " -an";

            if (audioOnly)
            {
                enc = tag == "wav"
                    ? "-vn -c:a pcm_s16le"
                    : "-vn -c:a libmp3lame -q:a 2";
                args = $"-y {sb}-filter_complex \"{string.Join(";", chains)}\" -map \"[aout]\" {enc} \"{output}\"";
                return true;
            }

            enc = tag switch
            {
                "mp4_h264" => $"-c:v libx264 -preset medium -crf {crf}{audioEnc}",
                "mp4_hevc" => $"-c:v libx265 -preset medium -crf {crf + 2}{audioEnc}",
                "webm_vp9" => $"-c:v libvpx-vp9 -crf {crf} -b:v 0{audioEnc}",
                "mov_prores" => $"-c:v prores_ks -profile:v 3{(hasAudioMix ? " -c:a pcm_s16le" : " -an")}",
                "png_seq" => "-c:v png",
                _ => $"-c:v libx264 -preset medium -crf {crf}{audioEnc}"
            };

            args = $"-y {sb}-filter_complex \"{string.Join(";", chains)}\" -map \"[vout]\"{audioMapArgs} {enc} \"{output}\"";
            return true;
        }

        private static string AudioEncoderFor(string tag) => tag switch
        {
            "mp4_h264" or "mp4_hevc" => " -c:a aac -b:a 192k",
            "webm_vp9" => " -c:a libopus -b:a 128k",
            "mov_prores" => "",
            _ => " -c:a aac -b:a 192k"
        };

        // ---------- Milestone 11: graphics overlays + burned subtitles ----------

        /// <summary>
        /// Appends drawtext/drawbox/overlay chains for graphics layers and the
        /// subtitle burn-in after the concat result; returns the label that the
        /// next filter stage must read. Image layers become extra inputs whose
        /// indices start at <paramref name="nextInputIndex"/> (after video+audio).
        /// </summary>
        private static string AppendGraphicsAndSubtitles(
            ProjectModel project, List<string> chains, ref StringBuilder sb,
            string sourceLabel, int w, int h, int nextInputIndex,
            Func<string, string?> resolve)
        {
            string cur = sourceLabel;
            int extra = 0;

            var layers = project.Graphics
                .Where(g => g.Visible && g.Duration > 0.01)
                .OrderBy(g => g.Start);

            foreach (var g in layers)
            {
                double st = Math.Max(0, g.Start);
                double en = g.Start + g.Duration;
                if (st >= en) continue;
                string enable = $"enable='between(t,{N(st)},{N(en)})'";
                double op = Math.Clamp(g.Opacity, 0, 1);
                int x = (int)Math.Round(g.PositionX);
                int y = (int)Math.Round(g.PositionY);

                switch (g)
                {
                    case TextLayer t:
                    {
                        var dt = DrawTextFilter(t, enable);
                        if (dt != null) cur = Chain(chains, cur, dt);
                        break;
                    }
                    case ShapeLayer s when s.ShapeType is "Rectangle" or "RoundedRectangle":
                    {
                        int bw = Math.Max(1, (int)(s.Width * s.ScaleX));
                        int bh = Math.Max(1, (int)(s.Height * s.ScaleY));
                        string fill = HexToDrawColor(s.FillColor, op);
                        string box = s.StrokeWidth > 0.5
                            ? $"drawbox=x={x}-w/2:y={y}-h/2:w={bw}:h={bh}:color={fill}:t={Math.Max(1, (int)s.StrokeWidth)}:{enable}"
                            : $"drawbox=x={x}-w/2:y={y}-h/2:w={bw}:h={bh}:color={fill}:t=fill:{enable}";
                        cur = Chain(chains, cur, box);
                        break;
                    }
                    case ImageLayer img:
                    {
                        string? path = resolve?.Invoke(img.ImagePath) ?? img.ImagePath;
                        if (path == null || !File.Exists(path)) break;

                        int idx = extra++;
                        sb.Append($"-i \"{path}\" ");
                        int inputIndex = nextInputIndex + idx;
                        double sx = Math.Max(0.01, g.ScaleX), sy = Math.Max(0.01, g.ScaleY);
                        chains.Add($"[{inputIndex}:v]scale=w=iw*{N(sx)}:h=ih*{N(sy)},format=rgba,colorchannelmixer=aa={N(op)}[ovi{idx}]");

                        string dst = $"gxo{idx}";
                        chains.Add($"[{cur}][ovi{idx}]overlay=x={x}-w/2:y={y}-h/2:{enable}[{dst}]");
                        cur = dst;
                        break;
                    }
                }
            }

            // subtitles burn-in (ASS keeps parsed styling; SRT otherwise)
            var track = project.SubtitleTracks.FirstOrDefault(t => t.Items.Count > 0);
            if (track != null)
            {
                try
                {
                    bool styled = track.Style != null;
                    string ext = styled ? ".ass" : ".srt";
                    string tmp = Path.Combine(Path.GetTempPath(), $"mirror_subs_{Guid.NewGuid():N}{ext}");
                    File.WriteAllText(tmp,
                        styled ? AssParser.Write(track) : SrtParser.Write(track));
                    cur = Chain(chains, cur, $"subtitles='{EscapePath(tmp)}'");
                }
                catch
                {
                    // unreadable temp dir must not kill the export
                }
            }

            return cur;
        }

        private static string Chain(List<string> chains, string src, string filter)
        {
            string dst = $"gfx{chains.Count}";
            chains.Add($"[{src}]{filter}[{dst}]");
            return dst;
        }

        private static string HexToDrawColor(string hex, double opacity)
        {
            var c = hex.TrimStart('#');
            if (c.Length == 8) c = c[2..];
            if (c.Length != 6 || !c.All(Uri.IsHexDigit)) c = "FFFFFF";
            return $"0x{c}@{N(opacity)}";
        }

        private static string? ResolveFontFile()
        {
            string fonts = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            foreach (var f in new[] { "arial.ttf", "segoeui.ttf", "calibri.ttf", "tahoma.ttf" })
            {
                string p = Path.Combine(fonts, "Fonts", f);
                if (File.Exists(p)) return p;
            }
            return null;
        }

        private static string? DrawTextFilter(MirrorStudio.Graphics.Models.TextLayer t, string enable)
        {
            try
            {
                string? font = ResolveFontFile();
                if (font == null) return null;

                string safeText = t.Text.Replace("\r", " ").Replace("\n", " ")
                    .Replace("'", "").Replace("%", "").Replace(":", "").Replace(",", "");
                var tmpText = Path.Combine(Path.GetTempPath(), $"mirror_gfx_{Guid.NewGuid():N}.txt");
                File.WriteAllText(tmpText, safeText);

                var parts = new List<string>
                {
                    $"drawtext=textfile='{EscapePath(tmpText)}'",
                    $"fontfile='{EscapePath(font)}'",
                    $"fontsize={N(t.Style.FontSize)}",
                    $"fontcolor={HexToDrawColor(t.Style.FillColor, Math.Clamp(t.Opacity, 0, 1))}",
                    $"x={N(t.PositionX)}-text_w/2",
                    $"y={N(t.PositionY)}-text_h/2",
                    enable
                };
                if (t.Style.ShadowOpacity > 0.01)
                    parts.Add($"shadowcolor=black@{N(Math.Min(1, t.Style.ShadowOpacity))}:shadowx={Math.Max(1,(int)t.Style.ShadowOffsetX)}:shadowy={Math.Max(1,(int)t.Style.ShadowOffsetY)}");
                if (t.Style.StrokeWidth > 0.5)
                    parts.Add($"bordercolor={HexToDrawColor(t.Style.StrokeColor, 1)}:borderw={Math.Max(1,(int)t.Style.StrokeWidth)}");

                return string.Join(":", parts);
            }
            catch
            {
                return null;
            }
        }

        private static string? DrawTextFilter(TitleModel t, int w, int h)
        {
            try
            {
                string font = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts", "arial.ttf");
                if (!File.Exists(font))
                    font = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts", "segoeui.ttf");
                if (!File.Exists(font)) return null;

                string safeText = t.Text.Replace("\r", " ").Replace("\n", " ").Replace("'", "").Replace("%", "").Replace(":", "").Replace(",", "");
                var tmpText = Path.Combine(Path.GetTempPath(), $"mirror_title_{Guid.NewGuid():N}.txt");
                File.WriteAllText(tmpText, safeText);

                string tf = EscapePath(tmpText);
                string ff = EscapePath(font);

                int x = (int)(t.X / 100.0 * w);
                int y = (int)(t.Y / 100.0 * h);
                double op = Math.Clamp(t.Opacity / 100.0, 0, 1);

                var parts = new List<string>
                {
                    $"drawtext=textfile='{tf}'",
                    $"fontfile='{ff}'",
                    $"fontsize={N(t.FontSize)}",
                    $"fontcolor={t.ColorHex}@{N(op)}",
                    $"x={x - 200}:y={y}"
                };
                if (t.Shadow) parts.Add("shadowcolor=black@0.8:shadowx=2:shadowy=2");
                if (t.Outline) parts.Add("bordercolor=black:borderw=2");

                return string.Join(":", parts);
            }
            catch
            {
                return null;
            }
        }

        private static string EscapePath(string p) =>
            p.Replace("\\", "/").Replace(":", "\\:");
    }
}

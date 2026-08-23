using MirrorStudio.Core.Models;
using System.Linq;

namespace MirrorStudio.Timeline
{
    public static class TimelineEngine
    {
        public static ClipModel? FindClip(TimelineData timeline, string clipId) =>
            timeline.Clips.FirstOrDefault(c => c.Id == clipId);

        public static bool Overlaps(TimelineData timeline, ClipModel moving) =>
            timeline.Clips.Any(c =>
                c.Id != moving.Id &&
                c.TrackKind == moving.TrackKind &&
                c.TrackIndex == moving.TrackIndex &&
                moving.StartTime < c.StartTime + c.Duration &&
                c.StartTime < moving.StartTime + moving.Duration);

        public static ClipModel SplitAt(TimelineData timeline, ClipModel clip, double sequenceTime)
        {
            if (sequenceTime <= clip.StartTime || sequenceTime >= clip.StartTime + clip.Duration)
                return clip;

            double offset = sequenceTime - clip.StartTime;

            var right = new ClipModel
            {
                Name = clip.Name,
                SourcePath = clip.SourcePath,
                MediaType = clip.MediaType,
                TrackKind = clip.TrackKind,
                TrackIndex = clip.TrackIndex,
                StartTime = sequenceTime,
                Duration = clip.Duration - offset,
                InPoint = clip.InPoint + offset,
                OutPoint = clip.OutPoint,
                PositionX = clip.PositionX,
                PositionY = clip.PositionY,
                ScaleX = clip.ScaleX,
                ScaleY = clip.ScaleY,
                Rotation = clip.Rotation,
                Opacity = clip.Opacity,
                Volume = clip.Volume,
                Pan = clip.Pan,
                GainDb = clip.GainDb,
                IsMuted = clip.IsMuted,
                Title = clip.Title == null ? null : new TitleModel
                {
                    Text = clip.Title.Text,
                    FontFamily = clip.Title.FontFamily,
                    FontSize = clip.Title.FontSize,
                    Bold = clip.Title.Bold,
                    Italic = clip.Title.Italic,
                    Alignment = clip.Title.Alignment,
                    X = clip.Title.X,
                    Y = clip.Title.Y,
                    ColorHex = clip.Title.ColorHex,
                    Opacity = clip.Title.Opacity,
                    Shadow = clip.Title.Shadow,
                    Outline = clip.Title.Outline
                }
            };
            right.Effects.AddRange(clip.Effects.Select(e => new EffectModel { Type = e.Type, Amount = e.Amount }));

            clip.Duration = offset;
            clip.OutPoint = clip.InPoint + offset;
            clip.FadeOut = 0;

            timeline.Clips.Add(right);
            return right;
        }

        public static bool Delete(TimelineData timeline, ClipModel clip, bool ripple)
        {
            if (!timeline.Clips.Remove(clip)) return false;

            if (ripple)
            {
                foreach (var later in timeline.Clips
                    .Where(c => c.TrackKind == clip.TrackKind && c.TrackIndex == clip.TrackIndex && c.StartTime >= clip.StartTime)
                    .ToList())
                {
                    later.StartTime -= clip.Duration;
                    if (later.StartTime < 0) later.StartTime = 0;
                }
            }
            return true;
        }

        public static bool MoveTo(TimelineData timeline, ClipModel clip, double newStart, int newTrackIndex)
        {
            double originalStart = clip.StartTime;
            int originalTrack = clip.TrackIndex;

            clip.StartTime = System.Math.Max(0, newStart);
            clip.TrackIndex = System.Math.Clamp(newTrackIndex, 0, clip.TrackKind == TrackKind.Video ? TimelineData.VideoTrackCount - 1 : TimelineData.AudioTrackCount - 1);

            if (Overlaps(timeline, clip))
            {
                clip.StartTime = originalStart;
                clip.TrackIndex = originalTrack;
                return false;
            }
            return true;
        }

        public static void TrimIn(TimelineData timeline, ClipModel clip, double deltaSeconds)
        {
            double newIn = System.Math.Max(0, clip.InPoint + deltaSeconds);
            double applied = newIn - clip.InPoint;
            if (clip.Duration - applied <= 0.05) return;

            clip.InPoint = newIn;
            clip.StartTime += applied;
            clip.Duration -= applied;
        }

        public static void TrimOut(ClipModel clip, double deltaSeconds)
        {
            clip.Duration = System.Math.Max(0.05, clip.Duration + deltaSeconds);
            clip.OutPoint = clip.InPoint + clip.Duration;
        }

        public static void RippleFrom(TimelineData timeline, ClipModel afterClip)
        {
            foreach (var c in timeline.Clips
                .Where(c => c.TrackKind == afterClip.TrackKind && c.TrackIndex == afterClip.TrackIndex && c.StartTime > afterClip.StartTime)
                .ToList())
            {
                c.StartTime = afterClip.StartTime + afterClip.Duration +
                              (c.StartTime - (afterClip.StartTime + afterClip.Duration));
            }
        }
    }
}

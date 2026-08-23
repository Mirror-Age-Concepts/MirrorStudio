using System;
using System.Collections.Generic;

namespace MirrorStudio.Core.Models
{
    public enum TrackKind
    {
        Video,
        Audio
    }

    public enum TransitionKind
    {
        Cut,
        Fade,
        CrossDissolve,
        DipToBlack,
        DipToWhite,
        Wipe
    }

    public class ClipTransition
    {
        public TransitionKind Kind { get; set; } = TransitionKind.Cut;
        public double DurationSeconds { get; set; } = 0.5;
    }

    public class EffectModel
    {
        public string Type { get; set; } = "Brightness";
        public double Amount { get; set; }

        public string Display => $"{Type}: {Amount:0.##}";
    }

    public class TitleModel
    {
        public string Text { get; set; } = "Title";
        public string FontFamily { get; set; } = "Segoe UI";
        public double FontSize { get; set; } = 48;
        public bool Bold { get; set; }
        public bool Italic { get; set; }
        public string Alignment { get; set; } = "Center";
        public double X { get; set; }
        public double Y { get; set; }
        public string ColorHex { get; set; } = "#FFFFFFFF";
        public double Opacity { get; set; } = 100;
        public bool Shadow { get; set; } = true;
        public bool Outline { get; set; }
    }

    public class ClipModel
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public string SourcePath { get; set; } = string.Empty;
        public string MediaType { get; set; } = "video";
        public TrackKind TrackKind { get; set; } = TrackKind.Video;
        public int TrackIndex { get; set; }

        public double StartTime { get; set; }
        public double Duration { get; set; } = 3.0;
        public double InPoint { get; set; }
        public double OutPoint { get; set; } = 3.0;

        public double PositionX { get; set; }
        public double PositionY { get; set; }
        public double ScaleX { get; set; } = 100;
        public double ScaleY { get; set; } = 100;
        public double Rotation { get; set; }
        public double Opacity { get; set; } = 100;

        public double Volume { get; set; } = 100;
        public double Pan { get; set; }
        public double GainDb { get; set; }
        public double FadeIn { get; set; }
        public double FadeOut { get; set; }
        public bool IsMuted { get; set; }
        public bool IsSolo { get; set; }

        public ClipTransition? TransitionIn { get; set; }
        public TitleModel? Title { get; set; }
        public List<EffectModel> Effects { get; set; } = new();

        public string DisplayName => Title != null ? Title.Text : Name;
    }

    public class TimelineData
    {
        public const int VideoTrackCount = 4;
        public const int AudioTrackCount = 4;

        public List<ClipModel> Clips { get; set; } = new();

        public IEnumerable<ClipModel> VideoClips => Clips.Where(c => c.TrackKind == TrackKind.Video);
        public IEnumerable<ClipModel> AudioClips => Clips.Where(c => c.TrackKind == TrackKind.Audio);

        public double SequenceDuration =>
            Clips.Count == 0 ? 0 : Clips.Max(c => c.StartTime + c.Duration);
    }
}

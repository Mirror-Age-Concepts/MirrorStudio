using System;
using System.Collections.Generic;
using MirrorStudio.Core.Enums;

namespace MirrorStudio.Core.Models
{
    /// <summary>A timeline container. Projects currently own one implicit sequence;
    /// this model is the spec-shaped view of it.</summary>
    public class Sequence
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid ProjectId { get; set; }

        public string Name { get; set; } = "Sequence 1";

        public int Width { get; set; } = 1920;

        public int Height { get; set; } = 1080;

        public double FrameRate { get; set; } = 25.0;

        public List<Track> Tracks { get; set; } = new();

        public List<Clip> Clips { get; set; } = new();

        public static Sequence FromProject(ProjectModel p)
        {
            var seq = new Sequence
            {
                Name = "Sequence 1",
                Width = p.Width,
                Height = p.Height,
                FrameRate = p.FrameRate
            };

            for (int i = 0; i < TimelineData.VideoTrackCount; i++)
                seq.Tracks.Add(new Track { Name = $"V{i + 1}", Type = TrackType.Video, Order = i });
            for (int i = 0; i < TimelineData.AudioTrackCount; i++)
                seq.Tracks.Add(new Track { Name = $"A{i + 1}", Type = TrackType.Audio, Order = TimelineData.VideoTrackCount + i });

            foreach (var c in p.Timeline.Clips)
                seq.Clips.Add(Clip.FromClipModel(c));

            return seq;
        }
    }
}

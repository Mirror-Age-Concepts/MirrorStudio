using System;

namespace MirrorStudio.Core.Models
{
    /// <summary>Spec-shaped, non-destructive clip. Bridges to the runtime ClipModel.</summary>
    public class Clip
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid MediaAssetId { get; set; }

        public Guid TrackId { get; set; }

        /// <summary>Start position on the timeline (seconds).</summary>
        public double TimelineStart { get; set; }

        public double Duration { get; set; }

        /// <summary>In point within the source media (seconds).</summary>
        public double SourceStart { get; set; }

        /// <summary>Out point within the source media (seconds).</summary>
        public double SourceEnd { get; set; }

        public double PositionX { get; set; }

        public double PositionY { get; set; }

        /// <summary>Scale factor where 1.0 = 100%.</summary>
        public double ScaleX { get; set; } = 1.0;

        public double ScaleY { get; set; } = 1.0;

        public double Rotation { get; set; }

        /// <summary>Opacity where 1.0 = fully opaque.</summary>
        public double Opacity { get; set; } = 1.0;

        /// <summary>Volume multiplier where 1.0 = unity gain.</summary>
        public double Volume { get; set; } = 1.0;

        // ---------- bridge to the runtime ClipModel ----------

        public static Clip FromClipModel(ClipModel c)
        {
            var guid = Guid.TryParse(c.Id, out var g) && g != Guid.Empty ? g : Guid.NewGuid();
            return new Clip
            {
                Id = guid,
                TimelineStart = c.StartTime,
                Duration = c.Duration,
                SourceStart = c.InPoint,
                SourceEnd = c.OutPoint,
                PositionX = c.PositionX,
                PositionY = c.PositionY,
                ScaleX = c.ScaleX / 100.0,
                ScaleY = c.ScaleY / 100.0,
                Rotation = c.Rotation,
                Opacity = c.Opacity / 100.0,
                Volume = c.Volume / 100.0
            };
        }
    }
}

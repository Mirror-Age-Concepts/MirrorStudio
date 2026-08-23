using System;

namespace MirrorStudio.Core.Models
{
    /// <summary>A single animated property sample: at TimeSec the property equals Value.</summary>
    public class Keyframe
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid ClipId { get; set; }

        /// <summary>Animated property, e.g. "PositionX", "Opacity", "ScaleX", "Volume".</summary>
        public string PropertyName { get; set; } = string.Empty;

        /// <summary>Time relative to clip start (seconds).</summary>
        public double TimeSec { get; set; }

        public double Value { get; set; }

        public string Easing { get; set; } = "Linear";
    }
}

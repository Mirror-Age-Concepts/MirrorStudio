using System;
using MirrorStudio.Core.Enums;

namespace MirrorStudio.Core.Models
{
    public class Track
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid SequenceId { get; set; }

        public string Name { get; set; } = string.Empty;

        public TrackType Type { get; set; }

        /// <summary>Zero-based index within its type (V1 = video 0, A1 = audio 0).</summary>
        public int Order { get; set; }

        public bool IsMuted { get; set; }

        public bool IsLocked { get; set; }

        public bool IsSolo { get; set; }
    }
}

using MirrorStudio.Core.Enums;

namespace MirrorStudio.Video.Models
{
    public class VideoMetadata
    {
        public string FilePath { get; set; } = string.Empty;

        public string FileName { get; set; } = string.Empty;

        public double Duration { get; set; }

        public int Width { get; set; }

        public int Height { get; set; }

        public double FrameRate { get; set; }

        public string VideoCodec { get; set; } = string.Empty;

        public string AudioCodec { get; set; } = string.Empty;

        public long FileSize { get; set; }

        public bool HasVideo { get; set; }

        public bool HasAudio { get; set; }

        /// <summary>High-level classification resolved from the extension and stream layout.</summary>
        public MediaType Type { get; set; } = MediaType.Unknown;
    }
}

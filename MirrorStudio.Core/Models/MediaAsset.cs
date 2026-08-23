using System;
using System.Collections.Generic;
using MirrorStudio.Core.Enums;

namespace MirrorStudio.Core.Models
{
    /// <summary>Spec-shaped media asset with full technical metadata (probed via FFprobe).</summary>
    public class MediaAsset
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public MediaType Type { get; set; } = MediaType.Unknown;

        public string FileName { get; set; } = string.Empty;

        public string FilePath { get; set; } = string.Empty;

        /// <summary>Path relative to the project folder ("Media/Video/clip.mp4").</summary>
        public string RelativePath { get; set; } = string.Empty;

        public string Extension { get; set; } = string.Empty;

        public long FileSize { get; set; }

        public double Duration { get; set; }

        public int Width { get; set; }

        public int Height { get; set; }

        public double FrameRate { get; set; }

        public string Codec { get; set; } = string.Empty;

        public string AudioCodec { get; set; } = string.Empty;

        public bool HasAudio { get; set; }

        public bool HasVideo { get; set; }

        public string ThumbnailPath { get; set; } = string.Empty;

        public DateTime ImportedAt { get; set; } = DateTime.UtcNow;

        // ---------- bridge to the project-file MediaEntry model ----------

        public static MediaType CategoryToType(string category) => category switch
        {
            "Video" => MediaType.Video,
            "Audio" => MediaType.Audio,
            "Images" => MediaType.Image,
            "Graphics" => MediaType.Graphic,
            "Fonts" => MediaType.Font,
            _ => MediaType.Unknown
        };

        public static MediaAsset FromMediaEntry(MediaEntry e, string? absolutePath = null)
        {
            var ext = System.IO.Path.GetExtension(e.FileName);
            return new MediaAsset
            {
                Id = Guid.TryParse(e.Id, out var g) && g != Guid.Empty ? g : Guid.NewGuid(),
                Type = CategoryToType(e.MediaType),
                FileName = e.FileName,
                RelativePath = e.RelativePath,
                FilePath = absolutePath ?? e.RelativePath,
                Extension = ext,
                HasVideo = e.MediaType is "Video" or "Images" or "Graphics",
                HasAudio = e.MediaType is "Audio" or "Video",
                Duration = e.DurationSec,
                Width = e.Width,
                Height = e.Height,
                FrameRate = e.FrameRate,
                Codec = e.Codec,
                ThumbnailPath = e.ThumbnailRelativePath
            };
        }

        public MediaEntry ToMediaEntry()
        {
            var category = Type switch
            {
                MediaType.Video => "Video",
                MediaType.Audio => "Audio",
                MediaType.Image => "Images",
                MediaType.Graphic => "Graphics",
                MediaType.Font => "Fonts",
                _ => !string.IsNullOrEmpty(Extension) &&
                     Extension.ToLowerInvariant() is ".png" or ".svg" or ".webp" or ".ico"
                    ? "Graphics"
                    : HasAudio && HasVideo ? "Video"
                    : HasAudio && !HasVideo ? "Audio"
                    : HasVideo ? "Images" : "Other"
            };

            return new MediaEntry
            {
                Id = Id.ToString(),
                FileName = FileName,
                RelativePath = RelativePath,
                MediaType = category,
                DurationSec = Duration,
                Width = Width,
                Height = Height,
                FrameRate = FrameRate,
                Codec = Codec,
                ThumbnailRelativePath = ThumbnailPath
            };
        }
    }
}

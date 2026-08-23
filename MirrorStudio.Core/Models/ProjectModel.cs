using System;
using System.Collections.Generic;

namespace MirrorStudio.Core.Models
{
    public class ProjectModel
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public double FrameRate { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public ProjectSettings Settings { get; set; }
        public List<ExportPreset> ExportPresets { get; set; }
        public List<string> Sequences { get; set; }
        public List<string> Effects { get; set; }
        public List<string> Transitions { get; set; }
        public List<string> Titles { get; set; }
        public List<string> Subtitles { get; set; }
        public List<string> Exports { get; set; }

        // Media entries for the project
        public List<MediaEntry> Media { get; set; }

        public string AspectRatio { get; set; } = "16:9";
        public int AudioSampleRateHz { get; set; } = 48000;
        public string DefaultExportFormat { get; set; } = "MP4 H.264";
        public TimelineData Timeline { get; set; } = new();

        public ProjectModel()
        {
            Id = string.Empty;
            Name = string.Empty;
            Width = 1920;
            Height = 1080;
            FrameRate = 25.0;
            CreatedAt = DateTime.UtcNow;
            UpdatedAt = DateTime.UtcNow;
            Settings = new ProjectSettings();
            ExportPresets = new List<ExportPreset>();
            Sequences = new List<string>();
            Effects = new List<string>();
            Transitions = new List<string>();
            Titles = new List<string>();
            Subtitles = new List<string>();
            Exports = new List<string>();
            Media = new List<MediaEntry>();
            Timeline = new TimelineData();
        }

        public static string ComputeAspectRatio(int width, int height)
        {
            if (width <= 0 || height <= 0) return "16:9";
            int a = width, b = height;
            while (b != 0) { int t = b; b = a % b; a = t; }
            int g = a;
            if (g == 0) return "16:9";
            int w = width / g, h = height / g;
            if (w > 32 || h > 32)
            {
                double r = (double)width / height;
                if (System.Math.Abs(r - 16.0 / 9) < 0.02) return "16:9";
                if (System.Math.Abs(r - 4.0 / 3) < 0.02) return "4:3";
                if (System.Math.Abs(r - 21.0 / 9) < 0.05) return "21:9";
                if (System.Math.Abs(r - 1.0) < 0.02) return "1:1";
                if (System.Math.Abs(r - 9.0 / 16) < 0.02) return "9:16";
                return $"{width / (double)height:0.00}:1";
            }
            return $"{w}:{h}";
        }
    }

    public class ProjectSettings
    {
        public string DefaultFormat { get; set; }
        public string DefaultVideoCodec { get; set; }
        public string DefaultAudioCodec { get; set; }

        public ProjectSettings()
        {
            DefaultFormat = "mp4";
            DefaultVideoCodec = "h264";
            DefaultAudioCodec = "aac";
        }
    }

    public class MediaEntry
    {
        public string Id { get; set; }
        public string FileName { get; set; }
        public string RelativePath { get; set; }
        public string MediaType { get; set; }

        // probed technical metadata (optional; older project files omit these)
        public double DurationSec { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public double FrameRate { get; set; }
        public string Codec { get; set; } = string.Empty;
        public string ThumbnailRelativePath { get; set; } = string.Empty;
        public bool Probed { get; set; }

        public MediaEntry()
        {
            Id = Guid.NewGuid().ToString();
            FileName = string.Empty;
            RelativePath = string.Empty;
            MediaType = string.Empty;
        }
    }

    public class ExportPreset
    {
        public string Name { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public double FrameRate { get; set; }
        public string Format { get; set; }
        public string VideoCodec { get; set; }
        public string AudioCodec { get; set; }

        public ExportPreset()
        {
            Name = string.Empty;
            Width = 1920;
            Height = 1080;
            FrameRate = 25.0;
            Format = "mp4";
            VideoCodec = "h264";
            AudioCodec = "aac";
        }
    }
}

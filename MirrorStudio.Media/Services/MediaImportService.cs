using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using MirrorStudio.Core.Enums;
using MirrorStudio.Core.Models;
using MirrorStudio.Video.Models;
using MirrorStudio.Video.Services;

namespace MirrorStudio.Media.Services
{
    /// <summary>
    /// Import workflow: file validation → FFprobe → thumbnail → MediaAsset.
    /// The source file is copied into the project's Media layout so projects are self-contained.
    /// </summary>
    public class MediaImportService
    {
        private readonly string _ffprobePath;
        private readonly string _ffmpegPath;

        public MediaImportService(string ffmpegPath = "ffmpeg", string ffprobePath = "ffprobe")
        {
            _ffmpegPath = ffmpegPath;
            _ffprobePath = ffprobePath;
        }

        public async Task<MediaAsset> ImportAsync(string filePath, string projectDirectory)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Media file not found.", filePath);

            if (string.IsNullOrWhiteSpace(projectDirectory))
                throw new ArgumentException("Project directory is required.", nameof(projectDirectory));

            var ffprobe = new FfprobeService(MediaEngineLocator.ResolveFfprobe(_ffprobePath));
            VideoMetadata metadata;
            try
            {
                metadata = await ffprobe.GetMetadataAsync(filePath);
            }
            catch
            {
                // engine missing/unreadable — import anyway with extension-based defaults
                metadata = new VideoMetadata { FileName = Path.GetFileName(filePath) };
            }
            metadata.Type = TypeFromExtension(filePath, metadata);
            if (metadata.FileSize == 0 && File.Exists(filePath))
                metadata.FileSize = new FileInfo(filePath).Length;

            // copy into the project layout: Media/<Category>/<file>
            var subfolder = metadata.Type switch
            {
                MediaType.Video => "Media/Video",
                MediaType.Audio => "Media/Audio",
                MediaType.Graphic => "Media/Graphics",
                MediaType.Image => "Media/Images",
                MediaType.Font => "Media/Fonts",
                _ => "Media/Other"
            };
            var destFolder = Path.Combine(projectDirectory, subfolder.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(destFolder);

            var fileName = Path.GetFileName(filePath);
            var destPath = Path.Combine(destFolder, fileName);

            if (!string.Equals(Path.GetFullPath(filePath), Path.GetFullPath(destPath), StringComparison.OrdinalIgnoreCase))
            {
                try { File.Copy(filePath, destPath, overwrite: true); }
                catch { /* fall back to original location on copy failure */ destPath = filePath; }
            }

            var asset = new MediaAsset
            {
                Type = metadata.Type,
                FileName = metadata.FileName,
                FilePath = destPath,
                RelativePath = $"{subfolder}/{fileName}".Replace('\\', '/'),
                Extension = Path.GetExtension(filePath),
                FileSize = File.Exists(destPath) ? new FileInfo(destPath).Length : metadata.FileSize,
                Duration = metadata.Duration,
                Width = metadata.Width,
                Height = metadata.Height,
                FrameRate = metadata.FrameRate,
                Codec = metadata.VideoCodec,
                AudioCodec = metadata.AudioCodec,
                HasVideo = metadata.HasVideo,
                HasAudio = metadata.HasAudio
            };

            asset.ThumbnailPath = await GenerateThumbnailAsync(asset, Path.Combine(projectDirectory, "Thumbnails"))
                ?? string.Empty;

            return asset;
        }

        public async Task<IReadOnlyList<MediaAsset>> ImportManyAsync(IEnumerable<string> filePaths, string projectDirectory)
        {
            var results = new List<MediaAsset>();
            foreach (var path in filePaths)
            {
                try { results.Add(await ImportAsync(path, projectDirectory)); }
                catch { /* skip unreadable files, continue importing */ }
            }
            return results;
        }

        public async Task<string?> GenerateThumbnailAsync(MediaAsset asset, string outputDirectory)
        {
            double at = asset.Duration > 3 ? 1.0 : 0;
            Directory.CreateDirectory(outputDirectory);

            var thumbName = $"{Path.GetFileNameWithoutExtension(asset.FileName):N}.jpg";
            var thumbPath = Path.Combine(outputDirectory, thumbName);

            var thumbs = new ThumbnailService(MediaEngineLocator.ResolveFfmpeg(_ffmpegPath));
            var written = await thumbs.TryGenerateAsync(asset.FilePath, thumbPath, at);
            if (written == null) return null;

            // store project-relative so .msp bundles stay portable
            try
            {
                var rel = Path.GetRelativePath(outputDirectory, written).Replace('\\', '/');
                if (!string.IsNullOrEmpty(rel) && !rel.Contains(".."))
                    return $"Thumbnails/{rel}";
            }
            catch
            {
                // fall through to absolute path
            }
            return Path.GetFullPath(written);
        }

        private static MediaType TypeFromExtension(string filePath, VideoMetadata metadata)
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (ext is ".png" or ".svg" or ".webp" or ".ico") return MediaType.Graphic;
            if (ext is ".ttf" or ".otf" or ".woff" or ".woff2") return MediaType.Font;

            // probed streams win
            if (metadata.HasVideo && !FfprobeService.IsStillCodec(metadata.VideoCodec)) return MediaType.Video;
            if (metadata.HasAudio && !metadata.HasVideo) return MediaType.Audio;
            if (metadata.HasVideo) return MediaType.Image;

            // fallback when probing failed: classify by extension
            if (ext is ".mp3" or ".wav" or ".aac" or ".flac" or ".ogg" or ".m4a" or ".wma") return MediaType.Audio;
            if (ext is ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".tif" or ".tiff") return MediaType.Image;
            if (ext is ".mp4" or ".mov" or ".avi" or ".mkv" or ".webm" or ".wmv") return MediaType.Video;
            return MediaType.Unknown;
        }
    }
}

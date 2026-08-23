namespace MirrorStudio.Core.Enums
{
    /// <summary>High-level classification used by the media browser and importer.</summary>
    public enum MediaType
    {
        Unknown,
        Video,
        Audio,
        Image,
        Graphic,
        Font,
        Title
    }

    public static class MediaTypeExtensions
    {
        public static MediaType FromExtension(string extension)
        {
            return extension.TrimStart('.').ToLowerInvariant() switch
            {
                "mp4" or "mov" or "avi" or "mkv" or "webm" or "wmv" or "m4v" or "mpg" or "mpeg" => MediaType.Video,
                "mp3" or "wav" or "aac" or "flac" or "ogg" or "m4a" or "wma" or "opus" => MediaType.Audio,
                "jpg" or "jpeg" or "bmp" or "gif" or "tif" or "tiff" => MediaType.Image,
                "png" or "svg" or "webp" or "ico" => MediaType.Graphic,
                "ttf" or "otf" or "woff" or "woff2" => MediaType.Font,
                _ => MediaType.Unknown
            };
        }

        /// <summary>Matches the string categories stored in project files ("Video", "Audio", "Images", ...).</summary>
        public static string ToProjectCategory(this MediaType t) => t switch
        {
            MediaType.Video => "Video",
            MediaType.Audio => "Audio",
            MediaType.Image => "Images",
            MediaType.Graphic => "Graphics",
            MediaType.Font => "Fonts",
            _ => "Other"
        };

        public static string Subfolder(this MediaType t) => t switch
        {
            MediaType.Video => "Media/Video",
            MediaType.Audio => "Media/Audio",
            MediaType.Image => "Media/Images",
            MediaType.Graphic => "Media/Graphics",
            MediaType.Font => "Media/Fonts",
            _ => "Media/Other"
        };
    }
}

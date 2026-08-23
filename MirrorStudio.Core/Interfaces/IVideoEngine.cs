using System.Threading.Tasks;
using MirrorStudio.Core.Models;

namespace MirrorStudio.Core.Interfaces
{
    /// <summary>Video decode/probe/frame services (FFmpeg-backed).</summary>
    public interface IVideoEngine
    {
        Task<MediaAsset?> ProbeAsync(string filePath);

        Task<bool> GenerateThumbnailAsync(string filePath, double atSeconds, string outputPath, int maxWidth = 320);

        /// <summary>Extracts a single preview frame; returns the written file path or null.</summary>
        Task<string?> ExtractFrameAsync(string filePath, double seconds, string outputPath, int width = 640);

        Task<bool> GenerateProxyAsync(string filePath, string outputPath);
    }
}

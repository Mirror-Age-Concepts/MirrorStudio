using System.Threading.Tasks;
using MirrorStudio.Core.Models;

namespace MirrorStudio.Core.Interfaces
{
    public interface IImageEngine
    {
        Task<MediaAsset?> ProbeAsync(string filePath);

        Task<string?> GenerateThumbnailAsync(string filePath, string outputPath, int maxWidth = 320);

        Task<bool> ResizeAsync(string filePath, string outputPath, int width, int height);

        /// <summary>Converts an image into a video segment of the given duration (for timeline rendering).</summary>
        Task<bool> ToVideoSegmentAsync(string filePath, string outputPath, double durationSeconds, double fps);
    }
}

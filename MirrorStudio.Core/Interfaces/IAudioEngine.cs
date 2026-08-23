using System.Threading.Tasks;
using MirrorStudio.Core.Models;

namespace MirrorStudio.Core.Interfaces
{
    public interface IAudioEngine
    {
        Task<MediaAsset?> ProbeAsync(string filePath);

        /// <summary>Writes normalized peak data ("0.0-1.0 per sample, one line per point") for waveform display.</summary>
        Task<string?> ExtractWaveformPeaksAsync(string filePath, string outputPath, int pointsPerSecond = 10);

        Task<bool> ApplyVolumeFadeAsync(string filePath, string outputPath, double volumeDb, double fadeInSec, double fadeOutSec);
    }
}

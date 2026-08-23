using System.Collections.Generic;
using System.Threading.Tasks;
using MirrorStudio.Core.Models;

namespace MirrorStudio.Core.Interfaces
{
    public interface IMediaImporter
    {
        /// <summary>Copies media into the project layout, probes metadata and generates a thumbnail.</summary>
        Task<MediaAsset?> ImportAsync(string sourcePath, string projectFolder);

        Task<IReadOnlyList<MediaAsset>> ImportManyAsync(IEnumerable<string> sourcePaths, string projectFolder);

        Task<MediaAsset?> ProbeAsync(string filePath);

        Task<string?> GenerateThumbnailAsync(MediaAsset asset, string outputFolder);
    }
}

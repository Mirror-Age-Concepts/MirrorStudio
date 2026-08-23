using MirrorStudio.Core.Projects;
using MirrorStudio.Export.Models;

namespace MirrorStudio.Export.Interfaces;

public interface IRenderEngine
{
    Task RenderAsync(
        Project project,
        ExportSettings settings,
        IProgress<ExportProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
